namespace JobAlign.Core.Enums;

/// <summary>Candidate's progress against a roadmap skill (FR-47).</summary>
public enum RoadmapItemStatus
{
    NotStarted = 0,
    InProgress = 1,

    /// <summary>Completed and confirmed; reflected in the profile (FR-47).</summary>
    Completed = 2
}
