using System.Globalization;
using JobAlign.Core.Entities.Postings;

namespace JobAlign.Core.Extraction;

/// <summary>
/// Reads an extracted field by name, and names it for display (FR-18).
/// </summary>
/// <remarks>
/// One place, used by both the review screen and the correction comparison. If the two
/// read a field differently — say one formats a decimal as "3.0" and the other as "3" —
/// then confirming without changing anything records a correction that was never made, and
/// BR-03 starts protecting a value the candidate never typed. Keeping both on this reader
/// is what stops that.
///
/// Field names are the <see cref="CorrectableFields"/> constants.
/// </remarks>
public static class ExtractionFields
{
    /// <summary>
    /// The fields offered for review, in the order they should be shown. LocationId is
    /// deliberately absent: it is a normalized foreign key, not something a candidate
    /// types, and location normalization is a later build step (FR-16).
    /// </summary>
    public static readonly IReadOnlyList<string> ReviewOrder =
    [
        CorrectableFields.JobTitle,
        CorrectableFields.CompanyName,
        CorrectableFields.RawLocationText,
        CorrectableFields.RemotePolicy,
        CorrectableFields.ExperienceMinYears,
        CorrectableFields.ExperienceMaxYears,
        CorrectableFields.SalaryMinRaw,
        CorrectableFields.SalaryMaxRaw,
        CorrectableFields.SalaryCurrencyRaw,
        CorrectableFields.SalaryPeriodRaw,
        CorrectableFields.Responsibilities
    ];

    /// <summary>
    /// The extracted value as text, or null where the posting did not state it.
    /// Null is meaningful: it renders as "Not specified" and is never blanked to ""
    /// (BR-02, FR-17).
    /// </summary>
    public static string? ReadAsText(PostingExtraction extraction, string fieldName) => fieldName switch
    {
        CorrectableFields.JobTitle           => extraction.JobTitle,
        CorrectableFields.CompanyName        => extraction.CompanyName,
        CorrectableFields.RawLocationText    => extraction.RawLocationText,
        CorrectableFields.RemotePolicy       => extraction.RemotePolicy?.ToString(),
        CorrectableFields.ExperienceMinYears => Format(extraction.ExperienceMinYears),
        CorrectableFields.ExperienceMaxYears => Format(extraction.ExperienceMaxYears),
        CorrectableFields.SalaryMinRaw       => Format(extraction.SalaryMinRaw),
        CorrectableFields.SalaryMaxRaw       => Format(extraction.SalaryMaxRaw),
        CorrectableFields.SalaryCurrencyRaw  => extraction.SalaryCurrencyRaw,
        CorrectableFields.SalaryPeriodRaw    => extraction.SalaryPeriodRaw?.ToString(),
        CorrectableFields.Responsibilities   => extraction.Responsibilities,
        _ => null
    };

    public static string Label(string fieldName) => fieldName switch
    {
        CorrectableFields.JobTitle           => "Job title",
        CorrectableFields.CompanyName        => "Company",
        CorrectableFields.RawLocationText    => "Location",
        CorrectableFields.RemotePolicy       => "Work mode",
        CorrectableFields.ExperienceMinYears => "Experience from (years)",
        CorrectableFields.ExperienceMaxYears => "Experience to (years)",
        CorrectableFields.SalaryMinRaw       => "Salary from",
        CorrectableFields.SalaryMaxRaw       => "Salary to",
        CorrectableFields.SalaryCurrencyRaw  => "Currency",
        CorrectableFields.SalaryPeriodRaw    => "Salary period",
        CorrectableFields.Responsibilities   => "Responsibilities",
        _ => fieldName
    };

    /// <summary>
    /// Treats null, empty and whitespace as the same thing when deciding whether the
    /// candidate changed a field. Without this, clearing an already-empty box would record
    /// a correction.
    /// </summary>
    public static bool IsSameValue(string? left, string? right) =>
        string.Equals(
            string.IsNullOrWhiteSpace(left) ? null : left.Trim(),
            string.IsNullOrWhiteSpace(right) ? null : right.Trim(),
            StringComparison.Ordinal);

    /// <summary>Invariant culture, trailing zeros trimmed — "3", not "3.00" or "3,00".</summary>
    private static string? Format(decimal? value) =>
        value?.ToString("0.##", CultureInfo.InvariantCulture);
}
