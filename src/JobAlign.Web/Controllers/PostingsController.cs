using System.Security.Claims;
using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Identity;
using JobAlign.Core.Entities.Postings;
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
    private readonly ILogger<PostingsController> _logger;

    public PostingsController(IJobPostingService postings, ILogger<PostingsController> logger)
    {
        _postings = postings;
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
            IsArchived = posting.IsArchived
        });
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
