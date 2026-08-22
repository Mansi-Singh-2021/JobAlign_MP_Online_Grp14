using JobAlign.Core.Entities.Postings;

namespace JobAlign.Core.Abstractions;

/// <summary>
/// Runs extraction for a posting and stores the result (FR-12, FR-19, FR-21).
/// Owner id on every method for the same reason as IJobPostingService (BR-09).
/// </summary>
public interface IExtractionService
{
    /// <summary>
    /// Extracts and stores. Marks the new run current and the previous one not current;
    /// history is retained (NFR-08). On failure, stores a Failed run with the reason and
    /// sets the posting to Pending — the posting itself is never lost (FR-19, NFR-06).
    /// Returns null when the posting does not exist for this owner.
    /// </summary>
    Task<PostingExtraction?> RunAsync(int postingId, int ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>The current run for a posting, or null if never extracted.</summary>
    Task<PostingExtraction?> GetCurrentAsync(int postingId, int ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a candidate's corrections and sets the posting to Confirmed (FR-18, AC-10).
    /// Corrections are written to PostingFieldCorrections against the POSTING, so they
    /// survive re-extraction (BR-03).
    /// </summary>
    Task<bool> ApplyCorrectionsAsync(
        int postingId,
        int ownerUserId,
        IReadOnlyDictionary<string, string?> correctedFields,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Standing corrections for a posting (BR-03). Reading a posting means taking the
    /// current extraction and overlaying these on top, so the review screen needs both.
    /// </summary>
    Task<IReadOnlyList<PostingFieldCorrection>> GetCorrectionsAsync(
        int postingId, int ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The posting's skills with their master skill loaded, for display (FR-13).
    /// Includes user-added rows, not only extracted ones.
    /// </summary>
    Task<IReadOnlyList<PostingSkill>> GetSkillsAsync(
        int postingId, int ownerUserId, CancellationToken cancellationToken = default);
}
