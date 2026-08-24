using System.Security.Claims;
using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Identity;
using JobAlign.Core.Entities.Profiles;
using JobAlign.Core.Enums;
using JobAlign.Web.Models.Profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobAlign.Web.Controllers;

/// <summary>
/// Candidate-owned profile management (FR-27 to FR-29, FR-33, FR-34).
/// Every service call receives the authenticated user id so BR-09 is enforced below the UI.
/// </summary>
[Authorize(Roles = RoleNames.Candidate)]
public sealed class ProfileController : Controller
{
    private readonly ICandidateProfileService _profiles;
    private readonly IProfileEntryService _entries;

    public ProfileController(ICandidateProfileService profiles, IProfileEntryService entries)
    {
        _profiles = profiles;
        _entries = entries;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetAsync(CurrentUserId, cancellationToken);
        if (profile is null)
            return NotFound();

        return View(ToViewModel(profile));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDetails(ProfileDetailsViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var profile = await _profiles.GetAsync(CurrentUserId, cancellationToken);
            return profile is null ? NotFound() : View(nameof(Index), ToViewModel(profile, model));
        }

        await _profiles.UpdateDetailsAsync(
            CurrentUserId,
            new ProfileDetails(model.FullName, model.Headline, model.CurrentRole, model.PhoneNumber),
            cancellationToken);

        TempData["StatusMessage"] = "Profile details updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddSkill(AddSkillViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return RedirectToAction(nameof(Index));

        var resolution = await _profiles.AddSkillAsync(
            CurrentUserId, model.RawSkillText, model.ProficiencyLevel, cancellationToken);

        TempData["StatusMessage"] = resolution.IsResolved
            ? $"Added {resolution.CanonicalName} to your profile."
            : $"'{model.RawSkillText}' was not recognised. Nothing was added.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveSkill(int id, CancellationToken cancellationToken)
    {
        if (!await _profiles.RemoveSkillAsync(CurrentUserId, id, cancellationToken))
            return NotFound();

        TempData["StatusMessage"] = "Skill removed.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddEducation(AddEducationViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return RedirectToAction(nameof(Index));
        if (model.StartDate.HasValue && model.EndDate.HasValue && model.EndDate < model.StartDate)
        {
            TempData["StatusMessage"] = "Education end date cannot be before the start date.";
            return RedirectToAction(nameof(Index));
        }

        await _entries.AddEducationAsync(CurrentUserId, new EducationEntry
        {
            Institution = model.Institution.Trim(),
            Qualification = Clean(model.Qualification),
            FieldOfStudy = Clean(model.FieldOfStudy),
            Grade = Clean(model.Grade),
            StartDate = model.StartDate,
            EndDate = model.EndDate
        }, cancellationToken);

        TempData["StatusMessage"] = "Education added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveEducation(int id, CancellationToken cancellationToken)
    {
        if (!await _entries.RemoveEducationAsync(CurrentUserId, id, cancellationToken)) return NotFound();
        TempData["StatusMessage"] = "Education removed.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddExperience(AddExperienceViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return RedirectToAction(nameof(Index));
        if (model.StartDate.HasValue && model.EndDate.HasValue && model.EndDate < model.StartDate)
        {
            TempData["StatusMessage"] = "Experience end date cannot be before the start date.";
            return RedirectToAction(nameof(Index));
        }

        await _entries.AddWorkExperienceAsync(CurrentUserId, new WorkExperienceEntry
        {
            CompanyName = model.CompanyName.Trim(),
            JobTitle = model.JobTitle.Trim(),
            Description = Clean(model.Description),
            StartDate = model.StartDate,
            EndDate = model.IsCurrent ? null : model.EndDate,
            IsCurrent = model.IsCurrent
        }, cancellationToken);

        TempData["StatusMessage"] = "Work experience added and total experience recalculated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveExperience(int id, CancellationToken cancellationToken)
    {
        if (!await _entries.RemoveWorkExperienceAsync(CurrentUserId, id, cancellationToken)) return NotFound();
        TempData["StatusMessage"] = "Work experience removed and total experience recalculated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddProject(AddProjectViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return RedirectToAction(nameof(Index));
        if (model.StartDate.HasValue && model.EndDate.HasValue && model.EndDate < model.StartDate)
        {
            TempData["StatusMessage"] = "Project end date cannot be before the start date.";
            return RedirectToAction(nameof(Index));
        }

        await _entries.AddProjectAsync(CurrentUserId, new ProjectEntry
        {
            Name = model.Name.Trim(),
            Description = Clean(model.Description),
            Url = Clean(model.Url),
            StartDate = model.StartDate,
            EndDate = model.EndDate
        }, cancellationToken);

        TempData["StatusMessage"] = "Project added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveProject(int id, CancellationToken cancellationToken)
    {
        if (!await _entries.RemoveProjectAsync(CurrentUserId, id, cancellationToken)) return NotFound();
        TempData["StatusMessage"] = "Project removed.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCertification(AddCertificationViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return RedirectToAction(nameof(Index));
        if (model.IssuedOn.HasValue && model.ExpiresOn.HasValue && model.ExpiresOn < model.IssuedOn)
        {
            TempData["StatusMessage"] = "Certification expiry cannot be before the issue date.";
            return RedirectToAction(nameof(Index));
        }

        await _entries.AddCertificationAsync(CurrentUserId, new CertificationEntry
        {
            Name = model.Name.Trim(),
            IssuingOrganization = Clean(model.IssuingOrganization),
            CredentialId = Clean(model.CredentialId),
            IssuedOn = model.IssuedOn,
            ExpiresOn = model.ExpiresOn
        }, cancellationToken);

        TempData["StatusMessage"] = "Certification added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveCertification(int id, CancellationToken cancellationToken)
    {
        if (!await _entries.RemoveCertificationAsync(CurrentUserId, id, cancellationToken)) return NotFound();
        TempData["StatusMessage"] = "Certification removed.";
        return RedirectToAction(nameof(Index));
    }

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? throw new InvalidOperationException("Authenticated user has no identifier claim."));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ProfileViewModel ToViewModel(CandidateProfile profile, ProfileDetailsViewModel? detailsOverride = null) => new()
    {
        Id = profile.Id,
        Details = detailsOverride ?? new ProfileDetailsViewModel
        {
            FullName = profile.FullName,
            Headline = profile.Headline,
            CurrentRole = profile.CurrentRole,
            PhoneNumber = profile.PhoneNumber
        },
        TotalExperienceYears = profile.TotalExperienceYears,
        Education = profile.Education
            .OrderByDescending(e => e.EndDate ?? DateOnly.MaxValue)
            .Select(e => new EducationItemViewModel(e.Id, e.Institution, e.Qualification, e.FieldOfStudy, e.Grade, e.StartDate, e.EndDate))
            .ToList(),
        WorkExperience = profile.WorkExperience
            .OrderByDescending(e => e.StartDate)
            .Select(e => new ExperienceItemViewModel(e.Id, e.CompanyName, e.JobTitle, e.Description, e.StartDate, e.EndDate, e.IsCurrent))
            .ToList(),
        Projects = profile.Projects
            .OrderByDescending(e => e.StartDate)
            .Select(e => new ProjectItemViewModel(e.Id, e.Name, e.Description, e.Url, e.StartDate, e.EndDate))
            .ToList(),
        Certifications = profile.Certifications
            .OrderByDescending(e => e.IssuedOn)
            .Select(e => new CertificationItemViewModel(e.Id, e.Name, e.IssuingOrganization, e.CredentialId, e.IssuedOn, e.ExpiresOn))
            .ToList(),
        Skills = profile.Skills
            .OrderByDescending(s => s.ProficiencyLevel)
            .ThenBy(s => s.MasterSkill.Name)
            .Select(s => new ProfileSkillItemViewModel(s.Id, s.MasterSkill.Name, s.ProficiencyLevel, s.Source, s.ConfirmedAt))
            .ToList()
    };
}
