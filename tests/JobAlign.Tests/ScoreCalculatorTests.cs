using JobAlign.Core.Matching;

namespace JobAlign.Tests;

public class ScoreCalculatorTests
{
    [Fact]
    public void RequiredScore_is_null_when_the_posting_lists_no_required_skills() =>
        Assert.Null(ScoreCalculator.RequiredSkillScore(0, 0));

    [Fact]
    public void RequiredScore_is_zero_when_the_candidate_holds_none_of_them() =>
        Assert.Equal(0m, ScoreCalculator.RequiredSkillScore(4, 0));

    [Fact]
    public void RequiredScore_is_the_proportion_held() =>
        Assert.Equal(66.67m, ScoreCalculator.RequiredSkillScore(6, 4));

    [Fact]
    public void PreferredScore_is_null_when_the_posting_lists_none() =>
        Assert.Null(ScoreCalculator.PreferredSkillScore(0, 0));

    [Fact]
    public void PreferredScore_is_zero_when_none_of_the_listed_skills_are_held() =>
        Assert.Equal(0m, ScoreCalculator.PreferredSkillScore(2, 0));

    [Fact]
    public void ExperienceScore_is_null_when_the_candidate_total_is_null() =>
        Assert.Null(ScoreCalculator.ExperienceScore(null, 5m));

    [Fact]
    public void ExperienceScore_is_null_when_the_posting_states_no_requirement() =>
        Assert.Null(ScoreCalculator.ExperienceScore(3m, null));

    [Theory]
    [InlineData(5, 5)]
    [InlineData(8, 5)]
    public void ExperienceScore_is_100_when_the_candidate_meets_or_exceeds(
        decimal candidateYears,
        decimal requiredYears) =>
        Assert.Equal(100m, ScoreCalculator.ExperienceScore(candidateYears, requiredYears));

    [Fact]
    public void ExperienceScore_is_proportional_when_short() =>
        Assert.Equal(60m, ScoreCalculator.ExperienceScore(3m, 5m));

    [Fact]
    public void OverallScore_renormalizes_over_present_components() =>
        Assert.Equal(85.88m, ScoreCalculator.OverallScore(80m, null, 100m));

    [Fact]
    public void OverallScore_weights_required_above_preferred()
    {
        var requiredMatch = ScoreCalculator.OverallScore(100m, 0m, null);
        var preferredMatch = ScoreCalculator.OverallScore(0m, 100m, null);

        Assert.True(requiredMatch > preferredMatch);
        Assert.Equal(80m, requiredMatch);
        Assert.Equal(20m, preferredMatch);
    }

    [Fact]
    public void OverallScore_is_null_only_when_every_component_is_null() 
    {
        Assert.Null(ScoreCalculator.OverallScore(null, null, null));
        Assert.Equal(0m, ScoreCalculator.OverallScore(0m, null, null));
    }

    [Fact]
    public void OverallScore_never_treats_a_null_component_as_zero() =>
        Assert.Equal(100m, ScoreCalculator.OverallScore(100m, null, null));

    [Fact]
    public void Worked_example_produces_the_expected_four_scores()
    {
        var required = ScoreCalculator.RequiredSkillScore(6, 4);
        var preferred = ScoreCalculator.PreferredSkillScore(2, 0);
        var experience = ScoreCalculator.ExperienceScore(3m, 5m);
        var overall = ScoreCalculator.OverallScore(required, preferred, experience);

        Assert.Equal(66.67m, required);
        Assert.Equal(0m, preferred);
        Assert.Equal(60m, experience);
        Assert.Equal(55m, overall);
    }
}
