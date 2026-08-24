using System.Security.Claims;
using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Identity;
using JobAlign.Core.Entities.Postings;
using JobAlign.Core.Enums;
using JobAlign.Core.Extraction;
using JobAlign.Infrastructure.Data;
using JobAlign.Web.Models.Dashboard;
using JobAlign.Web.Models.Postings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobAlign.Web.Controllers;

/// <summary>
/// Capture and management of the signed-in candidate's postings (FR-06, FR-08 to FR-11, FR-50, FR-51, FR-53).
/// </summary>
[Authorize(Roles = RoleNames.Candidate)]
public class PostingsController : Controller
{
    private readonly IJobPostingService _postings;
    private readonly IExtractionService _extraction;
    private readonly ICandidateProfileService _profileService;
    private readonly JobAlignDbContext _db;
    private readonly ILogger<PostingsController> _logger;

    public PostingsController(
        IJobPostingService postings,
        IExtractionService extraction,
        ICandidateProfileService profileService,
        JobAlignDbContext db,
        ILogger<PostingsController> logger)
    {
        _postings = postings;
        _extraction = extraction;
        _profileService = profileService;
        _db = db;
        _logger = logger;
    }

    /// <summary>The saved-postings list with filtering and sorting (FR-11, FR-50, FR-51).</summary>
    [HttpGet]
    public async Task<IActionResult> Index(
        bool includeArchived = false,
        RemotePolicy? workMode = null,
        string? location = null,
        decimal? minExp = null,
        decimal? maxExp = null,
        string sortBy = "date",
        string sortOrder = "desc",
        CancellationToken cancellationToken = default)
    {
        var query = _db.JobPostings
            .AsNoTracking()
            .Where(p => p.OwnerUserId == CurrentUserId); // BR-09 server-side filter

        if (!includeArchived)
            query = query.Where(p => !p.IsArchived);

        var rawPostings = await query
            .Include(p => p.Extractions.Where(e => e.IsCurrent))
            .Include(p => p.Corrections)
            .Include(p => p.MatchResult)
            .ToListAsync(cancellationToken);

        var items = rawPostings.Select(p =>
        {
            var extraction = p.Extractions.FirstOrDefault();
            var titleCorr = p.Corrections.FirstOrDefault(c => c.FieldName == CorrectableFields.JobTitle)?.CorrectedValue;
            var compCorr = p.Corrections.FirstOrDefault(c => c.FieldName == CorrectableFields.CompanyName)?.CorrectedValue;
            var locCorr = p.Corrections.FirstOrDefault(c => c.FieldName == CorrectableFields.RawLocationText)?.CorrectedValue;
            var expMinCorr = p.Corrections.FirstOrDefault(c => c.FieldName == CorrectableFields.ExperienceMinYears)?.CorrectedValue;
            var expMaxCorr = p.Corrections.FirstOrDefault(c => c.FieldName == CorrectableFields.ExperienceMaxYears)?.CorrectedValue;

            decimal? minYears = decimal.TryParse(expMinCorr, out var d1) ? d1 : extraction?.ExperienceMinYears;
            decimal? maxYears = decimal.TryParse(expMaxCorr, out var d2) ? d2 : extraction?.ExperienceMaxYears;

            return new PostingListItemViewModel
            {
                Id = p.Id,
                Reference = p.Reference,
                CapturedAt = p.CapturedAt,
                SourceName = p.SourceName,
                Status = p.Status,
                ApplicationStatus = p.ApplicationStatus,
                IsArchived = p.IsArchived,
                Preview = BuildPreview(p.RawText),
                JobTitle = titleCorr ?? extraction?.JobTitle,
                CompanyName = compCorr ?? extraction?.CompanyName,
                Location = locCorr ?? extraction?.RawLocationText,
                RemotePolicy = extraction?.RemotePolicy,
                ExperienceMinYears = minYears,
                ExperienceMaxYears = maxYears,
                SalaryText = FormatSalary(extraction),
                SalaryYearly = extraction?.SalaryMaxYearly ?? extraction?.SalaryMinYearly,
                OverallScore = p.MatchResult?.OverallScore
            };
        }).ToList();

        // 1. Filter by Work Mode (FR-50)
        if (workMode.HasValue)
        {
            items = items.Where(i => i.RemotePolicy == workMode.Value).ToList();
        }

        // 2. Filter by Location (FR-50)
        if (!string.IsNullOrWhiteSpace(location))
        {
            var loc = location.Trim();
            items = items.Where(i => i.Location != null && i.Location.Contains(loc, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // 3. Filter by Experience Range (FR-50)
        if (minExp.HasValue)
        {
            items = items.Where(i => (i.ExperienceMaxYears ?? i.ExperienceMinYears ?? 0) >= minExp.Value).ToList();
        }
        if (maxExp.HasValue)
        {
            items = items.Where(i => (i.ExperienceMinYears ?? 0) <= maxExp.Value).ToList();
        }

        // 4. Sort (FR-51) & Respect Nulls (BR-10)
        int unrankedCount = 0;
        var isAsc = string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(sortBy, "score", StringComparison.OrdinalIgnoreCase))
        {
            var withScore = items.Where(i => i.OverallScore.HasValue).ToList();
            var withoutScore = items.Where(i => !i.OverallScore.HasValue).ToList();
            unrankedCount = withoutScore.Count;

            withScore = isAsc
                ? withScore.OrderBy(i => i.OverallScore!.Value).ToList()
                : withScore.OrderByDescending(i => i.OverallScore!.Value).ToList();

            items = withScore.Concat(withoutScore).ToList();
        }
        else if (string.Equals(sortBy, "salary", StringComparison.OrdinalIgnoreCase))
        {
            var withSalary = items.Where(i => i.SalaryYearly.HasValue).ToList();
            var withoutSalary = items.Where(i => !i.SalaryYearly.HasValue).ToList();
            unrankedCount = withoutSalary.Count;

            withSalary = isAsc
                ? withSalary.OrderBy(i => i.SalaryYearly!.Value).ToList()
                : withSalary.OrderByDescending(i => i.SalaryYearly!.Value).ToList();

            items = withSalary.Concat(withoutSalary).ToList();
        }
        else
        {
            items = isAsc
                ? items.OrderBy(i => i.CapturedAt).ToList()
                : items.OrderByDescending(i => i.CapturedAt).ToList();
        }

        return View(new PostingListViewModel
        {
            IncludeArchived = includeArchived,
            WorkMode = workMode,
            Location = location,
            MinExperience = minExp,
            MaxExperience = maxExp,
            SortBy = sortBy,
            SortOrder = sortOrder,
            UnrankedCount = unrankedCount,
            Postings = items
        });
    }

    /// <summary>Paste-a-posting form (FR-06).</summary>
    [HttpGet]
    public IActionResult Create() => View(new CapturePostingViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CapturePostingViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        DateTimeOffset? capturedAt = model.CapturedOn.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(model.CapturedOn.Value, DateTimeKind.Local))
            : null;

        var posting = await _postings.CapturePastedTextAsync(
            CurrentUserId, model.RawText, model.SourceName, capturedAt, cancellationToken);

        _logger.LogInformation("Captured posting {Reference} for user {UserId}.", posting.Reference, CurrentUserId);

        TempData["StatusMessage"] = $"Saved as {posting.Reference}.";
        return RedirectToAction(nameof(Details), new { id = posting.Id });
    }

    /// <summary>One posting, including its original text, extracted details, and match breakdown (FR-08, FR-11, FR-42, FR-43).</summary>
    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var posting = await _postings.GetForOwnerAsync(id, CurrentUserId, cancellationToken);

        if (posting is null)
            return NotFound();

        var extraction = await _extraction.GetCurrentAsync(id, CurrentUserId, cancellationToken);
        var corrections = await _extraction.GetCorrectionsAsync(id, CurrentUserId, cancellationToken);
        var skills = await _extraction.GetSkillsAsync(id, CurrentUserId, cancellationToken);

        var review = ReviewViewModelBuilder.Build(posting, extraction, corrections, skills);

        // Load MatchResult & SkillGaps
        var matchResult = await _db.MatchResults
            .AsNoTracking()
            .Include(m => m.SkillGaps)
                .ThenInclude(g => g.MasterSkill)
            .FirstOrDefaultAsync(m => m.JobPostingId == id && m.JobPosting.OwnerUserId == CurrentUserId, cancellationToken);

        MatchScoreCardViewModel? scoreCard = null;
        if (matchResult is not null)
        {
            var profile = await _profileService.GetAsync(CurrentUserId, cancellationToken);
            var heldSkillIds = profile?.Skills.ToDictionary(s => s.MasterSkillId, s => s.ProficiencyLevel) ?? [];

            var postingSkills = await _db.PostingSkills
                .AsNoTracking()
                .Where(s => s.JobPostingId == id)
                .Include(s => s.MasterSkill)
                .ToListAsync(cancellationToken);

            var heldSkillsList = new List<MatchSkillItemViewModel>();
            foreach (var ps in postingSkills.Where(s => heldSkillIds.ContainsKey(s.MasterSkillId)))
            {
                heldSkillsList.Add(new MatchSkillItemViewModel
                {
                    MasterSkillId = ps.MasterSkillId,
                    SkillName = ps.MasterSkill.Name,
                    SkillType = ps.SkillType,
                    Proficiency = heldSkillIds.TryGetValue(ps.MasterSkillId, out var prof) ? prof : null
                });
            }

            var missingRequired = matchResult.SkillGaps
                .Where(g => g.SkillType == SkillType.Required)
                .Select(g => new MatchSkillItemViewModel
                {
                    MasterSkillId = g.MasterSkillId,
                    SkillName = g.MasterSkill?.Name ?? "Skill",
                    SkillType = SkillType.Required
                }).ToList();

            var missingPreferred = matchResult.SkillGaps
                .Where(g => g.SkillType == SkillType.Preferred)
                .Select(g => new MatchSkillItemViewModel
                {
                    MasterSkillId = g.MasterSkillId,
                    SkillName = g.MasterSkill?.Name ?? "Skill",
                    SkillType = SkillType.Preferred
                }).ToList();

            scoreCard = new MatchScoreCardViewModel
            {
                HasMatchResult = true,
                OverallScore = matchResult.OverallScore,
                RequiredSkillScore = matchResult.RequiredSkillScore,
                PreferredSkillScore = matchResult.PreferredSkillScore,
                ExperienceScore = matchResult.ExperienceScore,
                ScoringConfigVersion = matchResult.ScoringConfigVersion,
                FeedbackText = matchResult.FeedbackText,
                HeldSkills = heldSkillsList,
                MissingRequiredSkills = missingRequired,
                MissingPreferredSkills = missingPreferred
            };
        }

        return View(new PostingDetailsViewModel
        {
            Id = posting.Id,
            Reference = posting.Reference,
            RawText = posting.RawText,
            CapturedAt = posting.CapturedAt,
            SourceName = posting.SourceName,
            Status = posting.Status,
            ApplicationStatus = posting.ApplicationStatus,
            CaptureMethod = posting.CaptureMethod,
            IsArchived = posting.IsArchived,

            HasExtraction = review.HasExtraction,
            RunStatus = review.RunStatus,
            FailureReason = review.FailureReason,
            JobTitle = FieldValue(review, CorrectableFields.JobTitle),
            CompanyName = FieldValue(review, CorrectableFields.CompanyName),
            Location = FieldValue(review, CorrectableFields.RawLocationText),
            RemotePolicy = extraction?.RemotePolicy,
            ExperienceMinYears = extraction?.ExperienceMinYears,
            ExperienceMaxYears = extraction?.ExperienceMaxYears,
            SalaryText = FormatSalary(extraction),
            RequiredSkillCount = review.RequiredSkills.Count,
            PreferredSkillCount = review.PreferredSkills.Count,
            MatchScoreCard = scoreCard
        });
    }

    /// <summary>Update application lifecycle status (FR-53).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetApplicationStatus(
        int id,
        ApplicationStatus applicationStatus,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        var posting = await _db.JobPostings
            .FirstOrDefaultAsync(p => p.Id == id && p.OwnerUserId == CurrentUserId, cancellationToken);

        if (posting is null)
            return NotFound();

        posting.ApplicationStatus = applicationStatus;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = $"Application status updated to {applicationStatus}.";

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------------------------------------------------------------- extraction

    /// <summary>
    /// Runs extraction over the stored original text (FR-12, FR-21) and goes to review.
    /// Re-runnable: the raw text is never re-captured and never altered (BR-01).
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Extract(int id, CancellationToken cancellationToken)
    {
        var extraction = await _extraction.RunAsync(id, CurrentUserId, cancellationToken);

        if (extraction is null)
            return NotFound();

        TempData["StatusMessage"] = extraction.RunStatus == ExtractionRunStatus.Succeeded
            ? "Extraction finished. Check the details below before confirming."
            : "Extraction could not be completed. Your posting has been kept.";

        return RedirectToAction(nameof(Review), new { id });
    }

    /// <summary>
    /// Review and correct the extracted detail (FR-18). Shows the current extraction with
    /// any standing corrections laid over it (BR-03).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Review(int id, CancellationToken cancellationToken)
    {
        var posting = await _postings.GetForOwnerAsync(id, CurrentUserId, cancellationToken);

        if (posting is null)
            return NotFound();

        var extraction = await _extraction.GetCurrentAsync(id, CurrentUserId, cancellationToken);
        var corrections = await _extraction.GetCorrectionsAsync(id, CurrentUserId, cancellationToken);
        var skills = await _extraction.GetSkillsAsync(id, CurrentUserId, cancellationToken);

        return View(ReviewViewModelBuilder.Build(posting, extraction, corrections, skills));
    }

    /// <summary>
    /// Saves the candidate's corrections and confirms the posting (FR-18, AC-10).
    /// Confirming makes it eligible for scoring — Pending postings are never scored (BR-08).
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(ConfirmExtractionViewModel model, CancellationToken cancellationToken)
    {
        var confirmed = await _extraction.ApplyCorrectionsAsync(
            model.PostingId, CurrentUserId, model.ToCorrections(), cancellationToken);

        if (!confirmed)
            return NotFound();

        _logger.LogInformation("Posting {PostingId} confirmed by user {UserId}.", model.PostingId, CurrentUserId);

        TempData["StatusMessage"] = "Details confirmed.";
        return RedirectToAction(nameof(Details), new { id = model.PostingId });
    }

    /// <summary>Archive or restore (FR-11).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id, bool isArchived, CancellationToken cancellationToken)
    {
        var changed = await _postings.SetArchivedAsync(id, CurrentUserId, isArchived, cancellationToken);

        if (!changed)
            return NotFound();

        TempData["StatusMessage"] = isArchived ? "Posting archived." : "Posting restored.";
        return RedirectToAction(nameof(Index), new { includeArchived = !isArchived });
    }

    /// <summary>Permanent deletion (FR-11, NFR-09).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await _postings.DeleteAsync(id, CurrentUserId, cancellationToken);

        if (!deleted)
            return NotFound();

        _logger.LogInformation("Deleted posting {PostingId} for user {UserId}.", id, CurrentUserId);

        TempData["StatusMessage"] = "Posting deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ----------------------------------------------------------------

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? throw new InvalidOperationException("Authenticated user has no identifier claim."));

    private static string? FieldValue(ReviewExtractionViewModel review, string fieldName) =>
        review.Fields.FirstOrDefault(f => f.FieldName == fieldName)?.Value;

    private static string? FormatSalary(PostingExtraction? extraction)
    {
        if (extraction == null) return null;
        if (extraction.SalaryMinRaw.HasValue && extraction.SalaryMaxRaw.HasValue)
            return $"{extraction.SalaryCurrencyRaw ?? ""}{extraction.SalaryMinRaw:N0} - {extraction.SalaryCurrencyRaw ?? ""}{extraction.SalaryMaxRaw:N0} {(extraction.SalaryPeriodRaw.HasValue ? "/" + extraction.SalaryPeriodRaw.Value : "")}".Trim();
        if (extraction.SalaryMinRaw.HasValue)
            return $"{extraction.SalaryCurrencyRaw ?? ""}{extraction.SalaryMinRaw:N0}+ {(extraction.SalaryPeriodRaw.HasValue ? "/" + extraction.SalaryPeriodRaw.Value : "")}".Trim();
        if (extraction.SalaryMaxRaw.HasValue)
            return $"Up to {extraction.SalaryCurrencyRaw ?? ""}{extraction.SalaryMaxRaw:N0} {(extraction.SalaryPeriodRaw.HasValue ? "/" + extraction.SalaryPeriodRaw.Value : "")}".Trim();
        return null;
    }

    private static string BuildPreview(string rawText)
    {
        var firstLine = rawText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        return firstLine.Length <= 90 ? firstLine : firstLine[..90] + "…";
    }
}
