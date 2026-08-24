using JobAlign.Core.Enums;

namespace JobAlign.Web.Models.Dashboard;

/// <summary>Candidate overview dashboard (FR-52, FR-54).</summary>
public class DashboardViewModel
{
    public int TotalPostings { get; init; }
    public int PendingCount { get; init; }

    // Counts by ApplicationStatus (FR-52, FR-53)
    public int SavedCount { get; init; }
    public int AppliedCount { get; init; }
    public int InterviewCount { get; init; }
    public int RejectedCount { get; init; }
    public int ClosedCount { get; init; }

    /// <summary>Average overall score across confirmed postings with a non-null score (FR-52, BR-10).</summary>
    public decimal? AverageMatchScore { get; init; }

    /// <summary>Highest overall match score among confirmed postings (FR-52, BR-10).</summary>
    public decimal? BestMatchScore { get; init; }

    /// <summary>Top 5 missing skills from the roadmap (FR-45, FR-52).</summary>
    public IReadOnlyList<RoadmapItemViewModel> TopRoadmapSkills { get; init; } = [];

    /// <summary>Recent postings and their match scores (FR-52).</summary>
    public IReadOnlyList<DashboardPostingItemViewModel> RecentPostings { get; init; } = [];
}

/// <summary>One posting summary on the dashboard (FR-52).</summary>
public class DashboardPostingItemViewModel
{
    public int Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string Preview { get; init; } = string.Empty;
    public string? JobTitle { get; init; }
    public string? CompanyName { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public PostingStatus Status { get; init; }
    public ApplicationStatus ApplicationStatus { get; init; }
    public decimal? OverallScore { get; init; }
}

/// <summary>The candidate learning roadmap page (FR-45, FR-46, FR-47).</summary>
public class RoadmapViewModel
{
    public IReadOnlyList<RoadmapItemViewModel> Items { get; init; } = [];
    public int TotalGaps => Items.Count;
    public int InProgressCount => Items.Count(i => i.Status == RoadmapItemStatus.InProgress);
    public int CompletedCount => Items.Count(i => i.Status == RoadmapItemStatus.Completed);
    public int NotStartedCount => Items.Count(i => i.Status == RoadmapItemStatus.NotStarted);
}

/// <summary>One skill on the learning roadmap (FR-46, FR-47).</summary>
public class RoadmapItemViewModel
{
    public int Id { get; init; }
    public int MasterSkillId { get; init; }
    public string SkillName { get; init; } = string.Empty;
    public int Priority { get; init; }
    public int RequiredOccurrenceCount { get; init; }
    public int PreferredOccurrenceCount { get; init; }
    public RoadmapItemStatus Status { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public bool IsHeldInProfile { get; init; }
}

/// <summary>Explainable match score card with components and skills breakdown (FR-40, FR-42, FR-43).</summary>
public class MatchScoreCardViewModel
{
    public bool HasMatchResult { get; init; }
    public decimal? OverallScore { get; init; }
    public decimal? RequiredSkillScore { get; init; }
    public decimal? PreferredSkillScore { get; init; }
    public decimal? ExperienceScore { get; init; }
    public string? ScoringConfigVersion { get; init; }
    public string? FeedbackText { get; init; }

    public IReadOnlyList<MatchSkillItemViewModel> HeldSkills { get; init; } = [];
    public IReadOnlyList<MatchSkillItemViewModel> MissingRequiredSkills { get; init; } = [];
    public IReadOnlyList<MatchSkillItemViewModel> MissingPreferredSkills { get; init; } = [];
}

/// <summary>Skill item displayed on the score breakdown card (FR-42, FR-43).</summary>
public class MatchSkillItemViewModel
{
    public int MasterSkillId { get; init; }
    public string SkillName { get; init; } = string.Empty;
    public SkillType SkillType { get; init; }
    public ProficiencyLevel? Proficiency { get; init; }
}
