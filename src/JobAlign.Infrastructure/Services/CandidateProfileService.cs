using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Profiles;
using JobAlign.Core.Enums;
using JobAlign.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobAlign.Infrastructure.Services;

/// <inheritdoc cref="ICandidateProfileService" />
/// <inheritdoc cref="IProfileEntryService" />
public sealed class CandidateProfileService : ICandidateProfileService, IProfileEntryService
{
    private readonly JobAlignDbContext _db;
    private readonly ISkillResolver _skillResolver;
    private readonly IServiceProvider _services;
    private readonly ILogger<CandidateProfileService> _logger;

    public CandidateProfileService(
        JobAlignDbContext db,
        ISkillResolver skillResolver,
        IServiceProvider services,
        ILogger<CandidateProfileService> logger)
    {
        _db = db;
        _skillResolver = skillResolver;
        _services = services;
        _logger = logger;
    }

    public async Task<CandidateProfile?> GetAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        return await _db.CandidateProfiles
            .AsNoTracking()
            .Include(p => p.Education)
            .Include(p => p.WorkExperience)
            .Include(p => p.Projects)
            .Include(p => p.Certifications)
            .Include(p => p.Skills)
                .ThenInclude(s => s.MasterSkill)
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
    }

    public async Task UpdateDetailsAsync(
        int userId,
        ProfileDetails details,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetTrackedProfileAsync(userId, cancellationToken);
        if (profile is null)
            return;

        profile.FullName = NormalizeOptional(details.FullName);
        profile.Headline = NormalizeOptional(details.Headline);
        profile.CurrentRole = NormalizeOptional(details.CurrentRole);
        profile.PhoneNumber = NormalizeOptional(details.PhoneNumber);
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SkillResolution> AddSkillAsync(
        int userId,
        string rawSkillText,
        ProficiencyLevel level,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _skillResolver.ResolveAsync(rawSkillText, cancellationToken);

        // BR-04: an unresolved skill must never become a free-text identity.
        if (!resolution.IsResolved)
            return resolution;

        var profile = await GetTrackedProfileAsync(userId, cancellationToken);
        if (profile is null)
            return resolution;

        var existing = await _db.ProfileSkills
            .FirstOrDefaultAsync(
                s => s.CandidateProfileId == profile.Id && s.MasterSkillId == resolution.MasterSkillId!.Value,
                cancellationToken);

        if (existing is null)
        {
            _db.ProfileSkills.Add(new ProfileSkill
            {
                CandidateProfileId = profile.Id,
                MasterSkillId = resolution.MasterSkillId.Value,
                ProficiencyLevel = level,
                Source = ProfileSkillSource.Manual,
                ConfirmedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            // FR-28: adding an already-held skill updates proficiency rather than duplicating it.
            existing.ProficiencyLevel = level;
            existing.Source = ProfileSkillSource.Manual;
            existing.ConfirmedAt = DateTimeOffset.UtcNow;
        }

        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await RecalculateMatchesSafelyAsync(userId, cancellationToken);

        return resolution;
    }

    public async Task<bool> RemoveSkillAsync(
        int userId,
        int profileSkillId,
        CancellationToken cancellationToken = default)
    {
        var skill = await _db.ProfileSkills
            .Include(s => s.CandidateProfile)
            .FirstOrDefaultAsync(
                s => s.Id == profileSkillId && s.CandidateProfile.UserId == userId,
                cancellationToken);

        if (skill is null)
            return false;

        var candidateProfileId = skill.CandidateProfileId;
        _db.ProfileSkills.Remove(skill);

        var profile = await _db.CandidateProfiles
            .FirstOrDefaultAsync(p => p.Id == candidateProfileId && p.UserId == userId, cancellationToken);
        if (profile is not null)
            profile.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await RecalculateMatchesSafelyAsync(userId, cancellationToken);
        return true;
    }

    public async Task RecalculateTotalExperienceAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _db.CandidateProfiles
            .Include(p => p.WorkExperience)
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (profile is null)
            return;

        var ranges = profile.WorkExperience
            .Where(w => w.StartDate.HasValue)
            .Select(w =>
            {
                var start = w.StartDate!.Value;
                var end = w.IsCurrent || !w.EndDate.HasValue
                    ? DateOnly.FromDateTime(DateTime.UtcNow.Date)
                    : w.EndDate.Value;
                return (Start: start, End: end);
            })
            .Where(r => r.End >= r.Start)
            .OrderBy(r => r.Start)
            .ThenBy(r => r.End)
            .ToList();

        if (ranges.Count == 0)
        {
            // BR-02: no recorded experience is null, not zero.
            profile.TotalExperienceYears = null;
        }
        else
        {
            var merged = new List<(DateOnly Start, DateOnly End)>();
            foreach (var range in ranges)
            {
                if (merged.Count == 0)
                {
                    merged.Add(range);
                    continue;
                }

                var last = merged[^1];
                if (range.Start <= last.End)
                {
                    merged[^1] = (last.Start, range.End > last.End ? range.End : last.End);
                }
                else
                {
                    merged.Add(range);
                }
            }

            var totalDays = merged.Sum(r => (r.End.ToDateTime(TimeOnly.MinValue) - r.Start.ToDateTime(TimeOnly.MinValue)).TotalDays);
            profile.TotalExperienceYears = Math.Round((decimal)totalDays / 365.25m, 2, MidpointRounding.AwayFromZero);
        }

        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await RecalculateMatchesSafelyAsync(userId, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Candidate-owned collection entries. These methods deliberately live behind
    // IProfileEntryService so the shared ICandidateProfileService contract remains
    // byte-for-byte compatible with 01-CONTRACTS.md.

    public async Task<EducationEntry?> AddEducationAsync(
        int userId,
        EducationEntry entry,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetTrackedProfileAsync(userId, cancellationToken);
        if (profile is null)
            return null;

        if (string.IsNullOrWhiteSpace(entry.Institution))
            throw new ArgumentException("Institution is required.", nameof(entry));

        entry.Id = 0;
        entry.CandidateProfileId = profile.Id;
        entry.CandidateProfile = profile;
        _db.EducationEntries.Add(entry);
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public async Task<bool> RemoveEducationAsync(int userId, int entryId, CancellationToken cancellationToken = default)
    {
        var entry = await _db.EducationEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.CandidateProfile.UserId == userId, cancellationToken);
        if (entry is null) return false;
        _db.EducationEntries.Remove(entry);
        await TouchAndSaveAsync(userId, cancellationToken);
        return true;
    }

    public async Task<WorkExperienceEntry?> AddWorkExperienceAsync(
        int userId,
        WorkExperienceEntry entry,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetTrackedProfileAsync(userId, cancellationToken);
        if (profile is null)
            return null;

        if (string.IsNullOrWhiteSpace(entry.CompanyName) || string.IsNullOrWhiteSpace(entry.JobTitle))
            throw new ArgumentException("Company name and job title are required.", nameof(entry));

        if (entry.StartDate.HasValue && entry.EndDate.HasValue && entry.EndDate < entry.StartDate)
            throw new ArgumentException("End date cannot be before start date.", nameof(entry));

        if (entry.IsCurrent)
            entry.EndDate = null;

        entry.Id = 0;
        entry.CandidateProfileId = profile.Id;
        entry.CandidateProfile = profile;
        _db.WorkExperienceEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);

        await RecalculateTotalExperienceAsync(userId, cancellationToken);
        return entry;
    }

    public async Task<bool> RemoveWorkExperienceAsync(int userId, int entryId, CancellationToken cancellationToken = default)
    {
        var entry = await _db.WorkExperienceEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.CandidateProfile.UserId == userId, cancellationToken);
        if (entry is null) return false;
        _db.WorkExperienceEntries.Remove(entry);
        await _db.SaveChangesAsync(cancellationToken);
        await RecalculateTotalExperienceAsync(userId, cancellationToken);
        return true;
    }

    public async Task<ProjectEntry?> AddProjectAsync(
        int userId,
        ProjectEntry entry,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetTrackedProfileAsync(userId, cancellationToken);
        if (profile is null)
            return null;
        if (string.IsNullOrWhiteSpace(entry.Name))
            throw new ArgumentException("Project name is required.", nameof(entry));

        entry.Id = 0;
        entry.CandidateProfileId = profile.Id;
        entry.CandidateProfile = profile;
        _db.ProjectEntries.Add(entry);
        await TouchAndSaveAsync(userId, cancellationToken);
        return entry;
    }

    public async Task<bool> RemoveProjectAsync(int userId, int entryId, CancellationToken cancellationToken = default)
    {
        var entry = await _db.ProjectEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.CandidateProfile.UserId == userId, cancellationToken);
        if (entry is null) return false;
        _db.ProjectEntries.Remove(entry);
        await TouchAndSaveAsync(userId, cancellationToken);
        return true;
    }

    public async Task<CertificationEntry?> AddCertificationAsync(
        int userId,
        CertificationEntry entry,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetTrackedProfileAsync(userId, cancellationToken);
        if (profile is null)
            return null;
        if (string.IsNullOrWhiteSpace(entry.Name))
            throw new ArgumentException("Certification name is required.", nameof(entry));

        entry.Id = 0;
        entry.CandidateProfileId = profile.Id;
        entry.CandidateProfile = profile;
        _db.CertificationEntries.Add(entry);
        await TouchAndSaveAsync(userId, cancellationToken);
        return entry;
    }

    public async Task<bool> RemoveCertificationAsync(int userId, int entryId, CancellationToken cancellationToken = default)
    {
        var entry = await _db.CertificationEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.CandidateProfile.UserId == userId, cancellationToken);
        if (entry is null) return false;
        _db.CertificationEntries.Remove(entry);
        await TouchAndSaveAsync(userId, cancellationToken);
        return true;
    }

    private Task<CandidateProfile?> GetTrackedProfileAsync(int userId, CancellationToken cancellationToken) =>
        _db.CandidateProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    private async Task TouchAndSaveAsync(int userId, CancellationToken cancellationToken)
    {
        var profile = await GetTrackedProfileAsync(userId, cancellationToken);
        if (profile is not null)
            profile.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RecalculateMatchesSafelyAsync(int userId, CancellationToken cancellationToken)
    {
        // D's service is a planned dependency. Keeping this optional lets C run against
        // the current main branch before D lands, while still wiring FR-41 automatically
        // once IMatchedScoringService is registered.
        // Role D is not present on the current main branch yet. Resolve it by contract name
        // when it lands, without making Role C fail to compile before that merge.
        var scoringType = Type.GetType("JobAlign.Core.Abstractions.IMatchScoringService, JobAlign.Core");
        if (scoringType is null)
            return;

        var scoring = _services.GetService(scoringType);
        var method = scoringType.GetMethod("RecalculateAllAsync");
        if (scoring is null || method is null)
            return;

        try
        {
            var task = method.Invoke(scoring, [userId, cancellationToken]) as Task;
            if (task is not null)
                await task;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not OperationCanceledException)
        {
            // A scoring failure must not roll back an already-successful profile change (FR-41).
            _logger.LogWarning(ex.InnerException, "Profile changed for user {UserId}, but match rescoring failed.", userId);
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
