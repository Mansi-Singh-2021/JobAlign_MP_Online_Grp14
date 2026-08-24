using JobAlign.Core.Entities.Profiles;
using JobAlign.Core.Enums;

namespace JobAlign.Core.Abstractions;

/// <summary>
/// The signed-in candidate's own profile (FR-27, FR-28, FR-33, FR-34).
/// User id on every method — a profile is visible only to its owner (BR-09).
/// A profile row is created at registration, so GetAsync never returns null for a candidate.
/// </summary>
public interface ICandidateProfileService
{
    Task<CandidateProfile?> GetAsync(int userId, CancellationToken cancellationToken = default);

    Task UpdateDetailsAsync(int userId, ProfileDetails details, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a skill, resolving it through ISkillResolver first (FR-28, FR-29, BR-04).
    /// Returns the resolution so the caller can tell the candidate their skill was
    /// not recognised. Adding a skill already held updates its proficiency.
    /// </summary>
    Task<SkillResolution> AddSkillAsync(
        int userId, string rawSkillText, ProficiencyLevel level,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveSkillAsync(int userId, int profileSkillId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes CandidateProfile.TotalExperienceYears from the work-experience entries
    /// (FR-33). Null when nothing is recorded — which is not the same as zero years (BR-02).
    /// Call after any change to work experience.
    /// </summary>
    Task RecalculateTotalExperienceAsync(int userId, CancellationToken cancellationToken = default);
}

/// <summary>Editable profile header fields (FR-27).</summary>
public sealed record ProfileDetails(
    string? FullName,
    string? Headline,
    string? CurrentRole,
    string? PhoneNumber);
