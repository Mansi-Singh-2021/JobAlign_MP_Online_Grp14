using JobAlign.Core.Entities.Postings;
using JobAlign.Core.Entities.Profiles;
using JobAlign.Core.Enums;

namespace JobAlign.Core.Entities.Matching;

/// <summary>
/// How well the candidate fits one confirmed posting (FR-36 to FR-40).
/// Recalculated for every confirmed posting whenever the profile changes (FR-41).
/// Pending postings are never scored (BR-08, FR-54).
///
/// All four scores are stored, not just the overall one, so the result can be
/// explained rather than asserted (FR-40).
///
/// Scores are 0–100. The three component scores are nullable on purpose: a
/// proportion is undefined when the posting states nothing to measure against
/// — a posting listing no preferred skills has no preferred-skill score, and
/// recording that as 0 would be inventing a fact (BR-02, NFR-07). Callers must
/// treat null as "not measurable", never as zero.
/// </summary>
public class MatchResult
{
    public int Id { get; set; }

    /// <summary>One current result per posting.</summary>
    public int JobPostingId { get; set; }
    public JobPosting JobPosting { get; set; } = null!;

    public int CandidateProfileId { get; set; }
    public CandidateProfile CandidateProfile { get; set; } = null!;

    /// <summary>Proportion of the posting's required skills the candidate holds (FR-36).
    /// Null where the posting states no required skills.</summary>
    public decimal? RequiredSkillScore { get; set; }

    /// <summary>Proportion of the posting's preferred skills the candidate holds (FR-37).
    /// Null where the posting states none.</summary>
    public decimal? PreferredSkillScore { get; set; }

    /// <summary>Candidate's total experience against the posting's requirement (FR-38).
    /// Null where the posting does not state a requirement, or the profile has no total.</summary>
    public decimal? ExperienceScore { get; set; }

    /// <summary>
    /// Weighted combination of the components, required skills weighing more
    /// than preferred (FR-39, BR-07). Null where the posting stated too little
    /// to measure anything; such postings are excluded from match-score sorting,
    /// exactly as a posting with no salary is excluded from salary sorting (BR-10).
    /// </summary>
    public decimal? OverallScore { get; set; }

    /// <summary>
    /// Version of the weightings used, so a historical score can be explained
    /// against the rules that produced it (NFR-08).
    /// </summary>
    public required string ScoringConfigVersion { get; set; }

    public DateTimeOffset CalculatedAt { get; set; }

    /// <summary>Written strengths-and-weaknesses feedback for this role (FR-44). Generated, regenerable.</summary>
    public string? FeedbackText { get; set; }

    public DateTimeOffset? FeedbackGeneratedAt { get; set; }

    /// <summary>Skills this posting wants that the candidate does not hold (FR-42, FR-43).</summary>
    public ICollection<SkillGap> SkillGaps { get; set; } = new List<SkillGap>();
}
