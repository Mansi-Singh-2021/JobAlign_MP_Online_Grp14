namespace JobAlign.Core.Enums;

/// <summary>
/// Extraction/confirmation lifecycle of a posting (FR-09, FR-19, AC-10).
/// Deliberately separate from <see cref="ApplicationStatus"/> — the SRS defines
/// two independent status concepts and conflating them would break BR-08.
/// </summary>
public enum PostingStatus
{
    /// <summary>
    /// Captured and not confirmed (FR-09). Covers two situations, so it is not on its own
    /// a statement about whether there is anything to score: a posting that has never been
    /// extracted, and one whose extraction succeeded but which the candidate has not yet
    /// reviewed — a successful run leaves the posting here, it does not advance it.
    /// Re-extracting a Confirmed posting also returns it to New.
    /// Check the current PostingExtraction's RunStatus to tell the two apart.
    /// </summary>
    New = 0,

    /// <summary>Extraction failed or the AI service was unavailable (FR-19).
    /// Excluded from scoring, comparison and dashboard figures (BR-08, FR-54).</summary>
    Pending = 1,

    /// <summary>Candidate has reviewed and confirmed the extracted details (AC-10).</summary>
    Confirmed = 2
}

/// <summary>Where the candidate is in the application process (FR-53).</summary>
public enum ApplicationStatus
{
    Saved = 0,
    Applied = 1,
    Interview = 2,
    Rejected = 3,
    Closed = 4
}

/// <summary>How the posting text reached the system (FR-06, FR-07).</summary>
public enum PostingCaptureMethod
{
    PastedText = 0,
    Link = 1
}

/// <summary>Outcome of a single extraction run (FR-19, FR-21).</summary>
public enum ExtractionRunStatus
{
    Succeeded = 0,
    Failed = 1
}

/// <summary>
/// Work mode stated by the posting. Mirrors the AI extraction contract exactly:
/// remote | hybrid | onsite | unclear. "Unclear" is a valid, correct answer —
/// it is not a failure and must never be coerced to a guess (BR-02, NFR-07).
/// </summary>
public enum RemotePolicy
{
    Remote = 0,
    Hybrid = 1,
    Onsite = 2,
    Unclear = 3
}

/// <summary>
/// Period a stated salary figure refers to. Null (not a member here) means the
/// posting did not state a period — never assume one (BR-02, BR-05).
/// </summary>
public enum SalaryPeriod
{
    Year = 0,
    Month = 1,
    Hour = 2
}

/// <summary>Confidence the extractor reported for one field (FR-20, NFR-06).</summary>
public enum ConfidenceLevel
{
    High = 0,
    Medium = 1,
    Low = 2
}

/// <summary>Why two postings are related (FR-24, FR-26).</summary>
public enum PostingRelationType
{
    /// <summary>Detected as identical or substantially similar (FR-24).</summary>
    SuspectedDuplicate = 0,

    /// <summary>Candidate confirmed the same role advertised through different sources (FR-26).</summary>
    SameRole = 1
}

/// <summary>How the candidate resolved a suspected duplicate (FR-25).</summary>
public enum PostingRelationResolution
{
    Unresolved = 0,
    KeptBoth = 1,
    DiscardedNew = 2,
    LinkedAsSameRole = 3
}
