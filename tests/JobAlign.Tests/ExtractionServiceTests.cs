using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Postings;
using JobAlign.Core.Entities.Skills;
using JobAlign.Core.Enums;
using JobAlign.Core.Extraction;
using JobAlign.Infrastructure.Data;
using JobAlign.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobAlign.Tests;

/// <summary>
/// Behaviour of the extraction orchestrator (FR-12, FR-18, FR-19, FR-21, BR-01 to BR-04).
/// </summary>
/// <remarks>
/// Uses the in-memory provider, so database-level constraints are not exercised here — the
/// filtered unique index and the foreign keys are proven by running against SQL Server.
/// What these tests pin down is the logic that decides what gets written.
/// </remarks>
public class ExtractionServiceTests : IDisposable
{
    private const int OwnerId = 1;
    private const int OtherUserId = 99;

    private readonly JobAlignDbContext _db;

    public ExtractionServiceTests()
    {
        var options = new DbContextOptionsBuilder<JobAlignDbContext>()
            .UseInMemoryDatabase($"extraction-{Guid.NewGuid()}")
            // The service wraps its writes in a transaction; the in-memory provider has no
            // transactions and warns rather than silently ignoring. Acknowledge it.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new JobAlignDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    // ---------------------------------------------------------------- happy path

    [Fact]
    public async Task Stores_a_successful_run_as_the_current_one()
    {
        var posting = GivenPosting();
        var service = ServiceWith(Succeeds(new ExtractedPosting { JobTitle = "Backend Engineer" }));

        var extraction = await service.RunAsync(posting.Id, OwnerId);

        Assert.NotNull(extraction);
        Assert.Equal(ExtractionRunStatus.Succeeded, extraction!.RunStatus);
        Assert.True(extraction.IsCurrent);
        Assert.Equal("Backend Engineer", extraction.JobTitle);
    }

    [Fact]
    public async Task Marks_only_the_newest_run_as_current()
    {
        var posting = GivenPosting();
        var service = ServiceWith(Succeeds(new ExtractedPosting { JobTitle = "First" }));

        await service.RunAsync(posting.Id, OwnerId);
        await ServiceWith(Succeeds(new ExtractedPosting { JobTitle = "Second" })).RunAsync(posting.Id, OwnerId);

        var runs = await _db.PostingExtractions.Where(e => e.JobPostingId == posting.Id).ToListAsync();

        // Both runs are kept: history is what makes a stored result explainable (NFR-08).
        Assert.Equal(2, runs.Count);
        Assert.Single(runs, r => r.IsCurrent);
        Assert.Equal("Second", runs.Single(r => r.IsCurrent).JobTitle);
    }

    [Fact]
    public async Task Copies_unstated_fields_across_as_null_not_zero()
    {
        var posting = GivenPosting();
        var service = ServiceWith(Succeeds(new ExtractedPosting { JobTitle = "Only a title" }));

        var extraction = await service.RunAsync(posting.Id, OwnerId);

        // BR-02 / FR-17: a detail the posting never stated stays null all the way down.
        Assert.Null(extraction!.CompanyName);
        Assert.Null(extraction.SalaryMinRaw);
        Assert.Null(extraction.ExperienceMinYears);
        Assert.Null(extraction.RemotePolicy);
    }

    [Fact]
    public async Task Leaves_the_posting_awaiting_review_after_a_successful_run()
    {
        var posting = GivenPosting();

        await ServiceWith(Succeeds(new ExtractedPosting())).RunAsync(posting.Id, OwnerId);

        // Extraction alone does not confirm anything — the candidate does (FR-18, AC-10).
        Assert.Equal(PostingStatus.New, (await _db.JobPostings.FindAsync(posting.Id))!.Status);
    }

    // ---------------------------------------------------------------- failure

    [Fact]
    public async Task Stores_a_failed_run_and_sets_the_posting_to_Pending()
    {
        var posting = GivenPosting();
        var service = ServiceWith(Fails("The AI service did not respond."));

        var extraction = await service.RunAsync(posting.Id, OwnerId);

        // NFR-06 / FR-19: the posting survives, and the reason is recorded.
        Assert.Equal(ExtractionRunStatus.Failed, extraction!.RunStatus);
        Assert.Equal("The AI service did not respond.", extraction.FailureReason);
        Assert.Equal(PostingStatus.Pending, (await _db.JobPostings.FindAsync(posting.Id))!.Status);
        Assert.NotNull(await _db.JobPostings.FindAsync(posting.Id));
    }

    // ---------------------------------------------------------------- skills

    [Fact]
    public async Task Writes_resolved_skills_against_the_master_skill_foreign_key()
    {
        var posting = GivenPosting();
        var csharp = GivenMasterSkill(10, "C#");

        var service = ServiceWith(
            Succeeds(new ExtractedPosting
            {
                Skills = [new ExtractedSkill("c sharp", SkillType.Required, ConfidenceLevel.High)]
            }),
            resolver: new FakeResolver(("c sharp", csharp.Id, csharp.Name)));

        await service.RunAsync(posting.Id, OwnerId);

        var stored = await _db.PostingSkills.SingleAsync();

        Assert.Equal(csharp.Id, stored.MasterSkillId);        // BR-04: identity is the FK
        Assert.Equal("c sharp", stored.RawText);              // provenance only
        Assert.Equal(SkillType.Required, stored.SkillType);
        Assert.Equal(PostingSkillSource.Extracted, stored.Source);
    }

    [Fact]
    public async Task Skips_skills_that_do_not_resolve_rather_than_inventing_a_master_skill()
    {
        var posting = GivenPosting();

        var service = ServiceWith(
            Succeeds(new ExtractedPosting
            {
                Skills = [new ExtractedSkill("Underwater Basket Weaving", SkillType.Required, null)]
            }),
            resolver: new FakeResolver());                    // resolves nothing

        await service.RunAsync(posting.Id, OwnerId);

        // BR-04: the extractor must never be able to define the master skill list.
        Assert.Empty(await _db.PostingSkills.ToListAsync());
        Assert.Empty(await _db.MasterSkills.ToListAsync());
    }

    [Fact]
    public async Task Does_not_store_the_same_master_skill_twice()
    {
        var posting = GivenPosting();
        var csharp = GivenMasterSkill(10, "C#");

        var service = ServiceWith(
            Succeeds(new ExtractedPosting
            {
                Skills =
                [
                    new ExtractedSkill("C#", SkillType.Required, null),
                    new ExtractedSkill("c-sharp", SkillType.Preferred, null)
                ]
            }),
            resolver: new FakeResolver(("C#", csharp.Id, csharp.Name), ("c-sharp", csharp.Id, csharp.Name)));

        await service.RunAsync(posting.Id, OwnerId);

        // UX_PostingSkills_Posting_Skill is unique on (posting, master skill).
        Assert.Single(await _db.PostingSkills.ToListAsync());
    }

    [Fact]
    public async Task Preserves_user_added_skills_across_re_extraction()
    {
        var posting = GivenPosting();
        var csharp = GivenMasterSkill(10, "C#");
        var docker = GivenMasterSkill(11, "Docker");

        _db.PostingSkills.Add(new PostingSkill
        {
            JobPostingId = posting.Id,
            MasterSkillId = docker.Id,
            SkillType = SkillType.Preferred,
            Source = PostingSkillSource.UserAdded
        });
        await _db.SaveChangesAsync();

        var service = ServiceWith(
            Succeeds(new ExtractedPosting
            {
                Skills = [new ExtractedSkill("C#", SkillType.Required, null)]
            }),
            resolver: new FakeResolver(("C#", csharp.Id, csharp.Name)));

        await service.RunAsync(posting.Id, OwnerId);

        var stored = await _db.PostingSkills.ToListAsync();

        // BR-03: re-extraction replaces extracted rows and leaves the candidate's own alone.
        Assert.Contains(stored, s => s.MasterSkillId == docker.Id && s.Source == PostingSkillSource.UserAdded);
        Assert.Contains(stored, s => s.MasterSkillId == csharp.Id && s.Source == PostingSkillSource.Extracted);
    }

    // ---------------------------------------------------------------- ownership

    [Fact]
    public async Task Refuses_a_posting_belonging_to_someone_else()
    {
        var posting = GivenPosting();

        var result = await ServiceWith(Succeeds(new ExtractedPosting())).RunAsync(posting.Id, OtherUserId);

        Assert.Null(result);                                  // BR-09
        Assert.Empty(await _db.PostingExtractions.ToListAsync());
    }

    // ---------------------------------------------------------------- corrections

    [Fact]
    public async Task Correction_is_written_against_the_posting_not_the_extraction_run()
    {
        var posting = GivenPosting();
        var service = ServiceWith(Succeeds(new ExtractedPosting { JobTitle = "Wrong Title" }));
        await service.RunAsync(posting.Id, OwnerId);

        await service.ApplyCorrectionsAsync(
            posting.Id, OwnerId,
            new Dictionary<string, string?> { [CorrectableFields.JobTitle] = "Right Title" });

        var correction = await _db.PostingFieldCorrections.SingleAsync();

        // BR-03: the foreign key is to JobPostings, which is what makes it survive a re-run.
        Assert.Equal(posting.Id, correction.JobPostingId);
        Assert.Equal("Right Title", correction.CorrectedValue);
    }

    [Fact]
    public async Task Correction_survives_re_extraction()
    {
        var posting = GivenPosting();
        await ServiceWith(Succeeds(new ExtractedPosting { JobTitle = "Wrong" })).RunAsync(posting.Id, OwnerId);

        await ServiceWith(Succeeds(new ExtractedPosting())).ApplyCorrectionsAsync(
            posting.Id, OwnerId,
            new Dictionary<string, string?> { [CorrectableFields.JobTitle] = "Right Title" });

        await ServiceWith(Succeeds(new ExtractedPosting { JobTitle = "Wrong Again" })).RunAsync(posting.Id, OwnerId);

        var correction = await _db.PostingFieldCorrections.SingleAsync();
        Assert.Equal("Right Title", correction.CorrectedValue);
    }

    [Fact]
    public async Task Confirming_without_changing_anything_records_no_correction()
    {
        var posting = GivenPosting();
        var service = ServiceWith(Succeeds(new ExtractedPosting { JobTitle = "Backend Engineer" }));
        await service.RunAsync(posting.Id, OwnerId);

        await service.ApplyCorrectionsAsync(
            posting.Id, OwnerId,
            new Dictionary<string, string?> { [CorrectableFields.JobTitle] = "Backend Engineer" });

        // Otherwise BR-03 would start protecting a value the candidate never typed.
        Assert.Empty(await _db.PostingFieldCorrections.ToListAsync());
    }

    [Fact]
    public async Task Reverting_a_correction_to_the_extracted_value_removes_it()
    {
        var posting = GivenPosting();
        var service = ServiceWith(Succeeds(new ExtractedPosting { JobTitle = "Backend Engineer" }));
        await service.RunAsync(posting.Id, OwnerId);

        var field = new Dictionary<string, string?> { [CorrectableFields.JobTitle] = "Something Else" };
        await service.ApplyCorrectionsAsync(posting.Id, OwnerId, field);
        Assert.Single(await _db.PostingFieldCorrections.ToListAsync());

        field[CorrectableFields.JobTitle] = "Backend Engineer";
        await service.ApplyCorrectionsAsync(posting.Id, OwnerId, field);

        Assert.Empty(await _db.PostingFieldCorrections.ToListAsync());
    }

    [Fact]
    public async Task Confirming_sets_the_posting_to_Confirmed()
    {
        var posting = GivenPosting();
        var service = ServiceWith(Succeeds(new ExtractedPosting()));
        await service.RunAsync(posting.Id, OwnerId);

        await service.ApplyCorrectionsAsync(posting.Id, OwnerId, new Dictionary<string, string?>());

        var reloaded = await _db.JobPostings.FindAsync(posting.Id);
        Assert.Equal(PostingStatus.Confirmed, reloaded!.Status);   // AC-10
        Assert.NotNull(reloaded.ConfirmedAt);
    }

    [Fact]
    public async Task Ignores_a_correction_naming_a_field_that_cannot_be_corrected()
    {
        var posting = GivenPosting();
        var service = ServiceWith(Succeeds(new ExtractedPosting()));
        await service.RunAsync(posting.Id, OwnerId);

        await service.ApplyCorrectionsAsync(
            posting.Id, OwnerId,
            new Dictionary<string, string?> { ["RawText"] = "trying to rewrite the posting" });

        // BR-01: RawText is not correctable, and CorrectableFields is the whitelist.
        Assert.Empty(await _db.PostingFieldCorrections.ToListAsync());
    }

    [Fact]
    public async Task Refuses_to_confirm_someone_elses_posting()
    {
        var posting = GivenPosting();

        var confirmed = await ServiceWith(Succeeds(new ExtractedPosting()))
            .ApplyCorrectionsAsync(posting.Id, OtherUserId, new Dictionary<string, string?>());

        Assert.False(confirmed);                              // BR-09
    }

    // ---------------------------------------------------------------- helpers

    private ExtractionService ServiceWith(IJobExtractor extractor, ISkillResolver? resolver = null) =>
        new(_db, extractor, resolver ?? new FakeResolver(), NullLogger<ExtractionService>.Instance);

    private JobPosting GivenPosting()
    {
        var posting = new JobPosting(OwnerId, $"JA-TEST-{Guid.NewGuid():N}"[..16], "Raw posting text.",
            PostingCaptureMethod.PastedText);

        _db.JobPostings.Add(posting);
        _db.SaveChanges();
        return posting;
    }

    private MasterSkill GivenMasterSkill(int id, string name)
    {
        var skill = new MasterSkill
        {
            Id = id,
            Name = name,
            NormalizedName = name.ToLowerInvariant().Replace("#", "sharp").Replace(" ", ""),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.MasterSkills.Add(skill);
        _db.SaveChanges();
        return skill;
    }

    private static FakeExtractor Succeeds(ExtractedPosting posting) =>
        new(ExtractionOutcome.Success(posting));

    private static FakeExtractor Fails(string reason) =>
        new(ExtractionOutcome.Failure(reason));

    /// <summary>Returns whatever it was given. Implemented rather than mocked — the interface is two members.</summary>
    private sealed class FakeExtractor(ExtractionOutcome outcome) : IJobExtractor
    {
        public string ConfigVersion => "fake-v1";

        public Task<ExtractionOutcome> ExtractAsync(string rawText, CancellationToken cancellationToken = default) =>
            Task.FromResult(outcome);
    }

    /// <summary>Resolves only the names it was constructed with; everything else is unresolved.</summary>
    private sealed class FakeResolver(params (string Raw, int Id, string Name)[] known) : ISkillResolver
    {
        public Task<SkillResolution> ResolveAsync(string rawSkillText, CancellationToken cancellationToken = default) =>
            Task.FromResult(Resolve(rawSkillText));

        public Task<IReadOnlyList<SkillResolution>> ResolveManyAsync(
            IEnumerable<string> rawSkillTexts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SkillResolution>>(rawSkillTexts.Select(Resolve).ToList());

        public string Normalize(string rawSkillText) => rawSkillText.ToLowerInvariant();

        private SkillResolution Resolve(string raw)
        {
            var hit = known.FirstOrDefault(k => string.Equals(k.Raw, raw, StringComparison.OrdinalIgnoreCase));

            return hit.Name is null
                ? new SkillResolution(raw, null, null)
                : new SkillResolution(raw, hit.Id, hit.Name);
        }
    }
}
