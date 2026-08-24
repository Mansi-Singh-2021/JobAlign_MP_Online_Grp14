using JobAlign.Core.Entities.Matching;
using JobAlign.Core.Entities.Postings;
using JobAlign.Core.Entities.Profiles;
using JobAlign.Core.Entities.Skills;
using JobAlign.Core.Enums;
using JobAlign.Infrastructure.Data;
using JobAlign.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace JobAlign.Tests;

public class SkillGapServiceTests
{
    [Fact]
    public async Task GetGapsForPosting_filters_by_owner()
    {
        await using var db = CreateContext();
        var user10Posting = await AddPostingAsync(db, 10, PostingStatus.Confirmed);
        var user20Posting = await AddPostingAsync(db, 20, PostingStatus.Confirmed);

        var skill1 = await AddMasterSkillAsync(db, 1, "C#");
        var skill2 = await AddMasterSkillAsync(db, 2, "Docker");

        await AddMatchResultWithGapsAsync(db, user10Posting, (skill1.Id, SkillType.Required));
        await AddMatchResultWithGapsAsync(db, user20Posting, (skill2.Id, SkillType.Required));

        var service = new SkillGapService(db);

        // User 10 asks for their own posting
        var gapsFor10 = await service.GetGapsForPostingAsync(user10Posting.Id, 10);
        Assert.Single(gapsFor10);
        Assert.Equal(skill1.Id, gapsFor10[0].MasterSkillId);
        Assert.Equal("C#", gapsFor10[0].MasterSkill.Name);

        // User 10 asks for User 20's posting -> returns empty list (BR-09)
        var gapsFor20From10 = await service.GetGapsForPostingAsync(user20Posting.Id, 10);
        Assert.Empty(gapsFor20From10);
    }

    [Fact]
    public async Task RebuildRoadmap_orders_required_gaps_above_preferred()
    {
        await using var db = CreateContext();
        await AddProfileAsync(db, 10);

        var skillA = await AddMasterSkillAsync(db, 1, "Skill A");
        var skillB = await AddMasterSkillAsync(db, 2, "Skill B");

        var posting1 = await AddPostingAsync(db, 10, PostingStatus.Confirmed);
        var posting2 = await AddPostingAsync(db, 10, PostingStatus.Confirmed);

        // Skill A is preferred in 2 postings (Required: 0, Preferred: 2)
        // Skill B is required in 1 posting (Required: 1, Preferred: 0)
        // Requirement FR-46 / BR-07: Required gaps rank above preferred gaps regardless of total count
        await AddMatchResultWithGapsAsync(db, posting1, (skillA.Id, SkillType.Preferred), (skillB.Id, SkillType.Required));
        await AddMatchResultWithGapsAsync(db, posting2, (skillA.Id, SkillType.Preferred));

        var service = new SkillGapService(db);
        var roadmap = await service.RebuildRoadmapAsync(10);

        Assert.Equal(2, roadmap.Count);
        // Skill B has RequiredCount=1 -> Rank 1
        Assert.Equal(skillB.Id, roadmap[0].MasterSkillId);
        Assert.Equal(1, roadmap[0].Priority);
        Assert.Equal(1, roadmap[0].RequiredOccurrenceCount);
        Assert.Equal(0, roadmap[0].PreferredOccurrenceCount);

        // Skill A has RequiredCount=0, PreferredCount=2 -> Rank 2
        Assert.Equal(skillA.Id, roadmap[1].MasterSkillId);
        Assert.Equal(2, roadmap[1].Priority);
        Assert.Equal(0, roadmap[1].RequiredOccurrenceCount);
        Assert.Equal(2, roadmap[1].PreferredOccurrenceCount);
    }

    [Fact]
    public async Task RebuildRoadmap_counts_occurrences_across_postings()
    {
        await using var db = CreateContext();
        await AddProfileAsync(db, 10);

        var skillC = await AddMasterSkillAsync(db, 1, "C#");
        var skillSql = await AddMasterSkillAsync(db, 2, "SQL");

        var posting1 = await AddPostingAsync(db, 10, PostingStatus.Confirmed);
        var posting2 = await AddPostingAsync(db, 10, PostingStatus.Confirmed);
        var posting3 = await AddPostingAsync(db, 10, PostingStatus.Confirmed);

        // C# is required in 3 postings
        // SQL is required in 1 posting and preferred in 1 posting
        await AddMatchResultWithGapsAsync(db, posting1, (skillC.Id, SkillType.Required), (skillSql.Id, SkillType.Required));
        await AddMatchResultWithGapsAsync(db, posting2, (skillC.Id, SkillType.Required), (skillSql.Id, SkillType.Preferred));
        await AddMatchResultWithGapsAsync(db, posting3, (skillC.Id, SkillType.Required));

        var service = new SkillGapService(db);
        var roadmap = await service.RebuildRoadmapAsync(10);

        Assert.Equal(2, roadmap.Count);

        var cItem = roadmap.First(r => r.MasterSkillId == skillC.Id);
        Assert.Equal(1, cItem.Priority);
        Assert.Equal(3, cItem.RequiredOccurrenceCount);
        Assert.Equal(0, cItem.PreferredOccurrenceCount);

        var sqlItem = roadmap.First(r => r.MasterSkillId == skillSql.Id);
        Assert.Equal(2, sqlItem.Priority);
        Assert.Equal(1, sqlItem.RequiredOccurrenceCount);
        Assert.Equal(1, sqlItem.PreferredOccurrenceCount);
    }

    [Fact]
    public async Task RebuildRoadmap_preserves_an_InProgress_status()
    {
        await using var db = CreateContext();
        var profile = await AddProfileAsync(db, 10);

        var skill = await AddMasterSkillAsync(db, 1, "Kubernetes");
        var posting = await AddPostingAsync(db, 10, PostingStatus.Confirmed);
        await AddMatchResultWithGapsAsync(db, posting, (skill.Id, SkillType.Required));

        // Existing roadmap item with InProgress status
        db.RoadmapItems.Add(new RoadmapItem
        {
            CandidateProfileId = profile.Id,
            MasterSkillId = skill.Id,
            Priority = 1,
            RequiredOccurrenceCount = 1,
            PreferredOccurrenceCount = 0,
            Status = RoadmapItemStatus.InProgress,
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var service = new SkillGapService(db);
        var roadmap = await service.RebuildRoadmapAsync(10);

        Assert.Single(roadmap);
        Assert.Equal(RoadmapItemStatus.InProgress, roadmap[0].Status);
    }

    [Fact]
    public async Task RebuildRoadmap_preserves_Completed_status_and_CompletedAt()
    {
        await using var db = CreateContext();
        var profile = await AddProfileAsync(db, 10);

        var skill = await AddMasterSkillAsync(db, 1, "Azure");
        var posting = await AddPostingAsync(db, 10, PostingStatus.Confirmed);
        await AddMatchResultWithGapsAsync(db, posting, (skill.Id, SkillType.Required));

        var completedAt = DateTimeOffset.UtcNow.AddDays(-2);
        db.RoadmapItems.Add(new RoadmapItem
        {
            CandidateProfileId = profile.Id,
            MasterSkillId = skill.Id,
            Priority = 1,
            RequiredOccurrenceCount = 1,
            PreferredOccurrenceCount = 0,
            Status = RoadmapItemStatus.Completed,
            CompletedAt = completedAt,
            UpdatedAt = completedAt
        });
        await db.SaveChangesAsync();

        var service = new SkillGapService(db);
        var roadmap = await service.RebuildRoadmapAsync(10);

        Assert.Single(roadmap);
        Assert.Equal(RoadmapItemStatus.Completed, roadmap[0].Status);
        Assert.Equal(completedAt, roadmap[0].CompletedAt);
    }

    [Fact]
    public async Task RebuildRoadmap_ignores_pending_postings()
    {
        await using var db = CreateContext();
        await AddProfileAsync(db, 10);

        var skill1 = await AddMasterSkillAsync(db, 1, "AWS");
        var skill2 = await AddMasterSkillAsync(db, 2, "GCP");

        // Posting 1 is Confirmed with AWS gap
        var confirmedPosting = await AddPostingAsync(db, 10, PostingStatus.Confirmed);
        await AddMatchResultWithGapsAsync(db, confirmedPosting, (skill1.Id, SkillType.Required));

        // Posting 2 is Pending with GCP gap -> BR-08 / FR-54: Pending postings are strictly excluded
        var pendingPosting = await AddPostingAsync(db, 10, PostingStatus.Pending);
        await AddMatchResultWithGapsAsync(db, pendingPosting, (skill2.Id, SkillType.Required));

        var service = new SkillGapService(db);
        var roadmap = await service.RebuildRoadmapAsync(10);

        Assert.Single(roadmap);
        Assert.Equal(skill1.Id, roadmap[0].MasterSkillId);
    }

    [Fact]
    public async Task SetRoadmapStatus_rejects_another_users_item()
    {
        await using var db = CreateContext();
        var user10Profile = await AddProfileAsync(db, 10);
        var user20Profile = await AddProfileAsync(db, 20);

        var skill = await AddMasterSkillAsync(db, 1, "Rust");

        var user20Item = new RoadmapItem
        {
            CandidateProfileId = user20Profile.Id,
            MasterSkillId = skill.Id,
            Priority = 1,
            Status = RoadmapItemStatus.NotStarted
        };
        db.RoadmapItems.Add(user20Item);
        await db.SaveChangesAsync();

        var service = new SkillGapService(db);

        // User 10 attempts to update User 20's roadmap item
        var result = await service.SetRoadmapStatusAsync(user20Item.Id, 10, RoadmapItemStatus.Completed);
        Assert.False(result);

        // Verify status unchanged in DB
        var itemInDb = await db.RoadmapItems.FindAsync(user20Item.Id);
        Assert.Equal(RoadmapItemStatus.NotStarted, itemInDb!.Status);
        Assert.Null(itemInDb.CompletedAt);

        // User 20 updates their own item -> succeeds
        var user20Result = await service.SetRoadmapStatusAsync(user20Item.Id, 20, RoadmapItemStatus.Completed);
        Assert.True(user20Result);
        Assert.Equal(RoadmapItemStatus.Completed, itemInDb.Status);
        Assert.NotNull(itemInDb.CompletedAt);

        // Transition back to InProgress -> clears CompletedAt
        var inProgressResult = await service.SetRoadmapStatusAsync(user20Item.Id, 20, RoadmapItemStatus.InProgress);
        Assert.True(inProgressResult);
        Assert.Equal(RoadmapItemStatus.InProgress, itemInDb.Status);
        Assert.Null(itemInDb.CompletedAt);
    }

    [Fact]
    public async Task RebuildRoadmap_secondary_sorts_by_skill_name_when_counts_are_equal()
    {
        await using var db = CreateContext();
        await AddProfileAsync(db, 10);

        var skillZ = await AddMasterSkillAsync(db, 1, "Zig");
        var skillA = await AddMasterSkillAsync(db, 2, "Assembly");

        var posting = await AddPostingAsync(db, 10, PostingStatus.Confirmed);
        await AddMatchResultWithGapsAsync(db, posting, (skillZ.Id, SkillType.Required), (skillA.Id, SkillType.Required));

        var service = new SkillGapService(db);
        var roadmap = await service.RebuildRoadmapAsync(10);

        Assert.Equal(2, roadmap.Count);
        // "Assembly" before "Zig" alphabetically
        Assert.Equal(skillA.Id, roadmap[0].MasterSkillId);
        Assert.Equal(1, roadmap[0].Priority);
        Assert.Equal(skillZ.Id, roadmap[1].MasterSkillId);
        Assert.Equal(2, roadmap[1].Priority);
    }

    [Fact]
    public async Task RebuildRoadmap_clears_old_items_when_no_gaps_remain()
    {
        await using var db = CreateContext();
        var profile = await AddProfileAsync(db, 10);

        var skill = await AddMasterSkillAsync(db, 1, "Python");

        // Old roadmap item
        db.RoadmapItems.Add(new RoadmapItem
        {
            CandidateProfileId = profile.Id,
            MasterSkillId = skill.Id,
            Priority = 1,
            Status = RoadmapItemStatus.InProgress
        });
        await db.SaveChangesAsync();

        var service = new SkillGapService(db);
        // Rebuilding with no confirmed postings with gaps
        var roadmap = await service.RebuildRoadmapAsync(10);

        Assert.Empty(roadmap);
        Assert.Empty(db.RoadmapItems);
    }

    [Fact]
    public async Task GetRoadmap_returns_items_ordered_by_priority()
    {
        await using var db = CreateContext();
        var profile = await AddProfileAsync(db, 10);

        var skill1 = await AddMasterSkillAsync(db, 1, "C#");
        var skill2 = await AddMasterSkillAsync(db, 2, "Go");

        db.RoadmapItems.Add(new RoadmapItem
        {
            CandidateProfileId = profile.Id,
            MasterSkillId = skill2.Id,
            Priority = 2
        });
        db.RoadmapItems.Add(new RoadmapItem
        {
            CandidateProfileId = profile.Id,
            MasterSkillId = skill1.Id,
            Priority = 1
        });
        await db.SaveChangesAsync();

        var service = new SkillGapService(db);
        var roadmap = await service.GetRoadmapAsync(10);

        Assert.Equal(2, roadmap.Count);
        Assert.Equal(1, roadmap[0].Priority);
        Assert.Equal(skill1.Id, roadmap[0].MasterSkillId);
        Assert.Equal(2, roadmap[1].Priority);
        Assert.Equal(skill2.Id, roadmap[1].MasterSkillId);
    }

    private static JobAlignDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<JobAlignDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new JobAlignDbContext(options);
    }

    private static async Task<CandidateProfile> AddProfileAsync(JobAlignDbContext db, int userId)
    {
        var profile = new CandidateProfile
        {
            UserId = userId,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.CandidateProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    private static async Task<MasterSkill> AddMasterSkillAsync(JobAlignDbContext db, int id, string name)
    {
        var skill = new MasterSkill
        {
            Id = id,
            Name = name,
            NormalizedName = name.ToLowerInvariant().Replace(" ", "").Replace("#", "sharp"),
            IsActive = true
        };
        db.MasterSkills.Add(skill);
        await db.SaveChangesAsync();
        return skill;
    }

    private static async Task<JobPosting> AddPostingAsync(
        JobAlignDbContext db,
        int ownerUserId,
        PostingStatus status)
    {
        var posting = new JobPosting(
            ownerUserId,
            $"REF-{Guid.NewGuid():N}",
            "Example raw text",
            PostingCaptureMethod.PastedText)
        {
            Status = status
        };
        db.JobPostings.Add(posting);
        await db.SaveChangesAsync();
        return posting;
    }

    private static async Task<MatchResult> AddMatchResultWithGapsAsync(
        JobAlignDbContext db,
        JobPosting posting,
        params (int MasterSkillId, SkillType SkillType)[] gaps)
    {
        var result = new MatchResult
        {
            JobPostingId = posting.Id,
            CandidateProfileId = posting.OwnerUserId,
            ScoringConfigVersion = "weights-v1",
            CalculatedAt = DateTimeOffset.UtcNow
        };

        foreach (var (masterSkillId, skillType) in gaps)
        {
            result.SkillGaps.Add(new SkillGap
            {
                MasterSkillId = masterSkillId,
                SkillType = skillType
            });
        }

        db.MatchResults.Add(result);
        await db.SaveChangesAsync();
        return result;
    }
}
