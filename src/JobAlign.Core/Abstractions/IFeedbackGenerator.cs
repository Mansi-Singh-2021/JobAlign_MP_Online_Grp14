namespace JobAlign.Core.Abstractions;

/// <summary>
/// Written strengths-and-gaps feedback for one scored posting (FR-44).
/// Generated once and stored on MatchResult.FeedbackText — viewing a posting must
/// never trigger a fresh AI call (NFR-13).
/// </summary>
public interface IFeedbackGenerator
{
    /// <summary>Returns null when the provider is unavailable. Never throws for a provider failure.</summary>
    Task<string?> GenerateAsync(FeedbackRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Only what the model needs to write the feedback. Deliberately not the whole posting
/// or profile — NFR-09 limits what is sent to the AI service to the content required.
/// </summary>
public sealed record FeedbackRequest(
    string? JobTitle,
    decimal? OverallScore,
    IReadOnlyList<string> MatchedSkills,
    IReadOnlyList<string> MissingRequiredSkills,
    IReadOnlyList<string> MissingPreferredSkills);
