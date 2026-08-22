using JobAlign.Core.Entities.Skills;
using JobAlign.Core.Enums;

namespace JobAlign.Core.Entities.Profiles;

/// <summary>
/// A resume the candidate uploaded (FR-30). Saved independently of parsing, so
/// an AI outage never costs the upload (NFR-06, FR-19). Deletable by the
/// candidate (FR-34, NFR-09).
/// </summary>
public class Resume
{
    public int Id { get; set; }

    public int CandidateProfileId { get; set; }
    public CandidateProfile CandidateProfile { get; set; } = null!;

    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long FileSizeBytes { get; set; }

    /// <summary>The uploaded document itself.</summary>
    public byte[]? FileContent { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public ResumeExtractionStatus ExtractionStatus { get; set; } = ResumeExtractionStatus.Pending;

    public DateTimeOffset? ExtractedAt { get; set; }

    /// <summary>Why parsing failed, where it did.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Plain text recovered from the document, used as extraction input (FR-31).</summary>
    public string? ParsedTextContent { get; set; }

    /// <summary>Skills proposed from this resume, pending the candidate's decision (FR-32).</summary>
    public ICollection<ResumeSkillSuggestion> SkillSuggestions { get; set; }
        = new List<ResumeSkillSuggestion>();
}

/// <summary>
/// A skill found in a resume, offered to the candidate for confirmation (FR-31, FR-32).
///
/// This table exists so that BR-06 is structural: a suggestion is not a
/// ProfileSkill, so it cannot be picked up by scoring. Accepting one creates a
/// ProfileSkill; nothing here is ever read by the match calculation.
/// </summary>
public class ResumeSkillSuggestion
{
    public int Id { get; set; }

    public int ResumeId { get; set; }
    public Resume Resume { get; set; } = null!;

    /// <summary>
    /// The master skill this suggestion resolved to. Nullable, because an
    /// unrecognised name can still be shown to the candidate — but it must
    /// resolve before it can be confirmed into the profile (BR-04).
    /// </summary>
    public int? MasterSkillId { get; set; }
    public MasterSkill? MasterSkill { get; set; }

    /// <summary>The wording the resume used.</summary>
    public required string RawText { get; set; }

    /// <summary>Set once accepted; the corresponding ProfileSkill is created at that point.</summary>
    public bool IsConfirmed { get; set; }

    /// <summary>Set when the candidate rejects the suggestion, so it is not offered again.</summary>
    public bool IsDismissed { get; set; }

    public DateTimeOffset SuggestedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
