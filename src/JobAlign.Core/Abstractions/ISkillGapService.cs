using JobAlign.Core.Entities.Matching;
using JobAlign.Core.Enums;

namespace JobAlign.Core.Abstractions;

/// <summary>
/// Skill gaps per posting and the roadmap across all of them (FR-42 to FR-47).
/// </summary>
public interface ISkillGapService
{
    /// <summary>
    /// Gaps for one posting, required ones distinguishable from preferred (FR-42, FR-43).
    /// Written by IMatchScoringService; this reads them back with MasterSkill included.
    /// </summary>
    Task<IReadOnlyList<SkillGap>> GetGapsForPostingAsync(
        int postingId, int ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds the roadmap: every skill missing across the candidate's Confirmed
    /// postings, ordered by how often it is missing and whether it is required
    /// (FR-45, FR-46). Replaces existing items but preserves the Status of any skill
    /// the candidate had already marked InProgress or Completed (FR-47).
    /// </summary>
    Task<IReadOnlyList<RoadmapItem>> RebuildRoadmapAsync(
        int ownerUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoadmapItem>> GetRoadmapAsync(
        int ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a roadmap skill in progress or completed (FR-47). Completing an item does
    /// NOT by itself add the skill to the profile — the candidate confirms that
    /// separately, at which point a ProfileSkill with source RoadmapCompleted is
    /// created. A roadmap item alone never moves a match score (BR-06).
    /// </summary>
    Task<bool> SetRoadmapStatusAsync(
        int roadmapItemId, int ownerUserId, RoadmapItemStatus status,
        CancellationToken cancellationToken = default);
}
