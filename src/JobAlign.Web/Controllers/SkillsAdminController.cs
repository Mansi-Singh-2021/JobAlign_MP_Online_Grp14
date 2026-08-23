using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Identity;
using JobAlign.Core.Entities.Skills;
using JobAlign.Web.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobAlign.Web.Controllers;

/// <summary>
/// Administrator maintenance of the master skill list and its aliases (FR-57, FR-58).
/// </summary>
/// <remarks>
/// Administrator-only. Section 4.3 of the SRS grants administrators master-data management
/// and explicitly denies them candidate postings, profiles and resumes, so nothing in this
/// controller reads any of those (BR-09). A candidate reaching here gets Access Denied.
///
/// Master data is deactivated or merged, never deleted — every screen below reflects that.
/// </remarks>
[Authorize(Roles = RoleNames.Administrator)]
public class SkillsAdminController : Controller
{
    private readonly ISkillAdminService _skills;
    private readonly ISkillResolver _resolver;

    public SkillsAdminController(ISkillAdminService skills, ISkillResolver resolver)
    {
        _skills = skills;
        _resolver = resolver;
    }

    /// <summary>The master skill list, searchable (FR-57).</summary>
    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var skills = await _skills.ListAsync(search, includeInactive, cancellationToken);
        var all = await _skills.ListAsync(null, true, cancellationToken);

        return View(new SkillListViewModel
        {
            Skills = skills.Select(ToRow).ToList(),
            Search = search,
            IncludeInactive = includeInactive,
            ActiveCount = all.Count(s => s.IsActive),
            TotalCount = all.Count
        });
    }

    // ---------------------------------------------------------------- FR-57

    [HttpGet]
    public IActionResult Create() => View("Edit", new EditSkillViewModel());

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var skill = await _skills.GetAsync(id, cancellationToken);

        if (skill is null)
            return NotFound();

        return View(new EditSkillViewModel
        {
            Id = skill.Id,
            Name = skill.Name,
            Category = skill.Category,
            IsActive = skill.IsActive,
            IsMerged = skill.MergedIntoMasterSkillId is not null,
            MergedInto = skill.MergedIntoMasterSkill?.Name,
            NormalizedPreview = skill.NormalizedName
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(EditSkillViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View("Edit", Repopulate(model));

        var result = model.IsNew
            ? await _skills.CreateAsync(model.Name, model.Category, cancellationToken)
            : await _skills.UpdateAsync(model.Id, model.Name, model.Category, cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View("Edit", Repopulate(model));
        }

        TempData["StatusMessage"] = model.IsNew ? $"Added {model.Name}." : $"Updated {model.Name}.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Deactivate or reactivate (FR-57). There is deliberately no delete.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool isActive, CancellationToken cancellationToken)
    {
        var result = await _skills.SetActiveAsync(id, isActive, cancellationToken);

        TempData["StatusMessage"] = result.Succeeded
            ? (isActive ? "Skill reactivated." : "Skill deactivated. Existing data that references it is untouched.")
            : result.Error;

        return RedirectToAction(nameof(Index), new { includeInactive = true });
    }

    // ---------------------------------------------------------------- FR-58 aliases

    [HttpGet]
    public async Task<IActionResult> Aliases(int id, CancellationToken cancellationToken)
    {
        var model = await BuildAliasesAsync(id, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAlias(int id, string? newAlias, CancellationToken cancellationToken)
    {
        var result = await _skills.AddAliasAsync(id, newAlias ?? string.Empty, cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);

            var model = await BuildAliasesAsync(id, cancellationToken);
            if (model is null) return NotFound();

            model.NewAlias = newAlias;
            return View("Aliases", model);
        }

        TempData["StatusMessage"] = $"Alias \"{newAlias}\" added.";
        return RedirectToAction(nameof(Aliases), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAlias(int id, int aliasId, CancellationToken cancellationToken)
    {
        var result = await _skills.RemoveAliasAsync(aliasId, cancellationToken);
        TempData["StatusMessage"] = result.Succeeded ? "Alias removed." : result.Error;

        return RedirectToAction(nameof(Aliases), new { id });
    }

    // ---------------------------------------------------------------- FR-58 merge

    [HttpGet]
    public async Task<IActionResult> Merge(int? id, CancellationToken cancellationToken)
    {
        return View(new MergeSkillsViewModel
        {
            SourceId = id ?? 0,
            Candidates = await MergeCandidatesAsync(cancellationToken)
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Merge(MergeSkillsViewModel model, CancellationToken cancellationToken)
    {
        var result = await _skills.MergeAsync(model.SourceId, model.TargetId, cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);

            return View(new MergeSkillsViewModel
            {
                SourceId = model.SourceId,
                TargetId = model.TargetId,
                Candidates = await MergeCandidatesAsync(cancellationToken)
            });
        }

        TempData["StatusMessage"] =
            "Skills merged. The merged skill is kept and deactivated so existing postings still explain, "
            + "and its aliases now resolve to the surviving skill.";

        return RedirectToAction(nameof(Index));
    }

    // ----------------------------------------------------------------

    private async Task<IReadOnlyList<SkillRowViewModel>> MergeCandidatesAsync(CancellationToken cancellationToken)
    {
        // Merged skills are excluded: merging into one would build a chain, and merging a
        // second time would contradict the first.
        var all = await _skills.ListAsync(null, includeInactive: false, cancellationToken);

        return all
            .Where(s => s.MergedIntoMasterSkillId is null)
            .Select(ToRow)
            .ToList();
    }

    private async Task<SkillAliasesViewModel?> BuildAliasesAsync(int id, CancellationToken cancellationToken)
    {
        var skill = await _skills.GetAsync(id, cancellationToken);

        if (skill is null)
            return null;

        var aliases = await _skills.ListAliasesAsync(id, cancellationToken);

        return new SkillAliasesViewModel
        {
            SkillId = skill.Id,
            SkillName = skill.Name,
            NormalizedName = skill.NormalizedName,
            Aliases = aliases
                .Select(a => new AliasRowViewModel
                {
                    Id = a.Id,
                    Alias = a.Alias,
                    NormalizedAlias = a.NormalizedAlias
                })
                .ToList()
        };
    }

    /// <summary>Refills the derived preview when redisplaying a form after a failure.</summary>
    private EditSkillViewModel Repopulate(EditSkillViewModel model)
    {
        model.NormalizedPreview = _resolver.Normalize(model.Name ?? string.Empty);
        return model;
    }

    private static SkillRowViewModel ToRow(MasterSkill skill) => new()
    {
        Id = skill.Id,
        Name = skill.Name,
        NormalizedName = skill.NormalizedName,
        Category = skill.Category,
        IsActive = skill.IsActive,
        AliasCount = skill.Aliases.Count,
        MergedInto = skill.MergedIntoMasterSkill?.Name
    };
}
