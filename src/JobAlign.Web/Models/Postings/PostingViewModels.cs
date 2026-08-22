using System.ComponentModel.DataAnnotations;
using JobAlign.Core.Enums;

namespace JobAlign.Web.Models.Postings;

/// <summary>Paste-a-posting form (FR-06, FR-10).</summary>
public class CapturePostingViewModel
{
    /// <summary>
    /// The posting exactly as the candidate pasted it. Stored unmodified and never
    /// edited afterwards (FR-08, BR-01) — so this field is only ever bound on create.
    /// </summary>
    [Required(ErrorMessage = "Paste the job posting text.")]
    [DataType(DataType.MultilineText)]
    [Display(Name = "Job posting text")]
    public string RawText { get; set; } = string.Empty;

    /// <summary>Optional, per FR-10 — for example "LinkedIn" or "Company careers page".</summary>
    [StringLength(128)]
    [Display(Name = "Where did you find it?")]
    public string? SourceName { get; set; }

    /// <summary>Optional, per FR-10. Left blank, capture is recorded as now.</summary>
    [DataType(DataType.Date)]
    [Display(Name = "Date captured")]
    public DateTime? CapturedOn { get; set; }
}

/// <summary>One row in the saved-postings list (FR-11, NFR-02).</summary>
public class PostingListItemViewModel
{
    public int Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public DateTimeOffset CapturedAt { get; init; }
    public string? SourceName { get; init; }
    public PostingStatus Status { get; init; }
    public ApplicationStatus ApplicationStatus { get; init; }
    public bool IsArchived { get; init; }

    /// <summary>
    /// First line of the raw text, used as a stand-in title. There is no extracted
    /// job title yet — extraction is a later step, and inventing one here would
    /// breach BR-02.
    /// </summary>
    public string Preview { get; init; } = string.Empty;
}

/// <summary>The saved-postings page (FR-11).</summary>
public class PostingListViewModel
{
    public IReadOnlyList<PostingListItemViewModel> Postings { get; init; } = [];
    public bool IncludeArchived { get; init; }
}

/// <summary>A single posting (FR-11). Extracted detail arrives in a later step.</summary>
public class PostingDetailsViewModel
{
    public int Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string RawText { get; init; } = string.Empty;
    public DateTimeOffset CapturedAt { get; init; }
    public string? SourceName { get; init; }
    public PostingStatus Status { get; init; }
    public ApplicationStatus ApplicationStatus { get; init; }
    public PostingCaptureMethod CaptureMethod { get; init; }
    public bool IsArchived { get; init; }
}
