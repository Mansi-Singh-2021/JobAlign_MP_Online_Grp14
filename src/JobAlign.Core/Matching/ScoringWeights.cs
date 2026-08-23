namespace JobAlign.Core.Matching;

/// <summary>
/// The weightings behind the overall score (FR-39, BR-07). Held in one place and
/// versioned, so a stored score can be explained against the rules that produced it
/// (NFR-08) — MatchResult.ScoringConfigVersion records which version was used.
/// </summary>
public static class ScoringWeights
{
    public const string Version = "weights-v1";

    /// <summary>Required skills weigh more than preferred — this is BR-07, not a preference.</summary>
    public const decimal Required = 0.60m;
    public const decimal Preferred = 0.15m;
    public const decimal Experience = 0.25m;
}
