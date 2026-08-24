namespace JobAlign.Core.Matching;

/// <summary>Pure scoring arithmetic for FR-36 to FR-39.</summary>
public static class ScoreCalculator
{
    public static decimal? RequiredSkillScore(int requiredCount, int heldCount) =>
        SkillScore(requiredCount, heldCount, nameof(requiredCount));

    public static decimal? PreferredSkillScore(int preferredCount, int heldCount) =>
        SkillScore(preferredCount, heldCount, nameof(preferredCount));

    public static decimal? ExperienceScore(decimal? candidateYears, decimal? postingMinYears)
    {
        if (candidateYears is null || postingMinYears is null)
            return null;

        if (candidateYears < 0)
            throw new ArgumentOutOfRangeException(nameof(candidateYears));
        if (postingMinYears < 0)
            throw new ArgumentOutOfRangeException(nameof(postingMinYears));

        if (candidateYears >= postingMinYears)
            return 100m;

        return Round(candidateYears.Value / postingMinYears.Value * 100m);
    }

    public static decimal? OverallScore(
        decimal? required,
        decimal? preferred,
        decimal? experience)
    {
        decimal weightedScore = 0m;
        decimal presentWeight = 0m;

        AddComponent(required, ScoringWeights.Required, ref weightedScore, ref presentWeight);
        AddComponent(preferred, ScoringWeights.Preferred, ref weightedScore, ref presentWeight);
        AddComponent(experience, ScoringWeights.Experience, ref weightedScore, ref presentWeight);

        return presentWeight == 0m ? null : Round(weightedScore / presentWeight);
    }

    private static decimal? SkillScore(int totalCount, int heldCount, string totalParameterName)
    {
        if (totalCount < 0)
            throw new ArgumentOutOfRangeException(totalParameterName);
        if (heldCount < 0 || heldCount > totalCount)
            throw new ArgumentOutOfRangeException(nameof(heldCount));

        return totalCount == 0
            ? null
            : Round((decimal)heldCount / totalCount * 100m);
    }

    private static void AddComponent(
        decimal? score,
        decimal weight,
        ref decimal weightedScore,
        ref decimal presentWeight)
    {
        if (score is null)
            return;

        if (score < 0m || score > 100m)
            throw new ArgumentOutOfRangeException(nameof(score));

        weightedScore += score.Value * weight;
        presentWeight += weight;
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
