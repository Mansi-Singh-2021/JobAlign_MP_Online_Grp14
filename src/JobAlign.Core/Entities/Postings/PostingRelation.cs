using JobAlign.Core.Enums;

namespace JobAlign.Core.Entities.Postings;

/// <summary>
/// A link between two of the same candidate's postings: either a suspected
/// duplicate found at capture (FR-24, FR-25) or the same role the candidate
/// confirmed was advertised through different sources (FR-26).
/// </summary>
public class PostingRelation
{
    public int Id { get; set; }

    /// <summary>The newly captured posting.</summary>
    public int JobPostingId { get; set; }
    public JobPosting JobPosting { get; set; } = null!;

    /// <summary>The posting already saved that this one resembles.</summary>
    public int RelatedJobPostingId { get; set; }
    public JobPosting RelatedJobPosting { get; set; } = null!;

    public PostingRelationType RelationType { get; set; }

    /// <summary>Similarity 0–1 that triggered the warning, where detection produced one (FR-24).</summary>
    public decimal? SimilarityScore { get; set; }

    /// <summary>What the candidate chose to do about it (FR-25).</summary>
    public PostingRelationResolution Resolution { get; set; }

    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
