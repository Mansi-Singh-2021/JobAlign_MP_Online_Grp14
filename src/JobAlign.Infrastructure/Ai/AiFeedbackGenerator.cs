using System.Text;
using JobAlign.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace JobAlign.Infrastructure.Ai;

/// <summary>
/// Real implementation of <see cref="IFeedbackGenerator"/> (Member F, FR-44).
/// Called once per score by the matching slice and stored on
/// <c>MatchResult.FeedbackText</c> — never called just because a posting was viewed
/// (NFR-13). Sends only skill names and a score, matching <see cref="FeedbackRequest"/>
/// exactly — no posting text, no profile, no candidate identity (NFR-09).
/// </summary>
public sealed class AiFeedbackGenerator : IFeedbackGenerator
{
    private const string SystemPrompt = """
        You write short match feedback for a candidate looking at one job posting.

        Write two to three sentences, in a plain, encouraging, factual tone. Name specific
        skills from what you are given — do not write generically. Do not invent anything
        about the candidate you were not told (no assumptions about their experience,
        background, or motivation beyond the skills and score provided).

        Structure: acknowledge what they already have going for them (matched / required
        skills held), then name the one or two most important gaps (missing required skills
        first, then preferred if there is room). If there are no missing required skills, say
        so plainly rather than inventing a gap.

        Respond with ONLY the feedback paragraph itself — no heading, no markdown, no preamble.
        """;

    private readonly AnthropicClient _client;
    private readonly ILogger<AiFeedbackGenerator> _logger;

    public AiFeedbackGenerator(AnthropicClient client, ILogger<AiFeedbackGenerator> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<string?> GenerateAsync(FeedbackRequest request, CancellationToken cancellationToken = default)
    {
        var userMessage = BuildUserMessage(request);

        var result = await _client.SendAsync(SystemPrompt, userMessage, maxTokens: 300, cancellationToken);

        if (!result.Succeeded)
        {
            _logger.LogWarning("Feedback generation unavailable: {Reason}", result.FailureReason);
            return null;
        }

        var text = result.Text?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>Only the fields NFR-09 permits — nothing about the candidate beyond skill names.</summary>
    private static string BuildUserMessage(FeedbackRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Job title: {request.JobTitle ?? "Not specified"}");
        sb.AppendLine($"Overall match score: {(request.OverallScore is { } s ? $"{s:0}/100" : "not calculated")}");
        sb.AppendLine($"Matched skills: {Join(request.MatchedSkills)}");
        sb.AppendLine($"Missing required skills: {Join(request.MissingRequiredSkills)}");
        sb.AppendLine($"Missing preferred skills: {Join(request.MissingPreferredSkills)}");
        return sb.ToString();
    }

    private static string Join(IReadOnlyList<string> values) =>
        values.Count > 0 ? string.Join(", ", values) : "none";
}
