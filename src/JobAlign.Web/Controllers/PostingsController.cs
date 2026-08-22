using System.Security.Claims;
using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Identity;
using JobAlign.Core.Entities.Postings;
using JobAlign.Core.Enums;
using JobAlign.Web.Models.Postings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobAlign.Web.Controllers;

/// <summary>
/// Capture and management of the signed-in candidate's postings (FR-06, FR-08 to FR-11).
/// </summary>
/// <remarks>
/// Candidate-only by role: section 4.3 gives administrators no access to postings, and
/// BR-09 says they may not read them. The role check here is the outer gate; the inner
/// one is that every call into <see cref="IJobPostingService"/> carries the owner id.
///
/// No extraction happens in this controller. Capture and extraction are separate so a
/// posting saves whether or not the AI service is reachable (NFR-06).
/// </remarks>
[Authorize(Roles = RoleNames.Candidate)]
public class PostingsController : Controller
{
    private readonly IJobPostingService _postings;
    private readonly IExtractionService _extraction;
    private readonly ILogger<PostingsController> _logger;

    public PostingsController(
        IJobPostingService postings,
        IExtractionService extraction,
        ILogger<PostingsController> logger)
    {
        _postings = postings;
        _extraction = extraction;
        _logger = logger;
    }

    /// <summary>The saved-postings list (FR-11).</summary>
    [HttpGet]
    public async Task<IActionResult> Index(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var postings = await _postings.ListForOwnerAsync(CurrentUserId, includeArchived, cancellationToken);

        return View(new PostingListViewModel
        {
            IncludeArchived = includeArchived,
            Postings = postings.Select(ToListItem).ToList()
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

        // A date-only input carries no offset; treat what the candidate typed as a
        // local date and convert, rather than silently reading it as UTC.
        DateTimeOffset? capturedAt = model.CapturedOn.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(model.CapturedOn.Value, DateTimeKind.Local))
            : null;

        var posting = await _postings.CapturePastedTextAsync(
            CurrentUserId, model.RawText, model.SourceName, capturedAt, cancellationToken);

        _logger.LogInformation("Captured posting {Reference} for user {UserId}.", posting.Reference, CurrentUserId);

        TempData["StatusMessage"] = $"Saved as {posting.Reference}.";
        return RedirectToAction(nameof(Details), new { id = posting.Id });
    }

    /// <summary>One posting, including its original text (FR-08, FR-11).</summary>
    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var posting = await _postings.GetForOwnerAsync(id, CurrentUserId, cancellationToken);

        // NotFound rather than Forbid: telling the caller a posting exists but is
        // someone else's is itself a disclosure (BR-09).
        if (posting is null)
            return NotFound();

        var extraction = await _extraction.GetCurrentAsync(id, CurrentUserId, cancellationToken);
        var corrections = await _extraction.GetCorrectionsAsync(id, CurrentUserId, cancellationToken);
        var skills = await _extraction.GetSkillsAsync(id, CurrentUserId, cancellationToken);

        // Reuse the review builder so this page and the review screen agree on what a field
        // says — both must show the correction where one stands, not the raw extraction (BR-03).
        var review = ReviewViewModelBuilder.Build(posting, extraction, corrections, skills);

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
            RequiredSkillCount = review.RequiredSkills.Count,
            PreferredSkillCount = review.PreferredSkills.Count
        });
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

    /// <summary>
    /// The signed-in user's id. Read from the authentication cookie, never from the
    /// request — a user-supplied owner id would defeat BR-09 entirely.
    /// </summary>
    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? throw new InvalidOperationException("Authenticated user has no identifier claim."));

    private static string? FieldValue(ReviewExtractionViewModel review, string fieldName) =>
        review.Fields.FirstOrDefault(f => f.FieldName == fieldName)?.Value;

    private static PostingListItemViewModel ToListItem(JobPosting posting) => new()
    {
        Id = posting.Id,
        Reference = posting.Reference,
        CapturedAt = posting.CapturedAt,
        SourceName = posting.SourceName,
        Status = posting.Status,
        ApplicationStatus = posting.ApplicationStatus,
        IsArchived = posting.IsArchived,
        Preview = BuildPreview(posting.RawText)
    };

    /// <summary>
    /// First non-blank line of the posting, trimmed to a readable length. A stand-in
    /// for a title until extraction runs — not a guess at one (BR-02).
    /// </summary>
    private static string BuildPreview(string rawText)
    {
        var firstLine = rawText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        return firstLine.Length <= 90 ? firstLine : firstLine[..90] + "…";
    }
}
