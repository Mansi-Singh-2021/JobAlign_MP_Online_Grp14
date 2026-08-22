using JobAlign.Core.Entities.Identity;
using JobAlign.Core.Entities.Matching;
using JobAlign.Core.Entities.Skills;

namespace JobAlign.Core.Entities.Profiles;

/// <summary>
/// A candidate's profile: personal details, education, work experience,
/// projects and certifications (FR-27). Visible only to its owner (BR-09) —
/// administrators manage accounts but may not read profiles.
/// </summary>
public class CandidateProfile
{
    public int Id { get; set; }

    /// <summary>One profile per user. Also the ownership key for BR-09.</summary>
    public int UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public string? FullName { get; set; }
    public string? Headline { get; set; }
    public string? CurrentRole { get; set; }
    public string? PhoneNumber { get; set; }

    /// <summary>Where the candidate is based, normalized like posting locations (FR-16).</summary>
    public int? LocationId { get; set; }
    public Location? Location { get; set; }

    /// <summary>
    /// Total years of experience used by the experience score (FR-33, FR-38).
    /// Derived from <see cref="WorkExperience"/> and stored so scoring does not
    /// recompute it per posting. Nullable: a candidate with nothing recorded has
    /// no total, which is not the same as zero years (BR-02).
    /// </summary>
    public decimal? TotalExperienceYears { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<EducationEntry> Education { get; set; } = new List<EducationEntry>();
    public ICollection<WorkExperienceEntry> WorkExperience { get; set; } = new List<WorkExperienceEntry>();
    public ICollection<ProjectEntry> Projects { get; set; } = new List<ProjectEntry>();
    public ICollection<CertificationEntry> Certifications { get; set; } = new List<CertificationEntry>();

    /// <summary>Confirmed skills only — these are what scoring uses (BR-06).</summary>
    public ICollection<ProfileSkill> Skills { get; set; } = new List<ProfileSkill>();

    public ICollection<Resume> Resumes { get; set; } = new List<Resume>();

    public ICollection<RoadmapItem> RoadmapItems { get; set; } = new List<RoadmapItem>();
}

/// <summary>A qualification held by the candidate (FR-27).</summary>
public class EducationEntry
{
    public int Id { get; set; }

    public int CandidateProfileId { get; set; }
    public CandidateProfile CandidateProfile { get; set; } = null!;

    public required string Institution { get; set; }
    public string? Qualification { get; set; }
    public string? FieldOfStudy { get; set; }
    public string? Grade { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}

/// <summary>
/// A role the candidate has held (FR-27). Feeds
/// <see cref="CandidateProfile.TotalExperienceYears"/> (FR-33).
/// </summary>
public class WorkExperienceEntry
{
    public int Id { get; set; }

    public int CandidateProfileId { get; set; }
    public CandidateProfile CandidateProfile { get; set; } = null!;

    public required string CompanyName { get; set; }
    public required string JobTitle { get; set; }
    public string? Description { get; set; }

    public DateOnly? StartDate { get; set; }

    /// <summary>Null where <see cref="IsCurrent"/> — the role has not ended.</summary>
    public DateOnly? EndDate { get; set; }

    public bool IsCurrent { get; set; }
}

/// <summary>A project the candidate wants counted (FR-27).</summary>
public class ProjectEntry
{
    public int Id { get; set; }

    public int CandidateProfileId { get; set; }
    public CandidateProfile CandidateProfile { get; set; } = null!;

    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}

/// <summary>A certification held by the candidate (FR-27).</summary>
public class CertificationEntry
{
    public int Id { get; set; }

    public int CandidateProfileId { get; set; }
    public CandidateProfile CandidateProfile { get; set; } = null!;

    public required string Name { get; set; }
    public string? IssuingOrganization { get; set; }
    public string? CredentialId { get; set; }

    public DateOnly? IssuedOn { get; set; }

    /// <summary>Null where the certification does not expire.</summary>
    public DateOnly? ExpiresOn { get; set; }
}
