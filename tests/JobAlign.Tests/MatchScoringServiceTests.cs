using JobAlign.Core.Entities.Matching;
using JobAlign.Core.Entities.Postings;
using JobAlign.Core.Entities.Profiles;
using JobAlign.Core.Enums;
using JobAlign.Core.Matching;
using JobAlign.Infrastructure.Data;
using JobAlign.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace JobAlign.Tests;

public class MatchScoringServiceTests
{
    [Fact]
    public async Task ScoreAsync_refuses_a_Pending_posting()
    {
        await using var db = CreateContext();
        await AddProfileAsync(db, 10, 3m);
        var posting = await AddPostingAsync(db, 10, PostingStatus.Pending);
        var service = new MatchScoringService(db);

        var result = await service.ScoreAsync(posting.Id, 10);

        Assert.Null(result);
        Assert.Empty(db.MatchResults);
    }

    [Fact]
    public async Task ScoreAsync_refuses_a_New_posting()
    {
        await using var db = CreateContext();
        await AddProfileAsync(db, 10, 3m);
        var posting = await AddPostingAsync(db, 10, PostingStatus.New);
        var service = new MatchScoringService(db);

        var result = await service.ScoreAsync(posting.Id, 10);

        Assert.Null(result);
        Assert.Empty(db.MatchResults);
    }

    [Fact]
    public async Task ScoreAsync_refuses_another_users_posting()
    {
        await using var db = CreateContext();
        await AddProfileAsync(db, 10, 3m);
        var posting = await AddPostingAsync(db, 20, PostingStatus.Confirmed);
        var service = new MatchScoringService(db);

        var result = await service.ScoreAsync(posting.Id, 10);

        Assert.Null(result);
        Assert.Empty(db.MatchResults);
    }

    [Fact]
    public async Task ScoreAsync_writes_gaps_with_the_correct_skill_type()
    {
        await using var db = CreateContext();
        var profile = await AddProfileAsync(db, 10, 3m);
        profile.Skills.Add(ProfileSkill(1));

        var posting = await AddPostingAsync(db, 10, PostingStatus.Confirmed, 5m);
        posting.Skills.Add(PostingSkill(1, SkillType.Required, "C#"));
        posting.Skills.Add(PostingSkill(2, SkillType.Required, "Docker"));
        posting.Skills.Add(PostingSkill(3, SkillType.Preferred, "Terraform"));
        await db.SaveChangesAsync();
        var service = new MatchScoringService(db);

        var result = await service.ScoreAsync(posting.Id, 10);

        Assert.NotNull(result);
        Assert.Equal(50m, result.RequiredSkillScore);
        Assert.Equal(0m, result.PreferredSkillScore);
        Assert.Collection(
            result.SkillGaps.OrderBy(g => g.MasterSkillId),
            gap =>
            {
                Assert.Equal(2, gap.MasterSkillId);
                Assert.Equal(SkillType.Required, gap.SkillType);
            },
            gap =>
            {
                Assert.Equal(3, gap.MasterSkillId);
                Assert.Equal(SkillType.Preferred, gap.SkillType);
            });
    }

    [Fact]
    public async Task ScoreAsync_matches_skills_by_MasterSkillId_not_raw_name()
    {
        await using var db = CreateContext();
        var profile = await AddProfileAsync(db, 10, null);
        profile.Skills.Add(ProfileSkill(42));
        var posting = await AddPostingAsync(db, 10, PostingStatus.Confirmed);
        posting.Skills.Add(PostingSkill(42, SkillType.Required, "C#"));
        await db.SaveChangesAsync();
        var service = new MatchScoringService(db);

        var result = await service.ScoreAsync(posting.Id, 10);

        Assert.NotNull(result);
        Assert.Equal(100m, result.RequiredSkillScore);
        Assert.Empty(result.SkillGaps);
    }

    [Fact]
    public async Task ScoreAsync_preserves_existing_FeedbackText()
    {
        await using var db = CreateContext();
        var profile = await AddProfileAsync(db, 10, 3m);
        var posting = await AddPostingAsync(db, 10, PostingStatus.Confirmed, 5m);
        var feedbackAt = DateTimeOffset.UtcNow.AddHours(-1);
        db.MatchResults.Add(new MatchResult
        {
            JobPostingId = posting.Id,
            CandidateProfileId = profile.Id,
            ScoringConfigVersion = "old",
            FeedbackText = "Keep this feedback.",
            FeedbackGeneratedAt = feedbackAt
        });
        await db.SaveChangesAsync();
        var service = new MatchScoringService(db);

        var result = await service.ScoreAsync(posting.Id, 10);

        Assert.NotNull(result);
        Assert.Equal("Keep this feedback.", result.FeedbackText);
        Assert.Equal(feedbackAt, result.FeedbackGeneratedAt);
        Assert.Equal(ScoringWeights.Version, result.ScoringConfigVersion);
    }

    [Fact]
    public async Task ScoreAsync_uses_the_candidates_experience_correction()
    {
        await using var db = CreateContext();
        await AddProfileAsync(db, 10, 3m);
        var posting = await AddPostingAsync(db, 10, PostingStatus.Confirmed, 5m);
        posting.Corrections.Add(new PostingFieldCorrection
        {
            FieldName = CorrectableFields.ExperienceMinYears,
            CorrectedValue = "3",
            CorrectedAt = DateTimeOffset.UtcNow,
            CorrectedByUserId = 10
        });
        await db.SaveChangesAsync();
        var service = new MatchScoringService(db);

        var result = await service.ScoreAsync(posting.Id, 10);

        Assert.NotNull(result);
        Assert.Equal(100m, result.ExperienceScore);
    }

    [Fact]
    public async Task RecalculateAll_rescores_every_confirmed_posting()
    {
        await using var db = CreateContext();
        var profile = await AddProfileAsync(db, 10, 2m);
        profile.Skills.Add(ProfileSkill(1));

        var first = await AddPostingAsync(db, 10, PostingStatus.Confirmed, 4m);
        first.Skills.Add(PostingSkill(1, SkillType.Required, "c sharp"));
        var second = await AddPostingAsync(db, 10, PostingStatus.Confirmed);
        second.Skills.Add(PostingSkill(2, SkillType.Preferred, "Docker"));
        await AddPostingAsync(db, 10, PostingStatus.Pending);
        await AddPostingAsync(db, 10, PostingStatus.New);
        await AddPostingAsync(db, 99, PostingStatus.Confirmed);
        await db.SaveChangesAsync();
        var service = new MatchScoringService(db);

        var count = await service.RecalculateAllAsync(10);

        Assert.Equal(2, count);
        var results = await db.MatchResults.OrderBy(r => r.JobPostingId).ToListAsync();
        Assert.Equal(2, results.Count);
        Assert.Equal(100m, results.Single(r => r.JobPostingId == first.Id).RequiredSkillScore);
        Assert.Equal(0m, results.Single(r => r.JobPostingId == second.Id).PreferredSkillScore);
        Assert.All(results, result => Assert.Equal(ScoringWeights.Version, result.ScoringConfigVersion));
    }

    private static JobAlignDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<JobAlignDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new JobAlignDbContext(options);
    }

    private static async Task<CandidateProfile> AddProfileAsync(
        JobAlignDbContext db,
        int userId,
        decimal? experienceYears)
    {
        var profile = new CandidateProfile
        {
            UserId = userId,
            TotalExperienceYears = experienceYears,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.CandidateProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    private static async Task<JobPosting> AddPostingAsync(
        JobAlignDbContext db,
        int ownerUserId,
        PostingStatus status,
        decimal? minimumExperience = null)
    {
        var posting = new JobPosting(
            ownerUserId,
            $"REF-{Guid.NewGuid():N}",
            "Example posting text",
            PostingCaptureMethod.PastedText)
        {
            Status = status
        };
        db.JobPostings.Add(posting);
        await db.SaveChangesAsync();

        if (minimumExperience is not null)
        {
            posting.Extractions.Add(new PostingExtraction
            {
                IsCurrent = true,
                RunStatus = ExtractionRunStatus.Succeeded,
                ExtractedAt = DateTimeOffset.UtcNow,
                ExtractionConfigVersion = "test-v1",
                ExperienceMinYears = minimumExperience
            });
            await db.SaveChangesAsync();
        }

        return posting;
    }

    private static ProfileSkill ProfileSkill(int masterSkillId) => new()
    {
        MasterSkillId = masterSkillId,
        ProficiencyLevel = ProficiencyLevel.Intermediate,
        Source = ProfileSkillSource.Manual,
        ConfirmedAt = DateTimeOffset.UtcNow
    };

    private static PostingSkill PostingSkill(
        int masterSkillId,
        SkillType skillType,
        string rawText) => new()
    {
        MasterSkillId = masterSkillId,
        SkillType = skillType,
        RawText = rawText,
        Source = PostingSkillSource.Extracted
    };
}
