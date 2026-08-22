using JobAlign.Core.Entities.Skills;
using JobAlign.Core.Enums;

namespace JobAlign.Core.Entities.Postings;

/// <summary>
/// A skill this posting asks for, classified as required or preferred (FR-13).
///
/// <see cref="MasterSkillId"/> is a foreign key, never a string: every posting
/// skill resolves to exactly one master skill (BR-04). <see cref="RawText"/> is
/// kept only as provenance — what the posting actually said — and is never used
/// as the skill's identity.
/// </summary>
public class PostingSkill
{
    public int Id { get; set; }

    public int JobPostingId { get; set; }
    public JobPosting JobPosting { get; set; } = null!;

    /// <summary>The resolved master skill. This, not <see cref="RawText"/>, is the identity.</summary>
    public int MasterSkillId { get; set; }
    public MasterSkill MasterSkill { get; set; } = null!;

    /// <summary>Required skills weigh more than preferred ones when scoring (BR-07).</summary>
    public SkillType SkillType { get; set; }

    /// <summary>
    /// The wording the posting used, e.g. "C-Sharp", retained for explanation
    /// and for reviewing how well alias resolution is working (FR-59).
    /// </summary>
    public string? RawText { get; set; }

    /// <summary>
    /// Extracted rows are replaced on re-extraction; user-added rows are not (BR-03).
    /// </summary>
    public PostingSkillSource Source { get; set; }
}
