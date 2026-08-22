namespace JobAlign.Core.Entities.Skills;

/// <summary>
/// An alternative spelling that resolves to a master skill (FR-14, FR-58).
/// This table is what makes "C Sharp", "C-Sharp" and "C#" one skill (BR-04, BR-05
/// of the earlier draft; BR-04 here).
/// </summary>
public class SkillAlias
{
    public int Id { get; set; }

    /// <summary>The alternative name as a human would write it.</summary>
    public required string Alias { get; set; }

    /// <summary>
    /// Lookup form of <see cref="Alias"/>, matching MasterSkill.NormalizedName
    /// conventions. Unique across the table — one alias cannot resolve two ways.
    /// </summary>
    public required string NormalizedAlias { get; set; }

    public int MasterSkillId { get; set; }
    public MasterSkill MasterSkill { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
