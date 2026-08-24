using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobAlign.Infrastructure.Ai;

/// <summary>
/// Thin wrapper over the Anthropic Messages API (<c>POST /v1/messages</c>).
/// There is no official .NET SDK, so a typed <see cref="HttpClient"/> is the right amount
/// of machinery (role-f handout, "Provider"). This is the only class in the project that
/// knows the wire format; <see cref="AiExtractor"/> and <see cref="AiFeedbackGenerator"/>
/// depend on <see cref="AnthropicResult"/>, not on HTTP or JSON shapes.
///
/// Never throws for a provider problem (NFR-06) — every failure mode (timeout, non-2xx,
/// missing key, cancelled) comes back as <see cref="AnthropicResult.Failure"/> with a
/// reason a caller can store and show. One retry on a timeout or 429/5xx, with a short
/// fixed backoff — not more, since NFR-01 gives the whole extraction 10 seconds.
/// </summary>
public sealed class AnthropicClient
{
    private readonly HttpClient _httpClient;
    private readonly AiClientOptions _options;
    private readonly ILogger<AnthropicClient> _logger;

    private static readonly TimeSpan RetryBackoff = TimeSpan.FromMilliseconds(500);

    public AnthropicClient(HttpClient httpClient, IOptions<AiClientOptions> options, ILogger<AnthropicClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Sends one message and returns the concatenated text content of the reply.
    /// Retries exactly once on a timeout, a 429, or a 5xx.
    /// </summary>
    public async Task<AnthropicResult> SendAsync(
        string systemPrompt,
        string userMessage,
        int maxTokens,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return AnthropicResult.Failure("No Anthropic API key is configured.");

        var attempt = 0;

        while (true)
        {
            attempt++;

            var (result, shouldRetry) = await SendOnceAsync(systemPrompt, userMessage, maxTokens, cancellationToken);

            if (!shouldRetry || attempt > 1)
                return result;

            await Task.Delay(RetryBackoff, cancellationToken);
        }
    }

    private async Task<(AnthropicResult Result, bool ShouldRetry)> SendOnceAsync(
        string systemPrompt,
        string userMessage,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var requestBody = new
            {
                model = _options.Model,
                max_tokens = maxTokens,
                system = systemPrompt,
                messages = new[]
                {
                    new { role = "user", content = userMessage }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-api-key", _options.ApiKey);
            request.Headers.Add("anthropic-version", _options.AnthropicVersion);

            using var response = await _httpClient.SendAsync(request, linkedCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var isRetryable = response.StatusCode == (HttpStatusCode)429 || (int)response.StatusCode >= 500;
                var reason = $"Anthropic API returned {(int)response.StatusCode} {response.StatusCode}.";
                _logger.LogWarning("Anthropic API call failed: {Reason}", reason);
                return (AnthropicResult.Failure(reason), isRetryable);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var text = ExtractText(body);

            return text is null
                ? (AnthropicResult.Failure("Anthropic API response had no text content."), false)
                : (AnthropicResult.Success(text), false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning("Anthropic API call timed out after {Timeout}s.", _options.TimeoutSeconds);
            return (AnthropicResult.Failure("The AI service timed out."), true);
        }
        catch (OperationCanceledException)
        {
            // Caller-requested cancellation, not a timeout — do not retry, do not swallow intent.
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Anthropic API call failed with a network error.");
            return (AnthropicResult.Failure("Could not reach the AI service."), true);
        }
    }

    /// <summary>
    /// Pulls the concatenated text out of a Messages API response body:
    /// <c>{ "content": [ { "type": "text", "text": "..." }, ... ] }</c>.
    /// </summary>
    private static string? ExtractText(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);

            if (!doc.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                return null;

            var sb = new StringBuilder();
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text"
                    && block.TryGetProperty("text", out var text))
                {
                    sb.Append(text.GetString());
                }
            }

            var result = sb.ToString();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Outcome of one call to the Anthropic API. Failure is a normal result, not an exception.</summary>
public sealed class AnthropicResult
{
    public bool Succeeded { get; private init; }
    public string? Text { get; private init; }
    public string? FailureReason { get; private init; }

    public static AnthropicResult Success(string text) => new() { Succeeded = true, Text = text };
    public static AnthropicResult Failure(string reason) => new() { Succeeded = false, FailureReason = reason };
}
