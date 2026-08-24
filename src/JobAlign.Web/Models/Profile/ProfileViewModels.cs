using System.ComponentModel.DataAnnotations;
using JobAlign.Core.Enums;

namespace JobAlign.Web.Models.Profile;

public sealed class ProfileViewModel
{
    public int Id { get; init; }
    public ProfileDetailsViewModel Details { get; set; } = new();
    public decimal? TotalExperienceYears { get; init; }
    public IReadOnlyList<EducationItemViewModel> Education { get; init; } = [];
    public IReadOnlyList<ExperienceItemViewModel> WorkExperience { get; init; } = [];
    public IReadOnlyList<ProjectItemViewModel> Projects { get; init; } = [];
    public IReadOnlyList<CertificationItemViewModel> Certifications { get; init; } = [];
    public IReadOnlyList<ProfileSkillItemViewModel> Skills { get; init; } = [];
}

public sealed class ProfileDetailsViewModel
{
    [StringLength(160)]
    public string? FullName { get; set; }

    [StringLength(160)]
    public string? Headline { get; set; }

    [StringLength(160)]
    public string? CurrentRole { get; set; }

    [Phone]
    [StringLength(32)]
    public string? PhoneNumber { get; set; }
}

public sealed class AddSkillViewModel
{
    [Required]
    [StringLength(100)]
    [Display(Name = "Skill")]
    public string RawSkillText { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Proficiency")]
    public ProficiencyLevel ProficiencyLevel { get; set; }
}

public sealed class AddEducationViewModel
{
    [Required, StringLength(200)]
    public string Institution { get; set; } = string.Empty;
    [StringLength(160)] public string? Qualification { get; set; }
    [StringLength(160)] public string? FieldOfStudy { get; set; }
    [StringLength(80)] public string? Grade { get; set; }
    [DataType(DataType.Date)] public DateOnly? StartDate { get; set; }
    [DataType(DataType.Date)] public DateOnly? EndDate { get; set; }
}

public sealed class AddExperienceViewModel
{
    [Required, StringLength(200)] public string CompanyName { get; set; } = string.Empty;
    [Required, StringLength(160)] public string JobTitle { get; set; } = string.Empty;
    [StringLength(2000)] public string? Description { get; set; }
    [DataType(DataType.Date)] public DateOnly? StartDate { get; set; }
    [DataType(DataType.Date)] public DateOnly? EndDate { get; set; }
    public bool IsCurrent { get; set; }
}

public sealed class AddProjectViewModel
{
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [StringLength(3000)] public string? Description { get; set; }
    [Url, StringLength(500)] public string? Url { get; set; }
    [DataType(DataType.Date)] public DateOnly? StartDate { get; set; }
    [DataType(DataType.Date)] public DateOnly? EndDate { get; set; }
}

public sealed class AddCertificationViewModel
{
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [StringLength(200)] public string? IssuingOrganization { get; set; }
    [StringLength(160)] public string? CredentialId { get; set; }
    [DataType(DataType.Date)] public DateOnly? IssuedOn { get; set; }
    [DataType(DataType.Date)] public DateOnly? ExpiresOn { get; set; }
}

public sealed record EducationItemViewModel(
    int Id, string Institution, string? Qualification, string? FieldOfStudy, string? Grade,
    DateOnly? StartDate, DateOnly? EndDate);

public sealed record ExperienceItemViewModel(
    int Id, string CompanyName, string JobTitle, string? Description,
    DateOnly? StartDate, DateOnly? EndDate, bool IsCurrent);

public sealed record ProjectItemViewModel(
    int Id, string Name, string? Description, string? Url, DateOnly? StartDate, DateOnly? EndDate);

public sealed record CertificationItemViewModel(
    int Id, string Name, string? IssuingOrganization, string? CredentialId,
    DateOnly? IssuedOn, DateOnly? ExpiresOn);

public sealed record ProfileSkillItemViewModel(
    int Id, string CanonicalName, ProficiencyLevel ProficiencyLevel, ProfileSkillSource Source, DateTimeOffset ConfirmedAt);
