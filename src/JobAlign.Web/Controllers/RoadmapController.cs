using System.Security.Claims;
using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Identity;
using JobAlign.Core.Enums;
using JobAlign.Infrastructure.Data;
using JobAlign.Web.Models.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobAlign.Web.Controllers;

/// <summary>
/// Learning roadmap ordering missing skills across confirmed postings (FR-45, FR-46, FR-47).
/// </summary>
[Authorize(Roles = RoleNames.Candidate)]
public class RoadmapController : Controller
{
    private readonly ISkillGapService _skillGapService;
    private readonly ICandidateProfileService _profileService;
    private readonly JobAlignDbContext _db;

    public RoadmapController(
        ISkillGapService skillGapService,
        ICandidateProfileService profileService,
        JobAlignDbContext db)
    {
        _skillGapService = skillGapService;
        _profileService = profileService;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var items = await _skillGapService.GetRoadmapAsync(CurrentUserId, cancellationToken);

        // If roadmap is empty, check if candidate has confirmed postings with gaps and auto-rebuild
        if (items.Count == 0)
        {
            var hasGaps = await _db.SkillGaps
                .AnyAsync(g => g.MatchResult.JobPosting.OwnerUserId == CurrentUserId
                               && g.MatchResult.JobPosting.Status == PostingStatus.Confirmed,
                    cancellationToken);

            if (hasGaps)
            {
                items = await _skillGapService.RebuildRoadmapAsync(CurrentUserId, cancellationToken);
            }
        }

        var profile = await _profileService.GetAsync(CurrentUserId, cancellationToken);
        var heldMasterSkillIds = profile?.Skills.Select(s => s.MasterSkillId).ToHashSet() ?? [];

        var viewModel = new RoadmapViewModel
        {
            Items = items.Select(r => new RoadmapItemViewModel
            {
                Id = r.Id,
                MasterSkillId = r.MasterSkillId,
                SkillName = r.MasterSkill?.Name ?? "Skill",
                Priority = r.Priority,
                RequiredOccurrenceCount = r.RequiredOccurrenceCount,
                PreferredOccurrenceCount = r.PreferredOccurrenceCount,
                Status = r.Status,
                CompletedAt = r.CompletedAt,
                IsHeldInProfile = heldMasterSkillIds.Contains(r.MasterSkillId)
            }).ToList()
        };

        return View(viewModel);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Rebuild(CancellationToken cancellationToken)
    {
        await _skillGapService.RebuildRoadmapAsync(CurrentUserId, cancellationToken);
        TempData["StatusMessage"] = "Roadmap recalculated from your confirmed postings.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, RoadmapItemStatus status, CancellationToken cancellationToken)
    {
        var success = await _skillGapService.SetRoadmapStatusAsync(id, CurrentUserId, status, cancellationToken);
        if (!success)
            return NotFound();

        TempData["StatusMessage"] = $"Roadmap skill status updated to {status}.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Adds a learned skill into CandidateProfile (FR-47, BR-06).
    /// Adding a skill recalculates match scores; completing a roadmap item alone does not.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToProfile(
        int id,
        ProficiencyLevel level = ProficiencyLevel.Intermediate,
        CancellationToken cancellationToken = default)
    {
        var item = await _db.RoadmapItems
            .Include(r => r.MasterSkill)
            .Include(r => r.CandidateProfile)
            .FirstOrDefaultAsync(r => r.Id == id && r.CandidateProfile.UserId == CurrentUserId, cancellationToken);

        if (item is null)
            return NotFound();

        // 1. Add skill to profile (which invokes match rescoring)
        var resolution = await _profileService.AddSkillAsync(
            CurrentUserId, item.MasterSkill.Name, level, cancellationToken);

        // 2. Mark roadmap item completed
        await _skillGapService.SetRoadmapStatusAsync(id, CurrentUserId, RoadmapItemStatus.Completed, cancellationToken);

        TempData["StatusMessage"] = $"Added {item.MasterSkill.Name} to your profile! Match scores have been updated.";
        return RedirectToAction(nameof(Index));
    }

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? throw new InvalidOperationException("Authenticated user has no identifier claim."));
}
