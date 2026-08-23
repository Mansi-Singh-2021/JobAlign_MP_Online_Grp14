using JobAlign.Core.Entities.Skills;

namespace JobAlign.Core.Abstractions;

/// <summary>
/// Administrator maintenance of the master skill list and its aliases (FR-57, FR-58).
/// </summary>
/// <remarks>
/// Master data is **deactivated, never deleted**. Postings and profiles hold foreign keys to
/// these rows and the relationships are `Restrict`, so a delete would either throw or destroy
/// the ability to explain an existing match. Every "remove" here is a deactivation or a merge.
///
/// No method takes an owner id, unlike the candidate-facing services: this is system-wide
/// master data. Section 4.3 gives administrators master-data management and explicitly denies
/// them candidate postings and resumes, so nothing here reads either (BR-09).
/// </remarks>
public interface ISkillAdminService
{
    /// <summary>Master skills, optionally filtered by a search term, alphabetically.</summary>
    Task<IReadOnlyList<MasterSkill>> ListAsync(
        string? search, bool includeInactive, CancellationToken cancellationToken = default);

    Task<MasterSkill?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a skill (FR-57). Fails when the name normalizes onto an existing skill or an
    /// existing alias — either would make resolution ambiguous, and BR-04 requires a skill
    /// name to resolve exactly one way.
    /// </summary>
    Task<SkillAdminResult> CreateAsync(
        string name, string? category, CancellationToken cancellationToken = default);

    Task<SkillAdminResult> UpdateAsync(
        int id, string name, string? category, CancellationToken cancellationToken = default);

    /// <summary>Deactivates or reactivates a skill (FR-57). Deactivated skills stop resolving.</summary>
    Task<SkillAdminResult> SetActiveAsync(
        int id, bool isActive, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SkillAlias>> ListAliasesAsync(
        int masterSkillId, CancellationToken cancellationToken = default);

    /// <summary>Adds an alias (FR-58). Fails when it already resolves to something.</summary>
    Task<SkillAdminResult> AddAliasAsync(
        int masterSkillId, string alias, CancellationToken cancellationToken = default);

    Task<SkillAdminResult> RemoveAliasAsync(int aliasId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges <paramref name="sourceId"/> into <paramref name="targetId"/> (FR-58).
    ///
    /// The source row is kept, marked as merged and deactivated, so existing data that
    /// references it can still be explained. Its aliases move to the target, and posting and
    /// profile skills are repointed — without that last step the two skills would still
    /// compare as different during scoring, and the merge would not really have happened.
    /// </summary>
    Task<SkillAdminResult> MergeAsync(
        int sourceId, int targetId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of an administrative change. A failure here is an expected, explainable condition
/// — a duplicate name, a merge into itself — not an exception.
/// </summary>
public sealed record SkillAdminResult(bool Succeeded, string? Error)
{
    public static SkillAdminResult Ok() => new(true, null);
    public static SkillAdminResult Fail(string error) => new(false, error);
}
