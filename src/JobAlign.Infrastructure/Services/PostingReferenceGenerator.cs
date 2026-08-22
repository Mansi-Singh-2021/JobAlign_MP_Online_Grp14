using System.Security.Cryptography;
using JobAlign.Core.Abstractions;
using JobAlign.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace JobAlign.Infrastructure.Services;

/// <summary>
/// Generates references of the form <c>JA-202608-K7M2QX</c> (FR-09).
/// </summary>
/// <remarks>
/// Random rather than sequential on purpose: a sequential reference leaks how many
/// postings exist and lets one candidate guess another's reference, which works
/// against BR-09. The alphabet omits I, L, O, 0 and 1 so a reference read aloud or
/// copied by hand is unambiguous.
/// </remarks>
public class PostingReferenceGenerator : IPostingReferenceGenerator
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int RandomLength = 6;
    private const int MaxAttempts = 10;

    private readonly JobAlignDbContext _db;

    public PostingReferenceGenerator(JobAlignDbContext db) => _db = db;

    public async Task<string> NextAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var candidate = $"JA-{DateTimeOffset.UtcNow:yyyyMM}-{RandomNumberGenerator.GetString(Alphabet, RandomLength)}";

            var taken = await _db.JobPostings
                .AsNoTracking()
                .AnyAsync(p => p.Reference == candidate, cancellationToken);

            if (!taken)
                return candidate;
        }

        // 31^6 is ~887 million per month; ten collisions in a row means something is
        // wrong with the generator rather than bad luck, so fail loudly.
        throw new InvalidOperationException(
            $"Could not generate a unique posting reference after {MaxAttempts} attempts.");
    }
}
