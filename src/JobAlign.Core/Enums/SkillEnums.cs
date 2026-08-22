namespace JobAlign.Core.Enums;

/// <summary>
/// Whether a posting demands a skill or merely prefers it (FR-13).
/// Required skills carry greater weight in the overall match score (BR-07).
/// Mirrors the AI extraction contract: required | preferred.
/// </summary>
public enum SkillType
{
    Required = 0,
    Preferred = 1
}

/// <summary>
/// Where a posting skill came from. Re-extraction replaces only
/// <see cref="Extracted"/> rows, so skills the candidate added by hand
/// survive a re-run (BR-03).
/// </summary>
public enum PostingSkillSource
{
    Extracted = 0,
    UserAdded = 1
}
