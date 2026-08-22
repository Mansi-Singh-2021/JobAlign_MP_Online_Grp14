using JobAlign.Core.Entities.Skills;
using JobAlign.Core.Enums;

namespace JobAlign.Core.Entities.Matching;

/// <summary>
/// One skill a posting asks for that the candidate does not hold (FR-42).
/// <see cref="SkillType"/> keeps missing required skills distinguishable from
/// missing preferred ones (FR-43) and drives roadmap priority (FR-46).
/// </summary>
public class SkillGap
{
    public int Id { get; set; }

    public int MatchResultId { get; set; }
    public MatchResult MatchResult { get; set; } = null!;

    public int MasterSkillId { get; set; }
    public MasterSkill MasterSkill { get; set; } = null!;

    public SkillType SkillType { get; set; }
}
