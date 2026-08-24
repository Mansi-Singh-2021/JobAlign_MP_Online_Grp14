using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Matching;
using JobAlign.Core.Entities.Profiles;
using JobAlign.Core.Enums;
using JobAlign.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace JobAlign.Infrastructure.Services;

/// <inheritdoc cref="ISkillGapService" />
public sealed class SkillGapService : ISkillGapService
{
    private readonly JobAlignDbContext _db;

    public SkillGapService(JobAlignDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<SkillGap>> GetGapsForPostingAsync(
        int postingId, int ownerUserId, CancellationToken cancellationToken = default)
    {
        // Ownership enforcement (BR-09): Verify posting exists for this candidate
        var postingExists = await _db.JobPostings
            .AsNoTracking()
            .AnyAsync(p => p.Id == postingId && p.OwnerUserId == ownerUserId, cancellationToken);

        if (!postingExists)
            return [];

        return await _db.SkillGaps
            .AsNoTracking()
            .Where(g => g.MatchResult.JobPostingId == postingId && g.MatchResult.JobPosting.OwnerUserId == ownerUserId)
            .Include(g => g.MasterSkill)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RoadmapItem>> RebuildRoadmapAsync(
        int ownerUserId, CancellationToken cancellationToken = default)
    {
        var profile = await _db.CandidateProfiles
            .FirstOrDefaultAsync(p => p.UserId == ownerUserId, cancellationToken);

        if (profile is null)
            return [];

        // Preserve status of existing roadmap items (FR-47)
        var existingItems = await _db.RoadmapItems
            .Where(r => r.CandidateProfileId == profile.Id)
            .ToListAsync(cancellationToken);

        var existingStatusMap = existingItems
            .ToDictionary(r => r.MasterSkillId, r => (r.Status, r.CompletedAt));

        // FR-45, FR-46, BR-08: Gaps across Confirmed postings only (Pending excluded)
        var gaps = await _db.SkillGaps
            .AsNoTracking()
            .Where(g => g.MatchResult.JobPosting.OwnerUserId == ownerUserId
                        && g.MatchResult.JobPosting.Status == PostingStatus.Confirmed)
            .Include(g => g.MasterSkill)
            .ToListAsync(cancellationToken);

        if (existingItems.Count > 0)
        {
            _db.RoadmapItems.RemoveRange(existingItems);
        }

        if (gaps.Count == 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            return [];
        }

        // Group gaps by MasterSkillId
        var groupedGaps = gaps
            .GroupBy(g => g.MasterSkillId)
            .Select(group =>
            {
                var masterSkill = group.First().MasterSkill;
                var requiredCount = group.Count(g => g.SkillType == SkillType.Required);
                var preferredCount = group.Count(g => g.SkillType == SkillType.Preferred);

                return new
                {
                    MasterSkillId = group.Key,
                    MasterSkill = masterSkill,
                    RequiredOccurrenceCount = requiredCount,
                    PreferredOccurrenceCount = preferredCount,
                    SkillName = masterSkill?.Name ?? string.Empty
                };
            })
            // FR-46: Order by required count descending, then preferred count descending, then skill name
            .OrderByDescending(g => g.RequiredOccurrenceCount)
            .ThenByDescending(g => g.PreferredOccurrenceCount)
            .ThenBy(g => g.SkillName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var now = DateTimeOffset.UtcNow;
        var newRoadmapItems = new List<RoadmapItem>();

        for (int i = 0; i < groupedGaps.Count; i++)
        {
            var itemData = groupedGaps[i];
            var status = RoadmapItemStatus.NotStarted;
            DateTimeOffset? completedAt = null;

            if (existingStatusMap.TryGetValue(itemData.MasterSkillId, out var existing))
            {
                status = existing.Status;
                completedAt = existing.CompletedAt;
            }

            var roadmapItem = new RoadmapItem
            {
                CandidateProfileId = profile.Id,
                MasterSkillId = itemData.MasterSkillId,
                Priority = i + 1,
                RequiredOccurrenceCount = itemData.RequiredOccurrenceCount,
                PreferredOccurrenceCount = itemData.PreferredOccurrenceCount,
                Status = status,
                CompletedAt = completedAt,
                UpdatedAt = now
            };

            newRoadmapItems.Add(roadmapItem);
        }

        _db.RoadmapItems.AddRange(newRoadmapItems);
        await _db.SaveChangesAsync(cancellationToken);

        return await _db.RoadmapItems
            .AsNoTracking()
            .Where(r => r.CandidateProfileId == profile.Id)
            .Include(r => r.MasterSkill)
            .OrderBy(r => r.Priority)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RoadmapItem>> GetRoadmapAsync(
        int ownerUserId, CancellationToken cancellationToken = default)
    {
        return await _db.RoadmapItems
            .AsNoTracking()
            .Where(r => r.CandidateProfile.UserId == ownerUserId)
            .Include(r => r.MasterSkill)
            .OrderBy(r => r.Priority)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> SetRoadmapStatusAsync(
        int roadmapItemId, int ownerUserId, RoadmapItemStatus status, CancellationToken cancellationToken = default)
    {
        var item = await _db.RoadmapItems
            .Include(r => r.CandidateProfile)
            .FirstOrDefaultAsync(r => r.Id == roadmapItemId && r.CandidateProfile.UserId == ownerUserId, cancellationToken);

        if (item is null)
            return false;

        item.Status = status;
        if (status == RoadmapItemStatus.Completed)
        {
            item.CompletedAt ??= DateTimeOffset.UtcNow;
        }
        else
        {
            item.CompletedAt = null;
        }

        item.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
