using JobAlign.Core.Entities.Identity;

namespace JobAlign.Core.Entities.Postings;

/// <summary>
/// A candidate's correction to one extracted field (FR-18, BR-03).
///
/// The key design point: this row hangs off the <b>posting</b>, not off a
/// <see cref="PostingExtraction"/>. Re-running extraction replaces extraction
/// rows and leaves these untouched, so "a correction takes precedence over the
/// extracted value and is preserved when extraction is re-run" is guaranteed by
/// the shape of the schema rather than by remembering to write the right code.
///
/// Reading a posting therefore means: take the current extraction, then overlay
/// every correction on top of it.
/// </summary>
public class PostingFieldCorrection
{
    public int Id { get; set; }

    public int JobPostingId { get; set; }
    public JobPosting JobPosting { get; set; } = null!;

    /// <summary>
    /// Which field was corrected — one of <see cref="CorrectableFields"/>.
    /// Unique per posting, so a field has at most one standing correction.
    /// </summary>
    public required string FieldName { get; set; }

    /// <summary>
    /// The candidate's value, held as text and parsed against the target field's
    /// type on read.
    ///
    /// Null is meaningful and is not the same as "no correction": it records the
    /// candidate asserting the posting does not state this detail, which must
    /// render as "Not specified" (BR-02, FR-17). The presence of the row is what
    /// signals a correction exists.
    /// </summary>
    public string? CorrectedValue { get; set; }

    public DateTimeOffset CorrectedAt { get; set; }

    public int CorrectedByUserId { get; set; }
    public ApplicationUser CorrectedBy { get; set; } = null!;
}

/// <summary>
/// The extracted fields a candidate is allowed to correct (FR-18).
/// Held as constants so a correction row can never name a field that does not exist.
/// </summary>
public static class CorrectableFields
{
    public const string JobTitle = nameof(PostingExtraction.JobTitle);
    public const string CompanyName = nameof(PostingExtraction.CompanyName);
    public const string RawLocationText = nameof(PostingExtraction.RawLocationText);
    public const string LocationId = nameof(PostingExtraction.LocationId);
    public const string RemotePolicy = nameof(PostingExtraction.RemotePolicy);
    public const string ExperienceMinYears = nameof(PostingExtraction.ExperienceMinYears);
    public const string ExperienceMaxYears = nameof(PostingExtraction.ExperienceMaxYears);
    public const string Responsibilities = nameof(PostingExtraction.Responsibilities);
    public const string SalaryMinRaw = nameof(PostingExtraction.SalaryMinRaw);
    public const string SalaryMaxRaw = nameof(PostingExtraction.SalaryMaxRaw);
    public const string SalaryCurrencyRaw = nameof(PostingExtraction.SalaryCurrencyRaw);
    public const string SalaryPeriodRaw = nameof(PostingExtraction.SalaryPeriodRaw);

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        JobTitle, CompanyName, RawLocationText, LocationId, RemotePolicy,
        ExperienceMinYears, ExperienceMaxYears, Responsibilities,
        SalaryMinRaw, SalaryMaxRaw, SalaryCurrencyRaw, SalaryPeriodRaw
    };
}
