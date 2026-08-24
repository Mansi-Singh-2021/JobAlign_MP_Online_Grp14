using System.Net;
using System.Text;
using System.Text.Json;
using JobAlign.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobAlign.Tests;

/// <summary>
/// GeminiClient against a fake HttpMessageHandler — never the real API. Covers the wire
/// format (which is the only thing this class exists to know) and the NFR-06 contract that
/// a provider problem is a returned failure, never a thrown exception.
/// </summary>
public class GeminiClientTests
{
    private static GeminiClient BuildClient(
        HttpMessageHandler handler,
        string? apiKey = "test-key",
        int timeoutSeconds = 30)
    {
        var options = Options.Create(new AiClientOptions
        {
            Provider = AiProvider.Gemini,
            ApiKey = apiKey,
            Model = "gemini-3.6-flash",
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
            TimeoutSeconds = timeoutSeconds
        });

        return new GeminiClient(new HttpClient(handler), options, NullLogger<GeminiClient>.Instance);
    }

    private static IOptions<AiClientOptions> TestOptions() =>
        Options.Create(new AiClientOptions { MaxOutputTokens = 2048 });

    /// <summary>Wraps model output the way the real generateContent endpoint does.</summary>
    private static HttpResponseMessage GeminiEnvelope(string modelText, string finishReason = "STOP")
    {
        var envelope = new
        {
            candidates = new[]
            {
                new
                {
                    content = new { role = "model", parts = new[] { new { text = modelText } } },
                    finishReason
                }
            }
        };

        return Json(HttpStatusCode.OK, JsonSerializer.Serialize(envelope));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // ---------------------------------------------------------------- wire format

    [Fact]
    public async Task Puts_the_model_and_method_in_the_url_and_the_key_in_a_header()
    {
        HttpRequestMessage? seen = null;
        var handler = new Recorder((req, _) =>
        {
            seen = req;
            return Task.FromResult(GeminiEnvelope("ok"));
        });

        await BuildClient(handler).SendAsync("sys", "user", 100, AiResponseFormat.Text);

        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.6-flash:generateContent",
            seen!.RequestUri!.ToString());

        Assert.Equal("test-key", Assert.Single(seen.Headers.GetValues("x-goog-api-key")));

        // A key in the query string leaks into proxy logs and browser history.
        Assert.DoesNotContain("key=", seen.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sends_the_system_prompt_as_systemInstruction_not_as_a_user_turn()
    {
        string? body = null;
        var handler = new Recorder(async (req, ct) =>
        {
            body = await req.Content!.ReadAsStringAsync(ct);
            return GeminiEnvelope("ok");
        });

        await BuildClient(handler).SendAsync("SYSTEM-PROMPT", "USER-MESSAGE", 100, AiResponseFormat.Text);

        using var doc = JsonDocument.Parse(body!);
        Assert.Equal(
            "SYSTEM-PROMPT",
            doc.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal(
            "USER-MESSAGE",
            doc.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("user", doc.RootElement.GetProperty("contents")[0].GetProperty("role").GetString());
        Assert.Equal(100, doc.RootElement.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32());
    }

    [Theory]
    [InlineData(AiResponseFormat.Json, true)]
    [InlineData(AiResponseFormat.Text, false)]
    public async Task Asks_for_constrained_json_only_when_the_caller_wants_json(
        AiResponseFormat format, bool expectJsonMode)
    {
        string? body = null;
        var handler = new Recorder(async (req, ct) =>
        {
            body = await req.Content!.ReadAsStringAsync(ct);
            return GeminiEnvelope("{}");
        });

        await BuildClient(handler).SendAsync("sys", "user", 100, format);

        using var doc = JsonDocument.Parse(body!);
        var config = doc.RootElement.GetProperty("generationConfig");

        Assert.Equal(expectJsonMode, config.TryGetProperty("responseMimeType", out var mime));
        if (expectJsonMode)
        {
            Assert.Equal("application/json", mime.GetString());
            // Reproducibility: the same posting must extract the same way twice (NFR-08).
            Assert.Equal(0, config.GetProperty("temperature").GetInt32());
        }
    }

    // ---------------------------------------------------------------- responses

    [Fact]
    public async Task Concatenates_every_text_part_of_the_first_candidate()
    {
        var envelope = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new { content = new { parts = new[] { new { text = "one " }, new { text = "two" } } } }
            }
        });
        var handler = new Recorder((_, _) => Task.FromResult(Json(HttpStatusCode.OK, envelope)));

        var result = await BuildClient(handler).SendAsync("sys", "user", 100, AiResponseFormat.Text);

        Assert.True(result.Succeeded);
        Assert.Equal("one two", result.Text);
    }

    [Fact]
    public async Task Missing_key_fails_without_calling_the_api()
    {
        var called = false;
        var handler = new Recorder((_, _) =>
        {
            called = true;
            return Task.FromResult(GeminiEnvelope("ok"));
        });

        var result = await BuildClient(handler, apiKey: null).SendAsync("sys", "user", 100, AiResponseFormat.Text);

        Assert.False(result.Succeeded);
        Assert.False(called);
        Assert.Contains("no gemini api key", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- failure modes

    [Fact]
    public async Task Surfaces_the_providers_error_message_on_a_non_success_status()
    {
        var error = JsonSerializer.Serialize(new
        {
            error = new { code = 400, message = "API key not valid.", status = "INVALID_ARGUMENT" }
        });
        var handler = new Recorder((_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest, error)));

        var result = await BuildClient(handler).SendAsync("sys", "user", 100, AiResponseFormat.Text);

        Assert.False(result.Succeeded);
        Assert.Contains("400", result.FailureReason!);
        Assert.Contains("API key not valid.", result.FailureReason!);
    }

    [Fact]
    public async Task A_400_names_the_field_that_offended()
    {
        // Gemini's top-level message is often just "Request contains an invalid argument",
        // which is useless on its own. The field is in details[].fieldViolations.
        var error = JsonSerializer.Serialize(new
        {
            error = new
            {
                code = 400,
                message = "Request contains an invalid argument.",
                status = "INVALID_ARGUMENT",
                details = new object[]
                {
                    new
                    {
                        fieldViolations = new[]
                        {
                            new
                            {
                                field = "generationConfig.thinking_config",
                                description = "Thinking budget is not supported for this model."
                            }
                        }
                    }
                }
            }
        });
        var handler = new Recorder((_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest, error)));

        var result = await BuildClient(handler).SendAsync("sys", "user", 100, AiResponseFormat.Json);

        Assert.False(result.Succeeded);
        Assert.Contains("generationConfig.thinking_config", result.FailureReason!, StringComparison.Ordinal);
        Assert.Contains("not supported for this model", result.FailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_blocked_prompt_is_reported_as_blocked_not_as_empty()
    {
        var blocked = JsonSerializer.Serialize(new
        {
            promptFeedback = new { blockReason = "SAFETY" }
        });
        var handler = new Recorder((_, _) => Task.FromResult(Json(HttpStatusCode.OK, blocked)));

        var result = await BuildClient(handler).SendAsync("sys", "user", 100, AiResponseFormat.Text);

        Assert.False(result.Succeeded);
        Assert.Contains("blocked", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SAFETY", result.FailureReason!);
    }

    [Fact]
    public async Task A_truncated_reply_says_so_rather_than_reporting_no_content()
    {
        // finishReason MAX_TOKENS with no parts: the ceiling was hit before anything was emitted.
        var truncated = JsonSerializer.Serialize(new
        {
            candidates = new[] { new { finishReason = "MAX_TOKENS" } }
        });
        var handler = new Recorder((_, _) => Task.FromResult(Json(HttpStatusCode.OK, truncated)));

        var result = await BuildClient(handler).SendAsync("sys", "user", 5, AiResponseFormat.Text);

        Assert.False(result.Succeeded);
        Assert.Contains("ran out of output tokens", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Partial_json_from_a_truncated_reply_is_reported_as_truncation()
    {
        // The real failure this covers: the model emitted half an object before hitting the
        // ceiling. Handing that upstream turns "ran out of room" into "could not be parsed"
        // three layers away, which says nothing about how to fix it.
        var handler = new Recorder((_, _) =>
            Task.FromResult(GeminiEnvelope("{\"jobTitle\": \"Senior .NET Dev", finishReason: "MAX_TOKENS")));

        var result = await BuildClient(handler).SendAsync("sys", "user", 10, AiResponseFormat.Json);

        Assert.False(result.Succeeded);
        Assert.Contains("ran out of output tokens", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ThinkingBudget", result.FailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Thinking_budget_is_omitted_unless_configured()
    {
        string? body = null;
        var handler = new Recorder(async (req, ct) =>
        {
            body = await req.Content!.ReadAsStringAsync(ct);
            return GeminiEnvelope("{}");
        });

        await BuildClient(handler).SendAsync("sys", "user", 100, AiResponseFormat.Json);

        using var doc = JsonDocument.Parse(body!);
        Assert.False(doc.RootElement.GetProperty("generationConfig").TryGetProperty("thinkingConfig", out _));
    }

    [Fact]
    public async Task Thinking_budget_is_sent_when_configured()
    {
        string? body = null;
        var handler = new Recorder(async (req, ct) =>
        {
            body = await req.Content!.ReadAsStringAsync(ct);
            return GeminiEnvelope("{}");
        });

        var options = Options.Create(new AiClientOptions
        {
            Provider = AiProvider.Gemini,
            ApiKey = "test-key",
            Model = "gemini-3.6-flash",
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
            ThinkingBudget = 0
        });
        var client = new GeminiClient(new HttpClient(handler), options, NullLogger<GeminiClient>.Instance);

        await client.SendAsync("sys", "user", 100, AiResponseFormat.Json);

        using var doc = JsonDocument.Parse(body!);
        Assert.Equal(
            0,
            doc.RootElement.GetProperty("generationConfig").GetProperty("thinkingConfig")
                .GetProperty("thinkingBudget").GetInt32());
    }

    [Fact]
    public async Task Retries_once_on_a_500_then_gives_up()
    {
        var attempts = 0;
        var handler = new Recorder((_, _) =>
        {
            attempts++;
            return Task.FromResult(Json(HttpStatusCode.InternalServerError, "{}"));
        });

        var result = await BuildClient(handler).SendAsync("sys", "user", 100, AiResponseFormat.Text);

        Assert.False(result.Succeeded);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Does_not_retry_a_400()
    {
        var attempts = 0;
        var handler = new Recorder((_, _) =>
        {
            attempts++;
            return Task.FromResult(Json(HttpStatusCode.BadRequest, "{}"));
        });

        await BuildClient(handler).SendAsync("sys", "user", 100, AiResponseFormat.Text);

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task A_timeout_is_a_failure_not_an_exception()
    {
        var handler = new Recorder(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return GeminiEnvelope("never gets here");
        });

        var result = await BuildClient(handler, timeoutSeconds: 1)
            .SendAsync("sys", "user", 100, AiResponseFormat.Text);

        Assert.False(result.Succeeded);
        Assert.Contains("timed out", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_rather_than_being_swallowed()
    {
        using var cts = new CancellationTokenSource();
        var handler = new Recorder(async (_, ct) =>
        {
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return GeminiEnvelope("unreachable");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BuildClient(handler).SendAsync("sys", "user", 100, AiResponseFormat.Text, cts.Token));
    }

    // ---------------------------------------------------------------- end to end

    [Fact]
    public async Task AiExtractor_maps_a_gemini_reply_the_same_way_it_maps_any_other()
    {
        const string modelJson = """
            {
              "jobTitle": "Senior .NET Developer",
              "companyName": "Contoso",
              "remotePolicy": "hybrid",
              "experienceMinYears": 3,
              "skills": [ { "name": "C#", "type": "required", "confidence": "high" } ]
            }
            """;
        var handler = new Recorder((_, _) => Task.FromResult(GeminiEnvelope(modelJson)));
        var extractor = new AiExtractor(BuildClient(handler), TestOptions(), NullLogger<AiExtractor>.Instance);

        var outcome = await extractor.ExtractAsync("Senior .NET Developer\nWe are hiring.");

        Assert.True(outcome.Succeeded);
        Assert.Equal("Senior .NET Developer", outcome.Posting!.JobTitle);
        Assert.Equal(3m, outcome.Posting.ExperienceMinYears);
        Assert.Equal("C#", Assert.Single(outcome.Posting.Skills).RawText);
    }

    [Fact]
    public async Task AiExtractor_reports_a_provider_failure_instead_of_throwing()
    {
        var handler = new Recorder((_, _) =>
            Task.FromResult(Json(HttpStatusCode.ServiceUnavailable, "{}")));
        var extractor = new AiExtractor(BuildClient(handler), TestOptions(), NullLogger<AiExtractor>.Instance);

        var outcome = await extractor.ExtractAsync("Anything at all.");

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.FailureReason);
    }

    private sealed class Recorder(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
