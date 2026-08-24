using JobAlign.Core.Entities.Matching;

namespace JobAlign.Core.Abstractions;

/// <summary>
/// Scores a candidate profile against postings (FR-35 to FR-41).
///
/// A posting is scored once extraction has produced something to score, whether or not
/// the candidate has confirmed it. Only postings whose extraction failed are excluded —
/// those are Pending, and they stay out of scoring, comparison and dashboard figures
/// (BR-08, FR-54).
/// </summary>
public interface IMatchScoringService
{
    /// <summary>
    /// Scores one posting and stores the MatchResult, replacing any previous one.
    /// Also writes the SkillGap rows for that result (FR-42, FR-43).
    /// Returns null when the posting is not this owner's, or its extraction failed.
    /// </summary>
    Task<MatchResult?> ScoreAsync(
        int postingId,
        int ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rescores every scoreable posting for this candidate (FR-41). Called whenever the
    /// profile changes. Returns how many postings were rescored. Must complete within
    /// 30 seconds for a realistic library (NFR-03) — one pass over the data, not N+1.
    /// </summary>
    Task<int> RecalculateAllAsync(
        int ownerUserId,
        CancellationToken cancellationToken = default);
}
