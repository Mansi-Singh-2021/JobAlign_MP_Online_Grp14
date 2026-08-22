namespace JobAlign.Core.Extraction;

/// <summary>
/// Result of one extraction attempt. Failure is an expected outcome, not an
/// exception: NFR-06 requires the posting to survive an unavailable AI service,
/// and FR-19 requires the failure reason to be recorded.
/// </summary>
public sealed class ExtractionOutcome
{
    public bool Succeeded { get; private init; }
    public ExtractedPosting? Posting { get; private init; }
    public string? FailureReason { get; private init; }

    public static ExtractionOutcome Success(ExtractedPosting posting) =>
        new() { Succeeded = true, Posting = posting };

    public static ExtractionOutcome Failure(string reason) =>
        new() { Succeeded = false, FailureReason = reason };
}
