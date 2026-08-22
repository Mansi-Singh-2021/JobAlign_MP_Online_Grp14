using JobAlign.Core.Enums;

namespace JobAlign.Core.Extraction;

/// <summary>
/// Structured detail read out of one posting's raw text (FR-12, FR-13).
/// A plain DTO: validated and mapped onto PostingExtraction by the caller,
/// never handed to EF directly. Untrusted input (shared brief, rule 6).
/// </summary>
public sealed class ExtractedPosting
{
    public string? JobTitle { get; init; }
    public string? CompanyName { get; init; }

    /// <summary>Location exactly as stated. Normalization to a LocationId is a separate step (FR-16).</summary>
    public string? RawLocationText { get; init; }

    /// <summary>Unclear is a valid answer and is NOT the same as null (null = the posting said nothing).</summary>
    public RemotePolicy? RemotePolicy { get; init; }

    public decimal? ExperienceMinYears { get; init; }
    public decimal? ExperienceMaxYears { get; init; }

    public decimal? SalaryMinRaw { get; init; }
    public decimal? SalaryMaxRaw { get; init; }
    public string? SalaryCurrencyRaw { get; init; }
    public SalaryPeriod? SalaryPeriodRaw { get; init; }

    public string? Responsibilities { get; init; }

    /// <summary>Short summary of a long description (FR-48). Null unless the provider produced one.</summary>
    public string? Summary { get; init; }

    public IReadOnlyList<ExtractedSkill> Skills { get; init; } = [];
    public IReadOnlyList<ExtractedFieldConfidence> Confidences { get; init; } = [];
}

/// <summary>One skill as the posting worded it, before resolution to a MasterSkill (FR-13, BR-04).</summary>
public sealed record ExtractedSkill(string RawText, SkillType SkillType, ConfidenceLevel? Confidence);

/// <summary>Confidence for one named field of ExtractedPosting, e.g. "JobTitle" (FR-20).</summary>
public sealed record ExtractedFieldConfidence(string FieldName, ConfidenceLevel Confidence, decimal? Score);
