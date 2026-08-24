using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobAlign.Infrastructure.Ai;

/// <summary>
/// Thin wrapper over the Gemini generateContent API
/// (<c>POST {BaseUrl}/models/{model}:generateContent</c>).
///
/// This is the only class that knows Gemini's wire format; everything above it depends on
/// <see cref="IAiChatClient"/>. Mirrors <see cref="AnthropicClient"/> deliberately — same
/// retry rule, same "never throw for a provider problem" contract (NFR-06) — so the two are
/// interchangeable and behave the same way when things go wrong.
///
/// Two shape differences from Anthropic are worth knowing: the system prompt is its own
/// <c>systemInstruction</c> field rather than a top-level string, and the model name goes
/// in the URL rather than the body.
/// </summary>
public sealed class GeminiClient : IAiChatClient
{
    private readonly HttpClient _httpClient;
    private readonly AiClientOptions _options;
    private readonly ILogger<GeminiClient> _logger;

    private static readonly TimeSpan RetryBackoff = TimeSpan.FromMilliseconds(500);

    public GeminiClient(HttpClient httpClient, IOptions<AiClientOptions> options, ILogger<GeminiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderName => "Gemini";

    public async Task<AiResult> SendAsync(
        string systemPrompt,
        string userMessage,
        int maxTokens,
        AiResponseFormat responseFormat,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return AiResult.Failure("No Gemini API key is configured.");

        var attempt = 0;

        while (true)
        {
            attempt++;

            var (result, shouldRetry) = await SendOnceAsync(
                systemPrompt, userMessage, maxTokens, responseFormat, cancellationToken);

            if (!shouldRetry || attempt > 1)
                return result;

            await Task.Delay(RetryBackoff, cancellationToken);
        }
    }

    private async Task<(AiResult Result, bool ShouldRetry)> SendOnceAsync(
        string systemPrompt,
        string userMessage,
        int maxTokens,
        AiResponseFormat responseFormat,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var generationConfig = new Dictionary<string, object>
            {
                ["maxOutputTokens"] = maxTokens
            };

            if (responseFormat == AiResponseFormat.Json)
            {
                // Constrained decoding: Gemini enforces valid JSON rather than leaving it to
                // the prompt. Temperature 0 alongside it, so the same posting extracts the
                // same way twice — a stored extraction has to stay reproducible (NFR-08).
                generationConfig["responseMimeType"] = "application/json";
                generationConfig["temperature"] = 0;
            }

            // Thinking models spend maxOutputTokens on reasoning before they emit anything,
            // so a budget that looks generous can still truncate the answer. Left unset by
            // default because not every model accepts the field; set Ai:ThinkingBudget to 0
            // to spend the whole ceiling on output.
            if (_options.ThinkingBudget is { } budget)
                generationConfig["thinkingConfig"] = new Dictionary<string, object> { ["thinkingBudget"] = budget };

            var requestBody = new
            {
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[]
                {
                    new { role = "user", parts = new[] { new { text = userMessage } } }
                },
                generationConfig
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint())
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };

            // Header, not the ?key= query parameter Google's quickstarts use. A key in a URL
            // ends up in proxy logs, browser history and crash reports; a header does not.
            request.Headers.Add("x-goog-api-key", _options.ApiKey);

            using var response = await _httpClient.SendAsync(request, linkedCts.Token);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var isRetryable = response.StatusCode == (HttpStatusCode)429 || (int)response.StatusCode >= 500;
                var detail = DescribeError(body);
                var reason = $"Gemini API returned {(int)response.StatusCode} {response.StatusCode}."
                             + (detail is null ? string.Empty : $" {detail}");
                _logger.LogWarning("Gemini API call failed: {Reason}", reason);
                return (AiResult.Failure(reason), isRetryable);
            }

            // Truncation is checked before the text is used. A cut-off reply usually still
            // carries partial output, and passing that upstream turns a clear "ran out of
            // room" into an opaque "could not be parsed" three layers away.
            if (WasTruncated(body))
                return (AiResult.Failure(
                    "The AI service ran out of output tokens before it finished the response. "
                    + "Raise Ai:MaxOutputTokens, or set Ai:ThinkingBudget to 0 so the budget "
                    + "goes to the answer rather than to reasoning."), false);

            var text = ReadText(body);

            return text is null
                ? (AiResult.Failure(DescribeEmptyResponse(body)), false)
                : (AiResult.Success(text), false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning("Gemini API call timed out after {Timeout}s.", _options.TimeoutSeconds);
            return (AiResult.Failure("The AI service timed out."), true);
        }
        catch (OperationCanceledException)
        {
            // Caller-requested cancellation, not a timeout — do not retry, do not swallow intent.
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Gemini API call failed with a network error.");
            return (AiResult.Failure("Could not reach the AI service."), true);
        }
    }

    /// <summary>
    /// Gemini puts the model and the method in the path, so the endpoint is built per call
    /// rather than configured whole: <c>{BaseUrl}/models/{model}:generateContent</c>.
    /// </summary>
    private string BuildEndpoint()
    {
        var baseUrl = _options.ResolveBaseUrl().TrimEnd('/');
        return $"{baseUrl}/models/{_options.ResolveModel()}:generateContent";
    }

    /// <summary>
    /// Concatenates the text parts of the first candidate:
    /// <c>{ "candidates": [ { "content": { "parts": [ { "text": "..." } ] } } ] }</c>.
    /// Returns null when the reply carries no usable text, which is a normal outcome when
    /// the prompt was blocked or the token ceiling was hit before anything was emitted.
    /// </summary>
    private static string? ReadText(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);

            if (!doc.RootElement.TryGetProperty("candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array
                || candidates.GetArrayLength() == 0)
            {
                return null;
            }

            var first = candidates[0];

            if (!first.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var sb = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    sb.Append(text.GetString());
            }

            var result = sb.ToString();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when the first candidate stopped because it hit the token ceiling, whether or
    /// not it managed to emit anything first.
    /// </summary>
    private static bool WasTruncated(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);

            return doc.RootElement.TryGetProperty("candidates", out var candidates)
                   && candidates.ValueKind == JsonValueKind.Array
                   && candidates.GetArrayLength() > 0
                   && candidates[0].TryGetProperty("finishReason", out var finish)
                   && string.Equals(finish.GetString(), "MAX_TOKENS", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Turns an empty-but-successful reply into a reason worth storing. A blocked prompt and
    /// a truncated one are different problems, and FR-19 wants the candidate told which.
    /// </summary>
    private static string DescribeEmptyResponse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);

            if (doc.RootElement.TryGetProperty("promptFeedback", out var feedback)
                && feedback.TryGetProperty("blockReason", out var blockReason))
            {
                return $"The AI service blocked the request ({blockReason.GetString()}).";
            }

            if (doc.RootElement.TryGetProperty("candidates", out var candidates)
                && candidates.ValueKind == JsonValueKind.Array
                && candidates.GetArrayLength() > 0
                && candidates[0].TryGetProperty("finishReason", out var finish)
                && finish.GetString() is { } reason
                && !string.Equals(reason, "STOP", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(reason, "MAX_TOKENS", StringComparison.OrdinalIgnoreCase)
                    ? "The AI service response was cut off before it produced anything usable."
                    : $"The AI service stopped early ({reason}).";
            }
        }
        catch (JsonException)
        {
            // Fall through to the generic message.
        }

        return "Gemini API response had no text content.";
    }

    /// <summary>
    /// Pulls the useful part out of Gemini's error envelope. The top-level message is often
    /// only "Request contains an invalid argument", which names nothing — the field that
    /// actually offended is in error.details[].fieldViolations. Without those, a 400 says
    /// only that something is wrong somewhere, which is how a bad generationConfig key can
    /// cost an afternoon.
    /// </summary>
    private static string? DescribeError(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);

            if (!doc.RootElement.TryGetProperty("error", out var error))
                return null;

            var parts = new List<string>();

            if (error.TryGetProperty("message", out var message) && message.GetString() is { } text)
                parts.Add(text);

            foreach (var violation in FieldViolations(error))
                parts.Add(violation);

            return parts.Count == 0 ? null : string.Join(" ", parts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> FieldViolations(JsonElement error)
    {
        if (!error.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var detail in details.EnumerateArray())
        {
            if (!detail.TryGetProperty("fieldViolations", out var violations)
                || violations.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var violation in violations.EnumerateArray())
            {
                var field = violation.TryGetProperty("field", out var f) ? f.GetString() : null;
                var description = violation.TryGetProperty("description", out var d) ? d.GetString() : null;

                if (field is not null || description is not null)
                    yield return $"[{field ?? "?"}: {description ?? "no description"}]";
            }
        }
    }
}
