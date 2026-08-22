using JobAlign.Core.Entities.Postings;
using JobAlign.Core.Enums;
using JobAlign.Core.Extraction;

namespace JobAlign.Web.Models.Postings;

/// <summary>How a reviewable field should be rendered (FR-18).</summary>
public enum FieldInputKind
{
    Text,
    Number,
    MultilineText,
    RemotePolicyChoice,
    SalaryPeriodChoice
}

/// <summary>
/// One reviewable field: what was extracted, what the candidate corrected it to, and how
/// confident the extractor was (FR-18, FR-20).
/// </summary>
public class ExtractedFieldViewModel
{
    public required string FieldName { get; init; }
    public required string Label { get; init; }

    /// <summary>
    /// What the field currently says: the correction where one stands, otherwise the
    /// extracted value. Null means the posting did not state it, and the view must render
    /// that as "Not specified" rather than as an empty box (BR-02, FR-17).
    /// </summary>
    public string? Value { get; init; }

    /// <summary>What extraction produced, shown alongside a correction so the change is visible.</summary>
    public string? ExtractedValue { get; init; }

    public bool IsCorrected { get; init; }

    public ConfidenceLevel? Confidence { get; init; }

    /// <summary>Flagged in the view so the candidate knows where to look first (FR-20, NFR-06).</summary>
    public bool IsLowConfidence => Confidence == ConfidenceLevel.Low;

    public FieldInputKind InputKind { get; init; }

    /// <summary>Allowed values for the two enum-backed fields. Empty otherwise.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];
}

/// <summary>A skill the posting asks for, resolved to the master list (FR-13, BR-04).</summary>
public class ExtractedSkillViewModel
{
    public required string CanonicalName { get; init; }

    /// <summary>What the posting actually said, shown when it differs from the canonical name.</summary>
    public string? RawText { get; init; }

    public SkillType SkillType { get; init; }
    public PostingSkillSource Source { get; init; }
}

/// <summary>
/// One labelled group of skills for the review screen. Required and preferred are shown
/// separately and styled differently — FR-43 needs the distinction to survive to the eye,
/// not just to the database.
/// </summary>
public sealed record SkillListModel(
    string Heading,
    IReadOnlyList<ExtractedSkillViewModel> Skills,
    string BadgeClass);

/// <summary>The review and correct screen (FR-18).</summary>
public class ReviewExtractionViewModel
{
    public int PostingId { get; init; }
    public required string Reference { get; init; }
    public PostingStatus Status { get; init; }

    /// <summary>False when the posting has never been extracted — the view offers to run it.</summary>
    public bool HasExtraction { get; init; }

    public ExtractionRunStatus? RunStatus { get; init; }

    /// <summary>Why the last run failed, where it did (FR-19).</summary>
    public string? FailureReason { get; init; }

    public DateTimeOffset? ExtractedAt { get; init; }
    public string? ConfigVersion { get; init; }

    public IReadOnlyList<ExtractedFieldViewModel> Fields { get; init; } = [];

    public IReadOnlyList<ExtractedSkillViewModel> RequiredSkills { get; init; } = [];
    public IReadOnlyList<ExtractedSkillViewModel> PreferredSkills { get; init; } = [];

    public bool IsFailed => RunStatus == ExtractionRunStatus.Failed;
}

/// <summary>One field as posted back from the review form.</summary>
public class FieldSubmission
{
    public string FieldName { get; set; } = string.Empty;
    public string? Value { get; set; }
}

/// <summary>The review form's post body (FR-18).</summary>
public class ConfirmExtractionViewModel
{
    public int PostingId { get; set; }
    public List<FieldSubmission> Fields { get; set; } = [];

    /// <summary>Only whitelisted names reach the service; the service checks again (BR-03).</summary>
    public IReadOnlyDictionary<string, string?> ToCorrections() =>
        Fields
            .Where(f => !string.IsNullOrWhiteSpace(f.FieldName))
            .GroupBy(f => f.FieldName)
            .ToDictionary(g => g.Key, g => g.Last().Value);
}

/// <summary>
/// Builds the review model from an extraction plus its standing corrections.
/// </summary>
/// <remarks>
/// The overlay is the whole point: a posting reads as "the current extraction, with every
/// correction laid on top" (BR-03). Reading it any other way would let a re-extraction
/// appear to discard the candidate's edits.
/// </remarks>
public static class ReviewViewModelBuilder
{
    public static ReviewExtractionViewModel Build(
        JobPosting posting,
        PostingExtraction? extraction,
        IReadOnlyList<PostingFieldCorrection> corrections,
        IReadOnlyList<PostingSkill> skills)
    {
        var correctionsByField = corrections.ToDictionary(c => c.FieldName, c => c);

        var fields = extraction is null || extraction.RunStatus == ExtractionRunStatus.Failed
            ? []
            : ExtractionFields.ReviewOrder
                .Select(name => BuildField(extraction, name, correctionsByField))
                .ToList();

        var skillViews = skills.Select(s => new ExtractedSkillViewModel
        {
            CanonicalName = s.MasterSkill.Name,
            RawText = ExtractionFields.IsSameValue(s.RawText, s.MasterSkill.Name) ? null : s.RawText,
            SkillType = s.SkillType,
            Source = s.Source
        }).ToList();

        return new ReviewExtractionViewModel
        {
            PostingId = posting.Id,
            Reference = posting.Reference,
            Status = posting.Status,
            HasExtraction = extraction is not null,
            RunStatus = extraction?.RunStatus,
            FailureReason = extraction?.FailureReason,
            ExtractedAt = extraction?.ExtractedAt,
            ConfigVersion = extraction?.ExtractionConfigVersion,
            Fields = fields,
            RequiredSkills = skillViews.Where(s => s.SkillType == SkillType.Required).ToList(),
            PreferredSkills = skillViews.Where(s => s.SkillType == SkillType.Preferred).ToList()
        };
    }

    private static ExtractedFieldViewModel BuildField(
        PostingExtraction extraction,
        string fieldName,
        IReadOnlyDictionary<string, PostingFieldCorrection> corrections)
    {
        var extractedValue = ExtractionFields.ReadAsText(extraction, fieldName);
        var hasCorrection = corrections.TryGetValue(fieldName, out var correction);

        return new ExtractedFieldViewModel
        {
            FieldName = fieldName,
            Label = ExtractionFields.Label(fieldName),
            Value = hasCorrection ? correction!.CorrectedValue : extractedValue,
            ExtractedValue = extractedValue,
            IsCorrected = hasCorrection,
            Confidence = extraction.FieldConfidences
                .FirstOrDefault(c => c.FieldName == fieldName)?.Confidence,
            InputKind = InputKindFor(fieldName),
            Options = OptionsFor(fieldName)
        };
    }

    private static FieldInputKind InputKindFor(string fieldName) => fieldName switch
    {
        CorrectableFields.RemotePolicy => FieldInputKind.RemotePolicyChoice,
        CorrectableFields.SalaryPeriodRaw => FieldInputKind.SalaryPeriodChoice,
        CorrectableFields.Responsibilities => FieldInputKind.MultilineText,
        CorrectableFields.ExperienceMinYears or CorrectableFields.ExperienceMaxYears
            or CorrectableFields.SalaryMinRaw or CorrectableFields.SalaryMaxRaw => FieldInputKind.Number,
        _ => FieldInputKind.Text
    };

    private static IReadOnlyList<string> OptionsFor(string fieldName) => fieldName switch
    {
        // Unclear is an option, not an absence. A posting that says "hybrid where possible"
        // is Unclear; one that says nothing at all stays blank (BR-02).
        CorrectableFields.RemotePolicy => Enum.GetNames<RemotePolicy>(),
        CorrectableFields.SalaryPeriodRaw => Enum.GetNames<SalaryPeriod>(),
        _ => []
    };
}
