namespace JobAlign.Core.Abstractions;

/// <summary>
/// Produces the unique human-facing reference every saved posting carries (FR-09).
/// Behind an interface because the reference format is a presentation decision that
/// should be replaceable without touching capture logic (NFR-11).
/// </summary>
public interface IPostingReferenceGenerator
{
    /// <summary>
    /// Returns a reference not currently used by any posting. Implementations must
    /// guarantee uniqueness; the database also enforces it via UX_JobPostings_Reference,
    /// so a race produces a constraint violation rather than a duplicate.
    /// </summary>
    Task<string> NextAsync(CancellationToken cancellationToken = default);
}
