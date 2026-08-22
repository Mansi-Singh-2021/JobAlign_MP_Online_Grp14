namespace JobAlign.Core.Enums;

/// <summary>Self-declared command of a skill (FR-28).</summary>
public enum ProficiencyLevel
{
    Beginner = 0,
    Intermediate = 1,
    Advanced = 2,
    Expert = 3
}

/// <summary>
/// How a skill entered the profile. Everything here is confirmed by the
/// candidate — unconfirmed resume output lives in ResumeSkillSuggestion and
/// never reaches this table until accepted (BR-06, FR-32).
/// </summary>
public enum ProfileSkillSource
{
    /// <summary>Entered directly by the candidate (FR-28).</summary>
    Manual = 0,

    /// <summary>Suggested from a resume and then explicitly confirmed (FR-32).</summary>
    ResumeConfirmed = 1,

    /// <summary>Confirmed as completed from the learning roadmap (FR-47).</summary>
    RoadmapCompleted = 2
}

/// <summary>Progress of resume parsing (FR-30, FR-31).</summary>
public enum ResumeExtractionStatus
{
    /// <summary>Uploaded and stored; parsing not yet run. A resume is always
    /// saved even when the AI service is down (NFR-06).</summary>
    Pending = 0,
    Succeeded = 1,
    Failed = 2
}
