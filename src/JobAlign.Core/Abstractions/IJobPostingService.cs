using JobAlign.Core.Entities.Postings;

namespace JobAlign.Core.Abstractions;

/// <summary>
/// Capture and management of a candidate's own job postings (FR-06, FR-08 to FR-11).
///
/// Every method takes the owning user's id and filters on it. Ownership is therefore
/// enforced at this boundary rather than in the controller, which is what BR-09 and
/// NFR-04 require — no caller can ask for "posting 7" without also proving whose it is.
/// </summary>
public interface IJobPostingService
{
    /// <summary>
    /// Saves a posting from pasted text (FR-06). Assigns a unique reference and the
    /// initial status <see cref="Enums.PostingStatus.New"/> (FR-09). No AI runs here —
    /// capture and extraction are separate concerns, and NFR-06 requires that a posting
    /// save successfully whether or not extraction is available.
    /// </summary>
    /// <param name="capturedAt">
    /// When the candidate captured it (FR-10). Null means now.
    /// </param>
    Task<JobPosting> CapturePastedTextAsync(
        int ownerUserId,
        string rawText,
        string? sourceName,
        DateTimeOffset? capturedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Postings owned by this user, newest capture first (FR-11, NFR-02).</summary>
    Task<IReadOnlyList<JobPosting>> ListForOwnerAsync(
        int ownerUserId,
        bool includeArchived,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One posting, or null when it does not exist <em>or</em> belongs to someone else.
    /// The two cases are deliberately indistinguishable to the caller so that a wrong
    /// guess cannot confirm another user's posting exists (BR-09).
    /// </summary>
    Task<JobPosting?> GetForOwnerAsync(
        int postingId,
        int ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Archives or restores a posting (FR-11). False when not found for this owner.</summary>
    Task<bool> SetArchivedAsync(
        int postingId,
        int ownerUserId,
        bool isArchived,
        CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a posting and its derived rows (FR-11). False when not found for this owner.</summary>
    Task<bool> DeleteAsync(
        int postingId,
        int ownerUserId,
        CancellationToken cancellationToken = default);
}
