using JobAlign.Core.Entities.Postings;
using JobAlign.Core.Enums;

namespace JobAlign.Tests;

/// <summary>
/// Invariants the capture flow depends on (FR-06, FR-08, FR-09, BR-01).
/// Pure domain — no database involved.
/// </summary>
public class JobPostingTests
{
    private const string Reference = "JA-202608-K7M2QX";
    private const string RawText = "Senior .NET Developer\nWe are hiring.";

    [Fact]
    public void Constructor_sets_the_initial_status_to_New()
    {
        var posting = new JobPosting(1, Reference, RawText, PostingCaptureMethod.PastedText);

        // FR-09: a saved posting starts as New, awaiting extraction.
        Assert.Equal(PostingStatus.New, posting.Status);
    }

    [Fact]
    public void Constructor_records_the_posting_as_saved_not_applied()
    {
        var posting = new JobPosting(1, Reference, RawText, PostingCaptureMethod.PastedText);

        // The two status concepts are independent (BR-08); capturing a posting says
        // nothing about whether the candidate has applied.
        Assert.Equal(ApplicationStatus.Saved, posting.ApplicationStatus);
    }

    [Fact]
    public void Constructor_stores_the_raw_text_exactly_as_given()
    {
        var messy = "  Senior .NET Developer  \r\n\r\n   Salary: negotiable   ";

        var posting = new JobPosting(1, Reference, messy, PostingCaptureMethod.PastedText);

        // FR-08/BR-01: not trimmed, not normalized, not cleaned up.
        Assert.Equal(messy, posting.RawText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void Constructor_rejects_blank_raw_text(string rawText)
    {
        Assert.Throws<ArgumentException>(() =>
            new JobPosting(1, Reference, rawText, PostingCaptureMethod.PastedText));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_a_blank_reference(string reference)
    {
        Assert.Throws<ArgumentException>(() =>
            new JobPosting(1, reference, RawText, PostingCaptureMethod.PastedText));
    }

    [Fact]
    public void Constructor_captures_the_owner_and_capture_method()
    {
        var posting = new JobPosting(42, Reference, RawText, PostingCaptureMethod.Link);

        Assert.Equal(42, posting.OwnerUserId);       // the basis of BR-09
        Assert.Equal(PostingCaptureMethod.Link, posting.CaptureMethod);
    }

    [Fact]
    public void Constructor_stamps_the_capture_time()
    {
        var before = DateTimeOffset.UtcNow;

        var posting = new JobPosting(1, Reference, RawText, PostingCaptureMethod.PastedText);

        Assert.InRange(posting.CapturedAt, before, DateTimeOffset.UtcNow);   // FR-10
    }

    [Theory]
    [InlineData(nameof(JobPosting.RawText))]
    [InlineData(nameof(JobPosting.Reference))]
    public void Write_once_properties_expose_no_public_setter(string propertyName)
    {
        var property = typeof(JobPosting).GetProperty(propertyName);

        Assert.NotNull(property);

        // BR-01 is enforced by the type, not by convention. If someone adds a public
        // setter to make an edit screen easier, this test is what catches it.
        Assert.False(property!.SetMethod?.IsPublic ?? false);
    }
}
