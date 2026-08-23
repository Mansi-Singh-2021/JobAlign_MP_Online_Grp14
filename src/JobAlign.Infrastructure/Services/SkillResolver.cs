using System.Text;
using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Skills;
using JobAlign.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobAlign.Infrastructure.Services;

/// <inheritdoc cref="ISkillResolver"/>
public class SkillResolver : ISkillResolver
{
    /// <summary>
    /// A merge can point at a skill that was itself merged (FR-58). Following the chain has
    /// to stop somewhere, and a cycle from a mis-entered merge must not hang the request.
    /// </summary>
    private const int MaxMergeHops = 5;

    private readonly JobAlignDbContext _context;
    private readonly ILogger<SkillResolver> _logger;

    public SkillResolver(JobAlignDbContext context, ILogger<SkillResolver> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Lowercase, then keep only letters and digits. <c>#</c> and <c>+</c> are expanded to
    /// words first, so "C#" does not collapse onto "C" and collide with the C language.
    /// </summary>
    /// <remarks>
    /// Every <c>MasterSkill.NormalizedName</c> and <c>SkillAlias.NormalizedAlias</c> row must
    /// be written using this exact method. If seeding normalizes differently from lookup,
    /// nothing resolves and the failure is silent — which is why MasterSkillSeeder calls this
    /// rather than keeping its own copy.
    /// </remarks>
    public string Normalize(string rawSkillText)
    {
        if (string.IsNullOrWhiteSpace(rawSkillText))
            return string.Empty;

        var expanded = rawSkillText
            .Replace("#", "sharp", StringComparison.Ordinal)
            .Replace("+", "plus", StringComparison.Ordinal);

        var builder = new StringBuilder(expanded.Length);

        foreach (var c in expanded)
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    public async Task<SkillResolution> ResolveAsync(
        string rawSkillText,
        CancellationToken cancellationToken = default)
    {
        var results = await ResolveManyAsync([rawSkillText], cancellationToken);
        return results[0];
    }

    public async Task<IReadOnlyList<SkillResolution>> ResolveManyAsync(
        IEnumerable<string> rawSkillTexts,
        CancellationToken cancellationToken = default)
    {
        var inputs = rawSkillTexts.ToList();

        if (inputs.Count == 0)
            return [];

        var normalized = inputs.Select(Normalize).ToList();
        var lookup = normalized.Where(n => n.Length > 0).Distinct().ToList();

        if (lookup.Count == 0)
            return inputs.Select(i => Unresolved(i)).ToList();

        // Two queries for the whole batch, not two per skill. Extraction resolves a dozen
        // names at a time and this sits on the request path.
        // Deliberately not filtered by IsActive. A skill merged into another is deactivated
        // but must still resolve — to its target — because existing postings reference it and
        // FR-58 keeps the row precisely so they stay explainable. A skill that is merely
        // deactivated is rejected below, after the merge chain has been followed.
        var byName = await _context.MasterSkills
            .AsNoTracking()
            .Where(s => lookup.Contains(s.NormalizedName))
            .ToDictionaryAsync(s => s.NormalizedName, cancellationToken);

        var byAlias = await _context.SkillAliases
            .AsNoTracking()
            .Include(a => a.MasterSkill)
            .Where(a => lookup.Contains(a.NormalizedAlias))
            .ToDictionaryAsync(a => a.NormalizedAlias, a => a.MasterSkill, cancellationToken);

        var results = new List<SkillResolution>(inputs.Count);

        for (var i = 0; i < inputs.Count; i++)
        {
            var key = normalized[i];

            // Canonical name first, then alias. An alias can only ever point at one skill —
            // NormalizedAlias is unique — so the order matters only when a name is both.
            var skill = key.Length == 0
                ? null
                : byName.GetValueOrDefault(key) ?? byAlias.GetValueOrDefault(key);

            skill = await FollowMergeChainAsync(skill, cancellationToken);

            // Unresolved is not an error. A posting may name a skill the master list does
            // not carry yet; the caller decides whether to skip it or raise it for an
            // administrator to add (FR-57). Never create one here (BR-04).
            //
            // An inactive skill at the end of the chain means an administrator withdrew it
            // and it was not merged into anything, so there is nothing to resolve to.
            if (skill is null || !skill.IsActive)
            {
                results.Add(Unresolved(inputs[i]));
                continue;
            }

            results.Add(new SkillResolution(inputs[i], skill.Id, skill.Name));
        }

        return results;
    }

    /// <summary>
    /// Where a skill was merged into another (FR-58), resolve to the survivor. The merged row
    /// is retained rather than deleted so existing postings still explain, so this hop is
    /// what keeps their skills resolving.
    /// </summary>
    private async Task<MasterSkill?> FollowMergeChainAsync(
        MasterSkill? skill,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<int>();

        for (var hop = 0; skill?.MergedIntoMasterSkillId is { } target; hop++)
        {
            if (hop >= MaxMergeHops || !seen.Add(skill.Id))
            {
                _logger.LogWarning(
                    "Merge chain from master skill {SkillId} is too long or cyclic; treating as unresolved.",
                    skill.Id);

                return null;
            }

            skill = await _context.MasterSkills
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == target, cancellationToken);
        }

        return skill;
    }

    private static SkillResolution Unresolved(string rawText) => new(rawText, null, null);
}
