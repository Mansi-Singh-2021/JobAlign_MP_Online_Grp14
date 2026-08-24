using System.Globalization;
using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Matching;
using JobAlign.Core.Entities.Postings;
using JobAlign.Core.Entities.Profiles;
using JobAlign.Core.Enums;
using JobAlign.Core.Matching;
using JobAlign.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace JobAlign.Infrastructure.Services;

/// <summary>Persists explainable match scores and skill gaps (FR-35 to FR-43).</summary>
public sealed class MatchScoringService : IMatchScoringService
{
    private readonly JobAlignDbContext _db;

    public MatchScoringService(JobAlignDbContext db)
    {
        _db = db;
    }

    public async Task<MatchResult?> ScoreAsync(
        int postingId,
        int ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var posting = await ConfirmedPostings(ownerUserId)
            .Where(p => p.Id == postingId)
            .Include(p => p.MatchResult!)
                .ThenInclude(result => result.SkillGaps)
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);

        if (posting is null)
            return null;

        var profile = await _db.CandidateProfiles
            .Include(p => p.Skills)
            .SingleOrDefaultAsync(p => p.UserId == ownerUserId, cancellationToken);

        if (profile is null)
            return null;

        var heldSkillIds = profile.Skills
            .Select(s => s.MasterSkillId)
            .ToHashSet();

        var result = posting.MatchResult;
        if (result is null)
        {
            result = new MatchResult
            {
                JobPostingId = posting.Id,
                CandidateProfileId = profile.Id,
                ScoringConfigVersion = ScoringWeights.Version
            };
            _db.MatchResults.Add(result);
        }

        ReplaceScoreAndGaps(posting, profile, heldSkillIds, result, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        return result;
    }

    public async Task<int> RecalculateAllAsync(
        int ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _db.CandidateProfiles
            .Include(p => p.Skills)
            .SingleOrDefaultAsync(p => p.UserId == ownerUserId, cancellationToken);

        if (profile is null)
            return 0;

        // One set-based load for the whole library. AsSplitQuery avoids a cartesian
        // product between skills, extractions and corrections; the query count stays
        // constant as the number of postings grows (FR-41, NFR-03).
        var postings = await ConfirmedPostings(ownerUserId)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        if (postings.Count == 0)
            return 0;

        var postingIds = postings.Select(p => p.Id).ToArray();
        var resultsByPostingId = await _db.MatchResults
            .Where(r => r.CandidateProfileId == profile.Id && postingIds.Contains(r.JobPostingId))
            .Include(r => r.SkillGaps)
            .AsSplitQuery()
            .ToDictionaryAsync(r => r.JobPostingId, cancellationToken);

        var heldSkillIds = profile.Skills
            .Select(s => s.MasterSkillId)
            .ToHashSet();
        var calculatedAt = DateTimeOffset.UtcNow;

        foreach (var posting in postings)
        {
            if (!resultsByPostingId.TryGetValue(posting.Id, out var result))
            {
                result = new MatchResult
                {
                    JobPostingId = posting.Id,
                    CandidateProfileId = profile.Id,
                    ScoringConfigVersion = ScoringWeights.Version
                };
                _db.MatchResults.Add(result);
            }

            ReplaceScoreAndGaps(posting, profile, heldSkillIds, result, calculatedAt);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return postings.Count;
    }

    private IQueryable<JobPosting> ConfirmedPostings(int ownerUserId) =>
        _db.JobPostings
            .Where(p => p.OwnerUserId == ownerUserId && p.Status == PostingStatus.Confirmed)
            .Include(p => p.Skills)
            .Include(p => p.Extractions.Where(e => e.IsCurrent))
            .Include(p => p.Corrections);

    private void ReplaceScoreAndGaps(
        JobPosting posting,
        CandidateProfile profile,
        IReadOnlySet<int> heldSkillIds,
        MatchResult result,
        DateTimeOffset calculatedAt)
    {
        var requiredSkills = posting.Skills
            .Where(s => s.SkillType == SkillType.Required)
            .ToArray();
        var preferredSkills = posting.Skills
            .Where(s => s.SkillType == SkillType.Preferred)
            .ToArray();

        var requiredScore = ScoreCalculator.RequiredSkillScore(
            requiredSkills.Length,
            requiredSkills.Count(s => heldSkillIds.Contains(s.MasterSkillId)));
        var preferredScore = ScoreCalculator.PreferredSkillScore(
            preferredSkills.Length,
            preferredSkills.Count(s => heldSkillIds.Contains(s.MasterSkillId)));
        var experienceScore = ScoreCalculator.ExperienceScore(
            profile.TotalExperienceYears,
            EffectiveMinimumExperience(posting));

        result.CandidateProfileId = profile.Id;
        result.RequiredSkillScore = requiredScore;
        result.PreferredSkillScore = preferredScore;
        result.ExperienceScore = experienceScore;
        result.OverallScore = ScoreCalculator.OverallScore(
            requiredScore,
            preferredScore,
            experienceScore);
        result.ScoringConfigVersion = ScoringWeights.Version;
        result.CalculatedAt = calculatedAt;

        // FeedbackText and FeedbackGeneratedAt belong to Role F and are deliberately
        // left untouched during an upsert.
        _db.SkillGaps.RemoveRange(result.SkillGaps);
        result.SkillGaps.Clear();

        foreach (var missingSkill in posting.Skills.Where(
                     s => !heldSkillIds.Contains(s.MasterSkillId)))
        {
            result.SkillGaps.Add(new SkillGap
            {
                MasterSkillId = missingSkill.MasterSkillId,
                SkillType = missingSkill.SkillType
            });
        }
    }

    private static decimal? EffectiveMinimumExperience(JobPosting posting)
    {
        var correction = posting.Corrections.SingleOrDefault(
            c => c.FieldName == CorrectableFields.ExperienceMinYears);

        if (correction is not null)
        {
            if (string.IsNullOrWhiteSpace(correction.CorrectedValue))
                return null;

            return decimal.TryParse(
                correction.CorrectedValue,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var correctedYears)
                    ? correctedYears
                    : null;
        }

        return posting.Extractions.SingleOrDefault()?.ExperienceMinYears;
    }
}
