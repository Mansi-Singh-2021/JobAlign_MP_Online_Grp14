using JobAlign.Core.Enums;
using JobAlign.Infrastructure.Extraction;

namespace JobAlign.Tests;

/// <summary>
/// The stub is what the whole extraction flow is built and demonstrated against until the
/// AI client lands, so its guarantees matter (build order step 3).
/// </summary>
public class StubExtractorTests
{
    private readonly StubExtractor _extractor = new();

    private const string SamplePosting =
        "Senior .NET Developer\nWe need someone strong in C# and ASP.NET Core, with SQL Server "
        + "experience. Docker is a plus.";

    [Fact]
    public async Task Returns_failure_for_empty_text()
    {
        var outcome = await _extractor.ExtractAsync("   ");

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.FailureReason);
    }

    [Fact]
    public async Task Leaves_company_name_null_so_the_not_specified_path_is_exercised()
    {
        var outcome = await _extractor.ExtractAsync(SamplePosting);

        // Deliberate: a field that is always unstated keeps the "Not specified" rendering
        // honest from day one (BR-02, FR-17).
        Assert.Null(outcome.Posting!.CompanyName);
    }

    [Fact]
    public async Task Takes_the_first_line_as_the_job_title()
    {
        var outcome = await _extractor.ExtractAsync(SamplePosting);

        Assert.Equal("Senior .NET Developer", outcome.Posting!.JobTitle);
    }

    [Fact]
    public async Task Does_not_guess_a_title_from_a_long_opening_line()
    {
        var prose = new string('x', 200) + "\nC#";

        var outcome = await _extractor.ExtractAsync(prose);

        // A long first line is prose, not a heading. Guessing would invent a detail (BR-02).
        Assert.Null(outcome.Posting!.JobTitle);
    }

    [Fact]
    public async Task Finds_skills_that_the_text_actually_mentions()
    {
        var outcome = await _extractor.ExtractAsync(SamplePosting);

        var names = outcome.Posting!.Skills.Select(s => s.RawText).ToList();

        Assert.Contains("C#", names);
        Assert.Contains("ASP.NET Core", names);
        Assert.Contains("SQL Server", names);
        Assert.Contains("Docker", names);
        Assert.DoesNotContain("Python", names);
    }

    [Fact]
    public async Task Classifies_skills_as_required_or_preferred()
    {
        var outcome = await _extractor.ExtractAsync(SamplePosting);

        Assert.Contains(outcome.Posting!.Skills, s => s.SkillType == SkillType.Required);
        Assert.Contains(outcome.Posting!.Skills, s => s.SkillType == SkillType.Preferred);
    }

    [Fact]
    public async Task Falls_back_to_a_skill_set_when_the_text_mentions_nothing_known()
    {
        var outcome = await _extractor.ExtractAsync("We are hiring a gardener for our office plants.");

        // A demo should never show an empty skill list just because the paste was unusual.
        Assert.NotEmpty(outcome.Posting!.Skills);
    }

    [Fact]
    public async Task Reports_at_least_one_low_confidence_field()
    {
        var outcome = await _extractor.ExtractAsync(SamplePosting);

        // The review screen flags low confidence (FR-20, NFR-06); it needs something to flag.
        Assert.Contains(outcome.Posting!.Confidences, c => c.Confidence == ConfidenceLevel.Low);
    }

    [Fact]
    public void Reports_a_config_version_so_a_run_can_be_explained()
    {
        // NFR-08: every stored run records the configuration that produced it.
        Assert.False(string.IsNullOrWhiteSpace(_extractor.ConfigVersion));
    }
}
