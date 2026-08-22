namespace JobAlign.Core.Entities.Postings;

/// <summary>
/// How complete a posting is, and which expected details it never stated
/// (FR-22, FR-23).
///
/// This measures the <b>posting</b>, not the extraction: a posting that omits
/// salary scores lower here, which is a fact about the advert rather than a
/// failure of extraction. Absent details remain "Not specified" (BR-02).
/// </summary>
public class PostingQualityAssessment
{
    public int Id { get; set; }

    public int JobPostingId { get; set; }
    public JobPosting JobPosting { get; set; } = null!;

    /// <summary>Proportion of expected details the posting stated, 0–100 (FR-23).</summary>
    public decimal CompletenessScore { get; set; }

    /// <summary>
    /// Names of the expected details the posting did not state (FR-22),
    /// stored as a JSON array of field names.
    /// </summary>
    public required string MissingFields { get; set; }

    public DateTimeOffset AssessedAt { get; set; }
}
