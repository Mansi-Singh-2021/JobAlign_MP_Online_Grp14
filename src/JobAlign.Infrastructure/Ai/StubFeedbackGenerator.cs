using JobAlign.Core.Abstractions;

namespace JobAlign.Infrastructure.Ai;

/// <summary>
/// Feedback without an AI service — the same role <see cref="JobAlign.Infrastructure.Extraction.StubExtractor"/>
/// plays for extraction. <see cref="IFeedbackGenerator"/> did not exist when Wave 0 landed
/// (match scoring wasn't built yet), so this ships alongside the real
/// <see cref="AiFeedbackGenerator"/> instead — same effect: nobody is blocked on an API key,
/// and the demo survives if the AI service is unavailable (NFR-06).
/// </summary>
public sealed class StubFeedbackGenerator : IFeedbackGenerator
{
    public Task<string?> GenerateAsync(FeedbackRequest request, CancellationToken cancellationToken = default)
    {
        var title = string.IsNullOrWhiteSpace(request.JobTitle) ? "this role" : request.JobTitle;

        var strengths = request.MatchedSkills.Count > 0
            ? $"you already have a solid foundation, with matching skills in {string.Join(", ", request.MatchedSkills.Take(3))}"
            : "your profile has some overlap with what this posting is looking for";

        var gaps = request.MissingRequiredSkills.Count > 0
            ? $"The main gaps are {string.Join(" and ", request.MissingRequiredSkills.Take(2))}, which are listed as required."
            : "There are no missing required skills, which is a strong sign for this one.";

        return Task.FromResult<string?>($"For {title}, {strengths}. {gaps}");
    }
}
