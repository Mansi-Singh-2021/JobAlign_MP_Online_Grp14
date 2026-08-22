using System.Text;
using JobAlign.Core.Abstractions;
using JobAlign.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace JobAlign.Infrastructure.Services;

/// <summary>
/// TEMPORARY — Member B replaces this with <c>SkillResolver</c> in Wave 0. Delete this file
/// then, and change the one registration line in <c>DependencyInjection</c>.
/// </summary>
/// <remarks>
/// Exists only so <see cref="ExtractionService"/> has something to resolve against while
/// Role B is unbuilt. It does a real lookup, so it behaves correctly — but **nothing seeds
/// the master skill list yet**, so in practice every skill comes back unresolved and
/// extraction skips them. That is the correct behaviour, not a bug: BR-04 forbids inventing
/// a master skill from extracted text. Skills start appearing the moment B's seeder lands.
///
/// Deliberately missing, and B's job to add: the C#/C++ punctuation special cases, merge
/// chains beyond one hop, cycle guarding, and a single-round-trip batch query.
/// </remarks>
public class PlaceholderSkillResolver : ISkillResolver
{
    private readonly JobAlignDbContext _db;

    public PlaceholderSkillResolver(JobAlignDbContext db) => _db = db;

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
        var normalized = inputs.Select(Normalize).ToList();
        var lookup = normalized.Where(n => n.Length > 0).Distinct().ToList();

        if (lookup.Count == 0)
            return inputs.Select(i => new SkillResolution(i, null, null)).ToList();

        var byName = await _db.MasterSkills
            .AsNoTracking()
            .Where(s => s.IsActive && lookup.Contains(s.NormalizedName))
            .ToDictionaryAsync(s => s.NormalizedName, cancellationToken);

        var byAlias = await _db.SkillAliases
            .AsNoTracking()
            .Include(a => a.MasterSkill)
            .Where(a => lookup.Contains(a.NormalizedAlias))
            .ToDictionaryAsync(a => a.NormalizedAlias, a => a.MasterSkill, cancellationToken);

        var results = new List<SkillResolution>(inputs.Count);

        for (var i = 0; i < inputs.Count; i++)
        {
            var key = normalized[i];

            var skill = byName.GetValueOrDefault(key) ?? byAlias.GetValueOrDefault(key);

            // One hop only. A longer merge chain is B's problem, and following it here
            // would duplicate work B is about to do properly.
            if (skill?.MergedIntoMasterSkillId is { } mergedInto)
            {
                skill = await _db.MasterSkills
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == mergedInto, cancellationToken);
            }

            results.Add(skill is null
                ? new SkillResolution(inputs[i], null, null)
                : new SkillResolution(inputs[i], skill.Id, skill.Name));
        }

        return results;
    }

    /// <summary>
    /// Lowercase, then keep only letters and digits. <c>#</c> and <c>+</c> become words
    /// first so "C#" does not collapse onto "C".
    /// </summary>
    public string Normalize(string rawSkillText)
    {
        if (string.IsNullOrWhiteSpace(rawSkillText))
            return string.Empty;

        var expanded = rawSkillText
            .Replace("#", "sharp", StringComparison.Ordinal)
            .Replace("+", "plus", StringComparison.Ordinal)
            .ToLowerInvariant();

        var builder = new StringBuilder(expanded.Length);

        foreach (var c in expanded)
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(c);
        }

        return builder.ToString();
    }
}
