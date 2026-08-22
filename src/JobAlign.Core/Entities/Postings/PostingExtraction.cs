using JobAlign.Core.Entities.Skills;
using JobAlign.Core.Enums;

namespace JobAlign.Core.Entities.Postings;

/// <summary>
/// The structured result of one extraction run over a posting's raw text
/// (FR-12, FR-21). Derived, disposable and regenerable (BR-01) — nothing here
/// is authoritative over a candidate correction (BR-03).
///
/// Every extracted field is nullable on purpose. A detail the posting did not
/// state is null and renders as "Not specified"; it is never zero, never an
/// empty string, never a guess (BR-02, FR-17, NFR-07). Do not make these
/// columns non-nullable.
/// </summary>
public class PostingExtraction
{
    public int Id { get; set; }

    public int JobPostingId { get; set; }
    public JobPosting JobPosting { get; set; } = null!;

    /// <summary>
    /// Marks the run whose values are shown. Exactly one row per posting may
    /// have this set — enforced by a filtered unique index, not by convention.
    /// </summary>
    public bool IsCurrent { get; set; }

    public ExtractionRunStatus RunStatus { get; set; }

    public DateTimeOffset ExtractedAt { get; set; }

    /// <summary>
    /// Version of the prompt/model configuration used, so a result can be
    /// reproduced and explained later (NFR-08).
    /// </summary>
    public required string ExtractionConfigVersion { get; set; }

    /// <summary>Why the run failed, where it did (FR-19).</summary>
    public string? FailureReason { get; set; }

    // ---- Extracted detail (FR-12). All nullable by design — see class remarks. ----

    public string? JobTitle { get; set; }

    public string? CompanyName { get; set; }

    /// <summary>Location exactly as the posting stated it, retained alongside the
    /// normalized reference (FR-16, and the general rule in BR-05).</summary>
    public string? RawLocationText { get; set; }

    /// <summary>Normalized location, where the raw text resolved to one (FR-16).</summary>
    public int? LocationId { get; set; }
    public Location? Location { get; set; }

    /// <summary>Work mode. "Unclear" is a correct answer, distinct from null
    /// (null = the posting said nothing at all).</summary>
    public RemotePolicy? RemotePolicy { get; set; }

    public decimal? ExperienceMinYears { get; set; }
    public decimal? ExperienceMaxYears { get; set; }

    public string? Responsibilities { get; set; }

    /// <summary>
    /// Short summary of a lengthy description (FR-48). Kept here, with the rest
    /// of the derived values, so it is produced once and stored rather than
    /// regenerated every time the posting is viewed (NFR-13).
    /// </summary>
    public string? Summary { get; set; }

    // ---- Salary (FR-15, BR-05, BR-10) ----
    // The originally stated figures are always kept beside the comparable ones.

    public decimal? SalaryMinRaw { get; set; }
    public decimal? SalaryMaxRaw { get; set; }

    /// <summary>Currency as stated, e.g. "INR", "$".</summary>
    public string? SalaryCurrencyRaw { get; set; }

    /// <summary>Period as stated. Null means the posting did not say (BR-02).</summary>
    public SalaryPeriod? SalaryPeriodRaw { get; set; }

    /// <summary>Comparable yearly figure. Null where no salary was stated —
    /// such postings are excluded from salary sorting rather than treated as 0 (BR-10).</summary>
    public decimal? SalaryMinYearly { get; set; }
    public decimal? SalaryMaxYearly { get; set; }

    /// <summary>Currency the yearly figures are expressed in.</summary>
    public string? SalaryCurrencyNormalized { get; set; }

    /// <summary>Per-field confidence indicators (FR-20, NFR-06).</summary>
    public ICollection<ExtractionFieldConfidence> FieldConfidences { get; set; }
        = new List<ExtractionFieldConfidence>();
}

/// <summary>
/// Confidence the extractor reported for one named field (FR-20).
/// Held as rows rather than a column per field so that adding an extracted
/// field later does not require a schema change here.
/// </summary>
public class ExtractionFieldConfidence
{
    public int Id { get; set; }

    public int PostingExtractionId { get; set; }
    public PostingExtraction PostingExtraction { get; set; } = null!;

    /// <summary>Name of the field on <see cref="PostingExtraction"/>, e.g. "JobTitle".</summary>
    public required string FieldName { get; set; }

    public ConfidenceLevel Confidence { get; set; }

    /// <summary>Optional numeric score 0–1, where the provider supplies one.</summary>
    public decimal? Score { get; set; }
}
