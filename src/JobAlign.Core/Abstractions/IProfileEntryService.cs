using JobAlign.Core.Entities.Profiles;

namespace JobAlign.Core.Abstractions;

/// <summary>
/// Candidate-owned CRUD for profile collection entries (FR-27).
/// This is an internal Role C seam; the shared ICandidateProfileService contract remains unchanged.
/// </summary>
public interface IProfileEntryService
{
    Task<EducationEntry?> AddEducationAsync(int userId, EducationEntry entry, CancellationToken cancellationToken = default);
    Task<bool> RemoveEducationAsync(int userId, int entryId, CancellationToken cancellationToken = default);

    Task<WorkExperienceEntry?> AddWorkExperienceAsync(int userId, WorkExperienceEntry entry, CancellationToken cancellationToken = default);
    Task<bool> RemoveWorkExperienceAsync(int userId, int entryId, CancellationToken cancellationToken = default);

    Task<ProjectEntry?> AddProjectAsync(int userId, ProjectEntry entry, CancellationToken cancellationToken = default);
    Task<bool> RemoveProjectAsync(int userId, int entryId, CancellationToken cancellationToken = default);

    Task<CertificationEntry?> AddCertificationAsync(int userId, CertificationEntry entry, CancellationToken cancellationToken = default);
    Task<bool> RemoveCertificationAsync(int userId, int entryId, CancellationToken cancellationToken = default);
}
