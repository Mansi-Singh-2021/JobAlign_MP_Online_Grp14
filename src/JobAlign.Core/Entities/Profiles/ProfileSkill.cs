using JobAlign.Core.Entities.Skills;
using JobAlign.Core.Enums;

namespace JobAlign.Core.Entities.Profiles;

/// <summary>
/// A skill the candidate holds, with proficiency (FR-28) and resolved to the
/// master list by the same rules applied to posting skills (FR-29, BR-04).
///
/// Everything in this table is confirmed by the candidate. Skills merely
/// suggested from a resume live in <see cref="ResumeSkillSuggestion"/> and do
/// not reach this table — and therefore cannot affect a match score — until the
/// candidate accepts them (BR-06, FR-32).
/// </summary>
public class ProfileSkill
{
    public int Id { get; set; }

    public int CandidateProfileId { get; set; }
    public CandidateProfile CandidateProfile { get; set; } = null!;

    public int MasterSkillId { get; set; }
    public MasterSkill MasterSkill { get; set; } = null!;

    public ProficiencyLevel ProficiencyLevel { get; set; }

    /// <summary>How this skill came to be confirmed (Section 10: "source of confirmation").</summary>
    public ProfileSkillSource Source { get; set; }

    public DateTimeOffset ConfirmedAt { get; set; }
}
