using System.Security.Claims;
using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Identity;
using JobAlign.Core.Entities.Postings;
using JobAlign.Core.Enums;
using JobAlign.Core.Extraction;
using JobAlign.Infrastructure.Data;
using JobAlign.Web.Models.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobAlign.Web.Controllers;

/// <summary>
/// Candidate overview dashboard (FR-52, FR-54, BR-08).
/// </summary>
[Authorize(Roles = RoleNames.Candidate)]
public class DashboardController : Controller
{
    private readonly JobAlignDbContext _db;
    private readonly ISkillGapService _skillGapService;

    public DashboardController(JobAlignDbContext db, ISkillGapService skillGapService)
    {
        _db = db;
        _skillGapService = skillGapService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var allPostings = await _db.JobPostings
            .AsNoTracking()
            .Where(p => p.OwnerUserId == CurrentUserId)
            .Include(p => p.Extractions.Where(e => e.IsCurrent))
            .Include(p => p.Corrections)
            .Include(p => p.MatchResult)
            .OrderByDescending(p => p.CapturedAt)
            .ToListAsync(cancellationToken);

        var totalCount = allPostings.Count;
        var pendingCount = allPostings.Count(p => p.Status == PostingStatus.Pending);

        // BR-08 / FR-54: Pending postings are strictly excluded from comparison and dashboard metrics
        var nonPendingPostings = allPostings.Where(p => p.Status != PostingStatus.Pending).ToList();

        var scoredPostings = nonPendingPostings
            .Where(p => p.Status == PostingStatus.Confirmed && p.MatchResult?.OverallScore != null)
            .ToList();

        decimal? avgScore = scoredPostings.Count > 0
            ? Math.Round(scoredPostings.Average(p => p.MatchResult!.OverallScore!.Value), 1, MidpointRounding.AwayFromZero)
            : null;

        decimal? bestScore = scoredPostings.Count > 0
            ? scoredPostings.Max(p => p.MatchResult!.OverallScore!.Value)
            : null;

        var roadmap = await _skillGapService.GetRoadmapAsync(CurrentUserId, cancellationToken);
        var topRoadmap = roadmap.Take(5).Select(r => new RoadmapItemViewModel
        {
            Id = r.Id,
            MasterSkillId = r.MasterSkillId,
            SkillName = r.MasterSkill?.Name ?? "Skill",
            Priority = r.Priority,
            RequiredOccurrenceCount = r.RequiredOccurrenceCount,
            PreferredOccurrenceCount = r.PreferredOccurrenceCount,
            Status = r.Status,
            CompletedAt = r.CompletedAt
        }).ToList();

        var recentPostings = nonPendingPostings.Take(5).Select(p =>
        {
            var extraction = p.Extractions.FirstOrDefault();
            var titleCorrection = p.Corrections.FirstOrDefault(c => c.FieldName == CorrectableFields.JobTitle)?.CorrectedValue;
            var companyCorrection = p.Corrections.FirstOrDefault(c => c.FieldName == CorrectableFields.CompanyName)?.CorrectedValue;

            return new DashboardPostingItemViewModel
            {
                Id = p.Id,
                Reference = p.Reference,
                Preview = BuildPreview(p.RawText),
                JobTitle = titleCorrection ?? extraction?.JobTitle,
                CompanyName = companyCorrection ?? extraction?.CompanyName,
                CapturedAt = p.CapturedAt,
                Status = p.Status,
                ApplicationStatus = p.ApplicationStatus,
                OverallScore = p.MatchResult?.OverallScore
            };
        }).ToList();

        var model = new DashboardViewModel
        {
            TotalPostings = totalCount,
            PendingCount = pendingCount,
            SavedCount = nonPendingPostings.Count(p => p.ApplicationStatus == ApplicationStatus.Saved),
            AppliedCount = nonPendingPostings.Count(p => p.ApplicationStatus == ApplicationStatus.Applied),
            InterviewCount = nonPendingPostings.Count(p => p.ApplicationStatus == ApplicationStatus.Interview),
            RejectedCount = nonPendingPostings.Count(p => p.ApplicationStatus == ApplicationStatus.Rejected),
            ClosedCount = nonPendingPostings.Count(p => p.ApplicationStatus == ApplicationStatus.Closed),
            AverageMatchScore = avgScore,
            BestMatchScore = bestScore,
            TopRoadmapSkills = topRoadmap,
            RecentPostings = recentPostings
        };

        return View(model);
    }

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? throw new InvalidOperationException("Authenticated user has no identifier claim."));

    private static string BuildPreview(string rawText)
    {
        var firstLine = rawText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        return firstLine.Length <= 90 ? firstLine : firstLine[..90] + "…";
    }
}
