using JobAlign.Core.Entities.Matching;

namespace JobAlign.Core.Abstractions;

/// <summary>
/// Scores a candidate profile against postings (FR-35 to FR-41).
///
/// Only Confirmed postings are scored. Pending postings are excluded from scoring,
/// comparison and dashboard figures (BR-08, FR-54).
/// </summary>
public interface IMatchScoringService
{
    /// <summary>
    /// Scores one posting and stores the MatchResult, replacing any previous one.
    /// Also writes the SkillGap rows for that result (FR-42, FR-43).
    /// Returns null when the posting is not this owner's, or is not Confirmed.
    /// </summary>
    Task<MatchResult?> ScoreAsync(
        int postingId,
        int ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rescores every Confirmed posting for this candidate (FR-41). Called whenever the
    /// profile changes. Returns how many postings were rescored. Must complete within
    /// 30 seconds for a realistic library (NFR-03) — one pass over the data, not N+1.
    /// </summary>
    Task<int> RecalculateAllAsync(
        int ownerUserId,
        CancellationToken cancellationToken = default);
}
