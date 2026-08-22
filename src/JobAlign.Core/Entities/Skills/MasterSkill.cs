using JobAlign.Core.Entities.Matching;
using JobAlign.Core.Entities.Postings;
using JobAlign.Core.Entities.Profiles;

namespace JobAlign.Core.Entities.Skills;

/// <summary>
/// The single approved name for a skill (FR-57). Every skill anywhere in the
/// system — posting, resume or profile — resolves to exactly one row here
/// (BR-04). No table stores a free-text skill as its identity.
/// </summary>
public class MasterSkill
{
    public int Id { get; set; }

    /// <summary>The approved display name, e.g. "C#". Unique.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Case- and punctuation-insensitive form used for lookup, e.g. "csharp".
    /// Held as a column rather than computed at query time so the unique index
    /// on it can actually be used by the database.
    /// </summary>
    public required string NormalizedName { get; set; }

    /// <summary>Grouping such as "Language" or "Cloud" (Section 10). Optional.</summary>
    public string? Category { get; set; }

    /// <summary>Deactivated skills stay in place for history but are not offered (FR-57).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Set when an administrator merges this skill into another (FR-58).
    /// The row is retained rather than deleted so existing postings and profiles
    /// that referenced it can still be resolved and explained.
    /// </summary>
    public int? MergedIntoMasterSkillId { get; set; }
    public MasterSkill? MergedIntoMasterSkill { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<SkillAlias> Aliases { get; set; } = new List<SkillAlias>();
    public ICollection<PostingSkill> PostingSkills { get; set; } = new List<PostingSkill>();
    public ICollection<ProfileSkill> ProfileSkills { get; set; } = new List<ProfileSkill>();
    public ICollection<SkillGap> SkillGaps { get; set; } = new List<SkillGap>();
    public ICollection<RoadmapItem> RoadmapItems { get; set; } = new List<RoadmapItem>();
}
