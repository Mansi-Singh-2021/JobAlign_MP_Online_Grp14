using JobAlign.Core.Entities.Profiles;
using JobAlign.Core.Entities.Skills;
using JobAlign.Core.Enums;

namespace JobAlign.Core.Entities.Matching;

/// <summary>
/// One skill on the candidate's learning roadmap (FR-46).
///
/// Ordering comes from how often the skill is missing across the candidate's
/// saved postings and whether it is required or preferred — hence the two
/// occurrence counts, which are what make the ordering explainable rather than
/// arbitrary (FR-45, FR-46).
/// </summary>
public class RoadmapItem
{
    public int Id { get; set; }

    public int CandidateProfileId { get; set; }
    public CandidateProfile CandidateProfile { get; set; } = null!;

    public int MasterSkillId { get; set; }
    public MasterSkill MasterSkill { get; set; } = null!;

    /// <summary>Computed rank, 1 = learn first (FR-46).</summary>
    public int Priority { get; set; }

    /// <summary>Postings where this skill is missing and required (FR-45).</summary>
    public int RequiredOccurrenceCount { get; set; }

    /// <summary>Postings where this skill is missing and preferred (FR-45).</summary>
    public int PreferredOccurrenceCount { get; set; }

    public RoadmapItemStatus Status { get; set; } = RoadmapItemStatus.NotStarted;

    /// <summary>
    /// Set when the candidate marks the skill completed (FR-47). Completion is
    /// reflected in the profile only once confirmed, at which point a ProfileSkill
    /// is created with source RoadmapCompleted — a roadmap item on its own never
    /// affects a match score (BR-06).
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
