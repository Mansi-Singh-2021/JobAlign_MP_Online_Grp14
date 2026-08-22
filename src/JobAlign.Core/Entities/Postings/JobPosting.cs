using JobAlign.Core.Entities.Identity;
using JobAlign.Core.Entities.Matching;
using JobAlign.Core.Enums;

namespace JobAlign.Core.Entities.Postings;

/// <summary>
/// A job posting as captured by a candidate (FR-06 to FR-11).
///
/// This entity owns only the facts that are true regardless of extraction:
/// who captured it, from where, when, its reference, its status, and the
/// original text. Everything derived from the text lives in
/// <see cref="PostingExtraction"/> and can be thrown away and regenerated (BR-01).
/// </summary>
public class JobPosting
{
    /// <summary>Constructor used by EF Core when materializing rows.</summary>
    private JobPosting()
    {
        RawText = null!;
        Reference = null!;
    }

    /// <summary>
    /// The only way to create a posting. <paramref name="rawText"/> and
    /// <paramref name="reference"/> are fixed here and have no public setter,
    /// so BR-01 is enforced by the type rather than by convention.
    /// </summary>
    public JobPosting(int ownerUserId, string reference, string rawText, PostingCaptureMethod captureMethod)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            throw new ArgumentException("Raw posting text is required.", nameof(rawText));
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("A posting reference is required.", nameof(reference));

        OwnerUserId = ownerUserId;
        Reference = reference;
        RawText = rawText;
        CaptureMethod = captureMethod;
        Status = PostingStatus.New;              // FR-09
        ApplicationStatus = ApplicationStatus.Saved;
        CapturedAt = DateTimeOffset.UtcNow;
    }

    public int Id { get; set; }

    /// <summary>
    /// The candidate who captured this posting. Every query for postings filters
    /// on this column server-side — the UI is never trusted to hide another
    /// user's rows (BR-09, NFR-04).
    /// </summary>
    public int OwnerUserId { get; set; }
    public ApplicationUser Owner { get; set; } = null!;

    /// <summary>
    /// Unique human-facing reference generated at capture (FR-09).
    /// Write-once: no public setter.
    /// </summary>
    public string Reference { get; private set; }

    /// <summary>
    /// The posting exactly as supplied. Never altered, for the life of the
    /// posting (FR-08, BR-01). Retained so any extraction result can be
    /// reproduced and explained (NFR-08).
    /// </summary>
    public string RawText { get; private set; }

    public PostingCaptureMethod CaptureMethod { get; set; }

    /// <summary>Link the posting came from, where capture was by link (FR-07).</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Free-text source the candidate recorded, e.g. "LinkedIn" (FR-10).</summary>
    public string? SourceName { get; set; }

    /// <summary>When the candidate captured it (FR-10).</summary>
    public DateTimeOffset CapturedAt { get; set; }

    /// <summary>
    /// Extraction lifecycle: New → Pending (on failure) → Confirmed (FR-09, FR-19, AC-10).
    /// Pending postings are excluded from scoring, comparison and the dashboard (BR-08, FR-54).
    /// </summary>
    public PostingStatus Status { get; set; }

    /// <summary>Where the candidate is in applying (FR-53). Independent of <see cref="Status"/>.</summary>
    public ApplicationStatus ApplicationStatus { get; set; }

    /// <summary>Set when the candidate confirms the reviewed details (AC-10).</summary>
    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>Archived postings are hidden from the working list but retained (FR-11).</summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// Extraction runs against <see cref="RawText"/>, newest last. History is kept
    /// so a result can be reproduced against its configuration version (NFR-08, FR-21).
    /// </summary>
    public ICollection<PostingExtraction> Extractions { get; set; } = new List<PostingExtraction>();

    /// <summary>
    /// Candidate corrections. Attached to the posting, not to an extraction run,
    /// which is precisely what makes them survive re-extraction (BR-03).
    /// </summary>
    public ICollection<PostingFieldCorrection> Corrections { get; set; } = new List<PostingFieldCorrection>();

    /// <summary>Skills demanded by this posting, each resolved to a master skill (FR-13, BR-04).</summary>
    public ICollection<PostingSkill> Skills { get; set; } = new List<PostingSkill>();

    /// <summary>Suspected duplicates and confirmed same-role links (FR-24 to FR-26).</summary>
    public ICollection<PostingRelation> Relations { get; set; } = new List<PostingRelation>();

    /// <summary>Completeness assessment (FR-22, FR-23).</summary>
    public PostingQualityAssessment? QualityAssessment { get; set; }

    /// <summary>The current match result for this posting (FR-36 to FR-39).</summary>
    public MatchResult? MatchResult { get; set; }
}
