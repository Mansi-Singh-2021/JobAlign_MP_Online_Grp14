using System.Net;
using System.Text;
using JobAlign.Core.Abstractions;
using JobAlign.Core.Enums;
using JobAlign.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobAlign.Tests;

/// <summary>
/// AiExtractor and AiFeedbackGenerator against a fake HttpMessageHandler — never the real
/// API (role-f handout, "Tests to write"). Each test builds its own AnthropicClient so the
/// canned response and timeout can vary per test.
/// </summary>
public class AiExtractorTests
{
    private static AnthropicClient BuildClient(
        HttpMessageHandler handler,
        int timeoutSeconds = 30)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = null };
        var options = Options.Create(new AiClientOptions
        {
            ApiKey = "test-key",
            Model = "claude-sonnet-5",
            BaseUrl = "https://api.anthropic.com/v1/messages",
            TimeoutSeconds = timeoutSeconds
        });

        return new AnthropicClient(httpClient, options, NullLogger<AnthropicClient>.Instance);
    }

    private static AiExtractor BuildExtractor(HttpMessageHandler handler, int timeoutSeconds = 30) =>
        new(BuildClient(handler, timeoutSeconds), TestOptions(), NullLogger<AiExtractor>.Instance);

    private static IOptions<AiClientOptions> TestOptions() =>
        Options.Create(new AiClientOptions { MaxOutputTokens = 2048 });

    /// <summary>Wraps model output text the way the real Anthropic Messages API does.</summary>
    private static HttpResponseMessage AnthropicEnvelope(string modelText)
    {
        var envelope = new { content = new[] { new { type = "text", text = modelText } } };
        var json = System.Text.Json.JsonSerializer.Serialize(envelope);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private const string WellFormedJson = """
        {
          "jobTitle": "Senior .NET Developer",
          "companyName": "Contoso",
          "location": "Pune, India",
          "remotePolicy": "hybrid",
          "experienceMinYears": 3,
          "experienceMaxYears": 6,
          "salary": { "minRaw": 1200000, "maxRaw": 1800000, "currencyRaw": "INR", "periodRaw": "year" },
          "responsibilities": "Design, build and maintain backend services.",
          "summary": "Backend-focused .NET role in Pune.",
          "skills": [
            { "name": "C#", "type": "required", "confidence": "high" },
            { "name": "Docker", "type": "preferred", "confidence": "medium" }
          ],
          "fieldConfidences": [
            { "field": "jobTitle", "confidence": "high" }
          ]
        }
        """;

    [Fact]
    public async Task AiExtractor_maps_a_well_formed_response_to_ExtractedPosting()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(AnthropicEnvelope(WellFormedJson)));
        var extractor = BuildExtractor(handler);

        var outcome = await extractor.ExtractAsync("Senior .NET Developer\nWe are hiring.");

        Assert.True(outcome.Succeeded);
        Assert.Equal("Senior .NET Developer", outcome.Posting!.JobTitle);
        Assert.Equal("Contoso", outcome.Posting.CompanyName);
        Assert.Equal("Pune, India", outcome.Posting.RawLocationText);
        Assert.Equal(RemotePolicy.Hybrid, outcome.Posting.RemotePolicy);
        Assert.Equal(3m, outcome.Posting.ExperienceMinYears);
        Assert.Equal(2, outcome.Posting.Skills.Count);
    }

    [Fact]
    public async Task AiExtractor_converts_the_literal_string_Not_specified_to_null()
    {
        const string json = """{ "jobTitle": "Not specified", "companyName": "N/A", "skills": [], "fieldConfidences": [] }""";
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(AnthropicEnvelope(json)));
        var extractor = BuildExtractor(handler);

        var outcome = await extractor.ExtractAsync("some posting text");

        Assert.True(outcome.Succeeded);
        Assert.Null(outcome.Posting!.JobTitle);
        Assert.Null(outcome.Posting.CompanyName);
    }

    [Fact]
    public async Task AiExtractor_maps_unclear_remote_policy_to_Unclear_not_null()
    {
        const string json = """{ "remotePolicy": "unclear", "skills": [], "fieldConfidences": [] }""";
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(AnthropicEnvelope(json)));
        var extractor = BuildExtractor(handler);

        var outcome = await extractor.ExtractAsync("some posting text");

        Assert.True(outcome.Succeeded);
        Assert.Equal(RemotePolicy.Unclear, outcome.Posting!.RemotePolicy);
    }

    [Fact]
    public async Task AiExtractor_rejects_an_unknown_enum_value_without_throwing()
    {
        const string json = """{ "remotePolicy": "banana", "skills": [], "fieldConfidences": [] }""";
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(AnthropicEnvelope(json)));
        var extractor = BuildExtractor(handler);

        var outcome = await extractor.ExtractAsync("some posting text");

        Assert.True(outcome.Succeeded);
        Assert.Null(outcome.Posting!.RemotePolicy);
    }

    [Fact]
    public async Task AiExtractor_returns_Failure_on_a_timeout()
    {
        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            return AnthropicEnvelope(WellFormedJson);
        });
        var extractor = BuildExtractor(handler, timeoutSeconds: 1);

        var outcome = await extractor.ExtractAsync("some posting text");

        Assert.False(outcome.Succeeded);
        Assert.Contains("timed out", outcome.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AiExtractor_returns_Failure_on_a_non_success_status()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{ "error": "bad request" }""", Encoding.UTF8, "application/json")
            }));
        var extractor = BuildExtractor(handler);

        var outcome = await extractor.ExtractAsync("some posting text");

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.FailureReason);
    }

    [Fact]
    public async Task AiExtractor_returns_Failure_on_unparseable_json()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(AnthropicEnvelope("this is not json at all")));
        var extractor = BuildExtractor(handler);

        var outcome = await extractor.ExtractAsync("some posting text");

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.FailureReason);
    }

    [Fact]
    public async Task AiExtractor_strips_markdown_fences_before_parsing()
    {
        var fenced = $"```json\n{WellFormedJson}\n```";
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(AnthropicEnvelope(fenced)));
        var extractor = BuildExtractor(handler);

        var outcome = await extractor.ExtractAsync("some posting text");

        Assert.True(outcome.Succeeded);
        Assert.Equal("Senior .NET Developer", outcome.Posting!.JobTitle);
    }

    [Fact]
    public async Task AiExtractor_drops_a_skill_with_an_empty_name()
    {
        const string json = """
            {
              "skills": [
                { "name": "", "type": "required", "confidence": "high" },
                { "name": "SQL Server", "type": "required", "confidence": "high" }
              ],
              "fieldConfidences": []
            }
            """;
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(AnthropicEnvelope(json)));
        var extractor = BuildExtractor(handler);

        var outcome = await extractor.ExtractAsync("some posting text");

        Assert.True(outcome.Succeeded);
        var skill = Assert.Single(outcome.Posting!.Skills);
        Assert.Equal("SQL Server", skill.RawText);
    }

    [Fact]
    public async Task AiFeedbackGenerator_returns_null_when_the_provider_is_unavailable()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var client = BuildClient(handler, timeoutSeconds: 5);
        var generator = new AiFeedbackGenerator(client, NullLogger<AiFeedbackGenerator>.Instance);

        var request = new FeedbackRequest(
            JobTitle: "Backend Developer",
            OverallScore: 72m,
            MatchedSkills: ["C#", "SQL Server"],
            MissingRequiredSkills: ["Docker"],
            MissingPreferredSkills: ["Azure"]);

        var feedback = await generator.GenerateAsync(request);

        Assert.Null(feedback);
    }

    /// <summary>Minimal fake handler — a delegate stands in for a real HTTP transport.</summary>
    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
