using System.ComponentModel.DataAnnotations;
using JobAlign.Core.Enums;
using JobAlign.Web.Models.Dashboard;

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

/// <summary>One row in the saved-postings list (FR-11, FR-50, FR-51, NFR-02).</summary>
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
    /// First line of the raw text, used as a fallback if no extracted title.
    /// </summary>
    public string Preview { get; init; } = string.Empty;

    public string? JobTitle { get; init; }
    public string? CompanyName { get; init; }
    public string? Location { get; init; }
    public RemotePolicy? RemotePolicy { get; init; }
    public decimal? ExperienceMinYears { get; init; }
    public decimal? ExperienceMaxYears { get; init; }
    public string? SalaryText { get; init; }
    public decimal? SalaryYearly { get; init; }
    public decimal? OverallScore { get; init; }
}

/// <summary>The saved-postings page with filtering and sorting (FR-11, FR-50, FR-51).</summary>
public class PostingListViewModel
{
    public IReadOnlyList<PostingListItemViewModel> Postings { get; init; } = [];
    public bool IncludeArchived { get; init; }

    // Filter properties (FR-50)
    public RemotePolicy? WorkMode { get; init; }
    public string? Location { get; init; }
    public decimal? MinExperience { get; init; }
    public decimal? MaxExperience { get; init; }

    // Sort properties (FR-51)
    public string? SortBy { get; init; } = "date";
    public string? SortOrder { get; init; } = "desc";

    /// <summary>Number of postings excluded from the ranked list due to null score/salary (BR-10).</summary>
    public int UnrankedCount { get; init; }
}

/// <summary>A single posting (FR-11). Extracted detail and match scores attached.</summary>
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

    // ---- Extraction summary (FR-12, FR-18). Corrections are already applied (BR-03). ----

    public bool HasExtraction { get; init; }
    public ExtractionRunStatus? RunStatus { get; init; }
    public string? FailureReason { get; init; }

    /// <summary>Null means the posting did not state it — render "Not specified" (BR-02).</summary>
    public string? JobTitle { get; init; }
    public string? CompanyName { get; init; }
    public string? Location { get; init; }
    public RemotePolicy? RemotePolicy { get; init; }
    public decimal? ExperienceMinYears { get; init; }
    public decimal? ExperienceMaxYears { get; init; }
    public string? SalaryText { get; init; }

    public int RequiredSkillCount { get; init; }
    public int PreferredSkillCount { get; init; }

    public bool IsExtractionFailed => RunStatus == ExtractionRunStatus.Failed;

    /// <summary>Match score breakdown and skill gaps card (FR-40, FR-42, FR-43).</summary>
    public MatchScoreCardViewModel? MatchScoreCard { get; init; }
}
