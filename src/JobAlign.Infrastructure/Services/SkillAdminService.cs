using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Skills;
using JobAlign.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobAlign.Infrastructure.Services;

/// <inheritdoc cref="ISkillAdminService"/>
public class SkillAdminService : ISkillAdminService
{
    private readonly JobAlignDbContext _db;
    private readonly ISkillResolver _resolver;
    private readonly ILogger<SkillAdminService> _logger;

    public SkillAdminService(
        JobAlignDbContext db,
        ISkillResolver resolver,
        ILogger<SkillAdminService> logger)
    {
        _db = db;
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MasterSkill>> ListAsync(
        string? search,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var query = _db.MasterSkills.AsNoTracking();

        if (!includeInactive)
            query = query.Where(s => s.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Search the normalized form too, so looking for "c sharp" finds "C#".
            var normalized = _resolver.Normalize(search);
            var term = search.Trim();

            query = query.Where(s =>
                s.Name.Contains(term) ||
                (normalized.Length > 0 && s.NormalizedName.Contains(normalized)));
        }

        return await query
            .Include(s => s.Aliases)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<MasterSkill?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        _db.MasterSkills
            .AsNoTracking()
            .Include(s => s.Aliases)
            .Include(s => s.MergedIntoMasterSkill)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<SkillAdminResult> CreateAsync(
        string name,
        string? category,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return SkillAdminResult.Fail("A skill name is required.");

        var normalized = _resolver.Normalize(name);

        if (normalized.Length == 0)
            return SkillAdminResult.Fail("That name normalizes to nothing — it needs at least one letter or digit.");

        if (await TakenAsync(normalized, null, cancellationToken) is { } clash)
            return SkillAdminResult.Fail(clash);

        _db.MasterSkills.Add(new MasterSkill
        {
            Name = name.Trim(),
            NormalizedName = normalized,
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Master skill {Name} created.", name);

        return SkillAdminResult.Ok();
    }

    public async Task<SkillAdminResult> UpdateAsync(
        int id,
        string name,
        string? category,
        CancellationToken cancellationToken = default)
    {
        var skill = await _db.MasterSkills.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (skill is null)
            return SkillAdminResult.Fail("That skill no longer exists.");

        if (string.IsNullOrWhiteSpace(name))
            return SkillAdminResult.Fail("A skill name is required.");

        var normalized = _resolver.Normalize(name);

        if (normalized.Length == 0)
            return SkillAdminResult.Fail("That name normalizes to nothing — it needs at least one letter or digit.");

        if (await TakenAsync(normalized, id, cancellationToken) is { } clash)
            return SkillAdminResult.Fail(clash);

        skill.Name = name.Trim();
        skill.NormalizedName = normalized;
        skill.Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();

        await _db.SaveChangesAsync(cancellationToken);
        return SkillAdminResult.Ok();
    }

    public async Task<SkillAdminResult> SetActiveAsync(
        int id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var skill = await _db.MasterSkills.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (skill is null)
            return SkillAdminResult.Fail("That skill no longer exists.");

        // Reactivating a merged skill would give two live rows for one concept, and the
        // resolver would still forward it to the target. Undo the merge first.
        if (isActive && skill.MergedIntoMasterSkillId is not null)
            return SkillAdminResult.Fail("This skill was merged into another and cannot be reactivated on its own.");

        skill.IsActive = isActive;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Master skill {Name} {State}.", skill.Name, isActive ? "reactivated" : "deactivated");
        return SkillAdminResult.Ok();
    }

    public async Task<IReadOnlyList<SkillAlias>> ListAliasesAsync(
        int masterSkillId,
        CancellationToken cancellationToken = default) =>
        await _db.SkillAliases
            .AsNoTracking()
            .Where(a => a.MasterSkillId == masterSkillId)
            .OrderBy(a => a.Alias)
            .ToListAsync(cancellationToken);

    public async Task<SkillAdminResult> AddAliasAsync(
        int masterSkillId,
        string alias,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return SkillAdminResult.Fail("An alias is required.");

        var skill = await _db.MasterSkills.FirstOrDefaultAsync(s => s.Id == masterSkillId, cancellationToken);

        if (skill is null)
            return SkillAdminResult.Fail("That skill no longer exists.");

        var normalized = _resolver.Normalize(alias);

        if (normalized.Length == 0)
            return SkillAdminResult.Fail("That alias normalizes to nothing — it needs at least one letter or digit.");

        if (normalized == skill.NormalizedName)
            return SkillAdminResult.Fail($"\"{alias}\" already reads as \"{skill.Name}\" — no alias is needed.");

        if (await TakenAsync(normalized, null, cancellationToken) is { } clash)
            return SkillAdminResult.Fail(clash);

        _db.SkillAliases.Add(new SkillAlias
        {
            MasterSkillId = masterSkillId,
            Alias = alias.Trim(),
            NormalizedAlias = normalized,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Alias {Alias} added for {Name}.", alias, skill.Name);

        return SkillAdminResult.Ok();
    }

    public async Task<SkillAdminResult> RemoveAliasAsync(
        int aliasId,
        CancellationToken cancellationToken = default)
    {
        var alias = await _db.SkillAliases.FirstOrDefaultAsync(a => a.Id == aliasId, cancellationToken);

        if (alias is null)
            return SkillAdminResult.Fail("That alias no longer exists.");

        // Aliases carry no history — they are a lookup convenience, not a record of anything
        // that happened — so unlike skills they are genuinely deleted.
        _db.SkillAliases.Remove(alias);
        await _db.SaveChangesAsync(cancellationToken);

        return SkillAdminResult.Ok();
    }

    public async Task<SkillAdminResult> MergeAsync(
        int sourceId,
        int targetId,
        CancellationToken cancellationToken = default)
    {
        if (sourceId == targetId)
            return SkillAdminResult.Fail("A skill cannot be merged into itself.");

        var source = await _db.MasterSkills.FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken);
        var target = await _db.MasterSkills.FirstOrDefaultAsync(s => s.Id == targetId, cancellationToken);

        if (source is null || target is null)
            return SkillAdminResult.Fail("Both skills must exist.");

        if (source.MergedIntoMasterSkillId is not null)
            return SkillAdminResult.Fail($"\"{source.Name}\" has already been merged into another skill.");

        // Merging into something that is itself merged would build a chain the resolver has
        // to walk, and merging in a circle would make it walk forever.
        if (target.MergedIntoMasterSkillId is not null)
            return SkillAdminResult.Fail($"\"{target.Name}\" has itself been merged into another skill. Merge into that one instead.");

        if (!target.IsActive)
            return SkillAdminResult.Fail($"\"{target.Name}\" is deactivated. Reactivate it before merging into it.");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        await MoveAliasesAsync(source, target, cancellationToken);
        await RepointSkillReferencesAsync(sourceId, targetId, cancellationToken);

        // The row stays. Postings and profiles may still reference it, and FR-58 keeps it so
        // those references remain explainable; the resolver forwards it to the target.
        source.MergedIntoMasterSkillId = targetId;
        source.IsActive = false;

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Merged master skill {Source} into {Target}.", source.Name, target.Name);
        return SkillAdminResult.Ok();
    }

    // ----------------------------------------------------------------

    /// <summary>
    /// Whether a normalized form already resolves to something. Checks skills and aliases
    /// together: if "K8s" is an alias, a new skill called "K8S" would give the resolver two
    /// answers for one input, which BR-04 forbids.
    /// </summary>
    private async Task<string?> TakenAsync(string normalized, int? ignoreSkillId, CancellationToken cancellationToken)
    {
        var skill = await _db.MasterSkills
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.NormalizedName == normalized && s.Id != ignoreSkillId, cancellationToken);

        if (skill is not null)
            return $"That name already reads the same as the existing skill \"{skill.Name}\".";

        var alias = await _db.SkillAliases
            .AsNoTracking()
            .Include(a => a.MasterSkill)
            .FirstOrDefaultAsync(a => a.NormalizedAlias == normalized, cancellationToken);

        return alias is null
            ? null
            : $"That name is already an alias of \"{alias.MasterSkill.Name}\".";
    }

    /// <summary>
    /// Moves the source's aliases to the target, and adds the source's own name as an alias so
    /// postings still worded the old way keep resolving. Anything that would duplicate an
    /// existing alias is dropped — <c>NormalizedAlias</c> is unique.
    /// </summary>
    private async Task MoveAliasesAsync(MasterSkill source, MasterSkill target, CancellationToken cancellationToken)
    {
        var sourceAliases = await _db.SkillAliases
            .Where(a => a.MasterSkillId == source.Id)
            .ToListAsync(cancellationToken);

        var targetNormalized = target.NormalizedName;

        var existing = await _db.SkillAliases
            .Where(a => a.MasterSkillId == target.Id)
            .Select(a => a.NormalizedAlias)
            .ToListAsync(cancellationToken);

        var taken = existing.ToHashSet(StringComparer.Ordinal);
        taken.Add(targetNormalized);

        foreach (var alias in sourceAliases)
        {
            if (taken.Add(alias.NormalizedAlias))
                alias.MasterSkillId = target.Id;
            else
                _db.SkillAliases.Remove(alias);
        }

        // The source's canonical name becomes an alias of the target. Without this, a posting
        // saying "Kubernets" after that skill was merged away would stop resolving entirely.
        if (taken.Add(source.NormalizedName))
        {
            _db.SkillAliases.Add(new SkillAlias
            {
                MasterSkillId = target.Id,
                Alias = source.Name,
                NormalizedAlias = source.NormalizedName,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    /// <summary>
    /// Repoints stored posting and profile skills from the source to the target.
    /// </summary>
    /// <remarks>
    /// Scoring compares <c>MasterSkillId</c> directly, so without this a candidate holding the
    /// target skill would still fail to match a posting that recorded the source — the merge
    /// would be invisible to the thing it most affects.
    ///
    /// Both tables are unique on (owner, master skill), so a row whose owner already holds the
    /// target is deleted rather than repointed. Derived rows — <c>SkillGaps</c>,
    /// <c>RoadmapItems</c> — are left alone; rescoring regenerates them.
    /// </remarks>
    private async Task RepointSkillReferencesAsync(int sourceId, int targetId, CancellationToken cancellationToken)
    {
        var postingSkills = await _db.PostingSkills
            .Where(p => p.MasterSkillId == sourceId)
            .ToListAsync(cancellationToken);

        var postingsWithTarget = await _db.PostingSkills
            .Where(p => p.MasterSkillId == targetId)
            .Select(p => p.JobPostingId)
            .ToListAsync(cancellationToken);

        var postingSet = postingsWithTarget.ToHashSet();

        foreach (var row in postingSkills)
        {
            if (postingSet.Add(row.JobPostingId))
                row.MasterSkillId = targetId;
            else
                _db.PostingSkills.Remove(row);
        }

        var profileSkills = await _db.ProfileSkills
            .Where(p => p.MasterSkillId == sourceId)
            .ToListAsync(cancellationToken);

        var profilesWithTarget = await _db.ProfileSkills
            .Where(p => p.MasterSkillId == targetId)
            .Select(p => p.CandidateProfileId)
            .ToListAsync(cancellationToken);

        var profileSet = profilesWithTarget.ToHashSet();

        foreach (var row in profileSkills)
        {
            if (profileSet.Add(row.CandidateProfileId))
                row.MasterSkillId = targetId;
            else
                _db.ProfileSkills.Remove(row);
        }
    }
}
