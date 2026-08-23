namespace JobAlign.Core.Abstractions;

/// <summary>
/// Resolves a free-text skill name to exactly one master skill (FR-14, FR-29, BR-04).
/// "C#", "C Sharp" and "C-Sharp" must all return the same MasterSkillId.
///
/// Used identically by posting skills, profile skills and resume skills — the rule
/// is the same everywhere, so there is one implementation.
/// </summary>
public interface ISkillResolver
{
    Task<SkillResolution> ResolveAsync(string rawSkillText, CancellationToken cancellationToken = default);

    /// <summary>Batch form. One database round trip, not N — extraction resolves a dozen at a time.</summary>
    Task<IReadOnlyList<SkillResolution>> ResolveManyAsync(
        IEnumerable<string> rawSkillTexts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The lookup form of a name: lowercased, punctuation and whitespace stripped.
    /// "C#" -> "csharp", "ASP .NET Core" -> "aspnetcore". Public because MasterSkill
    /// and SkillAlias rows must be written with exactly this normalization applied.
    /// </summary>
    string Normalize(string rawSkillText);
}

/// <summary>
/// Outcome of resolving one skill name. Unresolved is a normal result, not an error:
/// a posting may name a skill the master list does not carry yet. The caller decides
/// whether to skip it or raise it for an administrator (FR-57).
/// </summary>
/// <param name="RawText">Exactly what was supplied, kept as provenance (BR-04).</param>
/// <param name="MasterSkillId">Null when unresolved.</param>
/// <param name="CanonicalName">The approved name, e.g. "C#". Null when unresolved.</param>
public sealed record SkillResolution(string RawText, int? MasterSkillId, string? CanonicalName)
{
    public bool IsResolved => MasterSkillId.HasValue;
}
