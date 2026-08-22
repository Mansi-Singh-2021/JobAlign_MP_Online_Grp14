using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Postings;
using JobAlign.Core.Enums;
using JobAlign.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace JobAlign.Infrastructure.Services;

/// <inheritdoc cref="IJobPostingService"/>
public class JobPostingService : IJobPostingService
{
    private readonly JobAlignDbContext _db;
    private readonly IPostingReferenceGenerator _references;

    public JobPostingService(JobAlignDbContext db, IPostingReferenceGenerator references)
    {
        _db = db;
        _references = references;
    }

    public async Task<JobPosting> CapturePastedTextAsync(
        int ownerUserId,
        string rawText,
        string? sourceName,
        DateTimeOffset? capturedAt,
        CancellationToken cancellationToken = default)
    {
        // The entity constructor rejects blank text and fixes RawText for the life of
        // the posting (BR-01, FR-08). Nothing here re-reads or rewrites it.
        var reference = await _references.NextAsync(cancellationToken);

        var posting = new JobPosting(ownerUserId, reference, rawText, PostingCaptureMethod.PastedText)
        {
            SourceName = string.IsNullOrWhiteSpace(sourceName) ? null : sourceName.Trim()   // FR-10
        };

        // FR-10 lets the candidate record when they captured it. Left alone, the
        // constructor's UtcNow stands.
        if (capturedAt.HasValue)
            posting.CapturedAt = capturedAt.Value;

        _db.JobPostings.Add(posting);
        await _db.SaveChangesAsync(cancellationToken);

        return posting;
    }

    public async Task<IReadOnlyList<JobPosting>> ListForOwnerAsync(
        int ownerUserId,
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        var query = _db.JobPostings
            .AsNoTracking()
            .Where(p => p.OwnerUserId == ownerUserId);      // BR-09, before anything else

        if (!includeArchived)
            query = query.Where(p => !p.IsArchived);        // FR-11

        return await query
            .OrderByDescending(p => p.CapturedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<JobPosting?> GetForOwnerAsync(
        int postingId,
        int ownerUserId,
        CancellationToken cancellationToken = default) =>
        _db.JobPostings
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == postingId && p.OwnerUserId == ownerUserId, cancellationToken);

    public async Task<bool> SetArchivedAsync(
        int postingId,
        int ownerUserId,
        bool isArchived,
        CancellationToken cancellationToken = default)
    {
        var posting = await _db.JobPostings
            .FirstOrDefaultAsync(p => p.Id == postingId && p.OwnerUserId == ownerUserId, cancellationToken);

        if (posting is null)
            return false;

        posting.IsArchived = isArchived;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(
        int postingId,
        int ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var posting = await _db.JobPostings
            .FirstOrDefaultAsync(p => p.Id == postingId && p.OwnerUserId == ownerUserId, cancellationToken);

        if (posting is null)
            return false;

        // Extractions, corrections, skills, the quality assessment and the match result
        // all cascade from JobPostings. Relations pointing *at* this posting do not:
        // PostingRelations.RelatedJobPostingId is NoAction, because SQL Server rejects
        // two cascade paths into one table. Clear them first or the delete fails.
        var inboundRelations = await _db.PostingRelations
            .Where(r => r.RelatedJobPostingId == postingId)
            .ToListAsync(cancellationToken);

        if (inboundRelations.Count > 0)
            _db.PostingRelations.RemoveRange(inboundRelations);

        _db.JobPostings.Remove(posting);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
