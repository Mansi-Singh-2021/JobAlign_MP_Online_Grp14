using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Postings;
using JobAlign.Core.Enums;
using JobAlign.Core.Extraction;
using JobAlign.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobAlign.Infrastructure.Services;

/// <inheritdoc cref="IExtractionService"/>
public class ExtractionService : IExtractionService
{
    private readonly JobAlignDbContext _db;
    private readonly IJobExtractor _extractor;
    private readonly ISkillResolver _skills;
    private readonly ILogger<ExtractionService> _logger;

    public ExtractionService(
        JobAlignDbContext db,
        IJobExtractor extractor,
        ISkillResolver skills,
        ILogger<ExtractionService> logger)
    {
        _db = db;
        _extractor = extractor;
        _skills = skills;
        _logger = logger;
    }

    public async Task<PostingExtraction?> RunAsync(
        int postingId,
        int ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var posting = await _db.JobPostings
            .FirstOrDefaultAsync(p => p.Id == postingId && p.OwnerUserId == ownerUserId, cancellationToken);

        if (posting is null)
            return null;                                     // not found, or not theirs (BR-09)

        // RawText is the only thing the extractor ever sees: it is the whole input (BR-01)
        // and the limit of what may leave the system (NFR-09).
        var outcome = await _extractor.ExtractAsync(posting.RawText, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // The filtered unique index UX_PostingExtractions_CurrentPerPosting permits one
        // current run per posting. Clear the old flag and save before inserting the new
        // row — a single SaveChanges gives no ordering guarantee and can trip the index.
        await ClearCurrentFlagAsync(postingId, cancellationToken);

        var extraction = outcome.Succeeded
            ? BuildSucceededRun(posting, outcome.Posting!)
            : BuildFailedRun(posting, outcome.FailureReason);

        _db.PostingExtractions.Add(extraction);
        await _db.SaveChangesAsync(cancellationToken);

        if (outcome.Succeeded)
        {
            AddConfidences(extraction, outcome.Posting!);
            await ReplaceExtractedSkillsAsync(posting, outcome.Posting!, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Extraction {Status} for posting {Reference} using {ConfigVersion}.",
            extraction.RunStatus, posting.Reference, extraction.ExtractionConfigVersion);

        return extraction;
    }

    public Task<PostingExtraction?> GetCurrentAsync(
        int postingId,
        int ownerUserId,
        CancellationToken cancellationToken = default) =>
        _db.PostingExtractions
            .AsNoTracking()
            .Include(e => e.FieldConfidences)
            .FirstOrDefaultAsync(
                e => e.JobPostingId == postingId
                     && e.IsCurrent
                     && e.JobPosting.OwnerUserId == ownerUserId,
                cancellationToken);

    public async Task<IReadOnlyList<PostingFieldCorrection>> GetCorrectionsAsync(
        int postingId,
        int ownerUserId,
        CancellationToken cancellationToken = default) =>
        await _db.PostingFieldCorrections
            .AsNoTracking()
            .Where(c => c.JobPostingId == postingId && c.JobPosting.OwnerUserId == ownerUserId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PostingSkill>> GetSkillsAsync(
        int postingId,
        int ownerUserId,
        CancellationToken cancellationToken = default) =>
        await _db.PostingSkills
            .AsNoTracking()
            .Include(s => s.MasterSkill)
            .Where(s => s.JobPostingId == postingId && s.JobPosting.OwnerUserId == ownerUserId)
            .OrderBy(s => s.MasterSkill.Name)
            .ToListAsync(cancellationToken);

    public async Task<bool> ApplyCorrectionsAsync(
        int postingId,
        int ownerUserId,
        IReadOnlyDictionary<string, string?> correctedFields,
        CancellationToken cancellationToken = default)
    {
        var posting = await _db.JobPostings
            .Include(p => p.Corrections)
            .FirstOrDefaultAsync(p => p.Id == postingId && p.OwnerUserId == ownerUserId, cancellationToken);

        if (posting is null)
            return false;

        // Compared against, so that submitting the form unchanged records nothing. Without
        // this every confirmation would create a correction for every field, and BR-03
        // would start protecting values the candidate never typed.
        var current = await _db.PostingExtractions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.JobPostingId == postingId && e.IsCurrent, cancellationToken);

        foreach (var (fieldName, correctedValue) in correctedFields)
        {
            // CorrectableFields is the whitelist. An unknown field name here would mean a
            // correction row naming a column that does not exist.
            if (!CorrectableFields.All.Contains(fieldName))
            {
                _logger.LogWarning("Ignored correction to unknown field {FieldName}.", fieldName);
                continue;
            }

            var extractedValue = current is null ? null : ExtractionFields.ReadAsText(current, fieldName);
            var existing = posting.Corrections.FirstOrDefault(c => c.FieldName == fieldName);

            if (ExtractionFields.IsSameValue(extractedValue, correctedValue))
            {
                // Back to what was extracted — the correction no longer says anything, so
                // drop it rather than leaving a row that overrides with an identical value.
                if (existing is not null)
                    _db.PostingFieldCorrections.Remove(existing);

                continue;
            }

            if (existing is not null)
            {
                existing.CorrectedValue = correctedValue;
                existing.CorrectedAt = DateTimeOffset.UtcNow;
                existing.CorrectedByUserId = ownerUserId;
                continue;
            }

            // The foreign key is to JobPostings, not to PostingExtractions. That is what
            // makes a correction survive re-extraction (BR-03) — do not "tidy" it onto
            // the extraction row.
            posting.Corrections.Add(new PostingFieldCorrection
            {
                JobPostingId = posting.Id,
                FieldName = fieldName,
                CorrectedValue = correctedValue,
                CorrectedAt = DateTimeOffset.UtcNow,
                CorrectedByUserId = ownerUserId
            });
        }

        posting.Status = PostingStatus.Confirmed;            // AC-10
        posting.ConfirmedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ----------------------------------------------------------------

    private async Task ClearCurrentFlagAsync(int postingId, CancellationToken cancellationToken)
    {
        var current = await _db.PostingExtractions
            .Where(e => e.JobPostingId == postingId && e.IsCurrent)
            .ToListAsync(cancellationToken);

        if (current.Count == 0)
            return;

        // Superseded runs are kept, not deleted: NFR-08 requires a stored result to remain
        // reproducible against the configuration that produced it.
        foreach (var run in current)
            run.IsCurrent = false;

        await _db.SaveChangesAsync(cancellationToken);
    }

    private PostingExtraction BuildSucceededRun(JobPosting posting, ExtractedPosting extracted)
    {
        // Every field copied straight across, nulls included. A null here means the posting
        // did not state the detail and must render as "Not specified" — never coerced to
        // zero or an empty string (BR-02, FR-17, NFR-07).
        posting.Status = PostingStatus.New;

        return new PostingExtraction
        {
            JobPostingId = posting.Id,
            IsCurrent = true,
            RunStatus = ExtractionRunStatus.Succeeded,
            ExtractedAt = DateTimeOffset.UtcNow,
            ExtractionConfigVersion = _extractor.ConfigVersion,

            JobTitle = extracted.JobTitle,
            CompanyName = extracted.CompanyName,
            RawLocationText = extracted.RawLocationText,
            RemotePolicy = extracted.RemotePolicy,
            ExperienceMinYears = extracted.ExperienceMinYears,
            ExperienceMaxYears = extracted.ExperienceMaxYears,
            Responsibilities = extracted.Responsibilities,
            Summary = extracted.Summary,

            SalaryMinRaw = extracted.SalaryMinRaw,
            SalaryMaxRaw = extracted.SalaryMaxRaw,
            SalaryCurrencyRaw = extracted.SalaryCurrencyRaw,
            SalaryPeriodRaw = extracted.SalaryPeriodRaw

            // SalaryMinYearly / SalaryMaxYearly / SalaryCurrencyNormalized and LocationId
            // are left null here. Normalization is a separate step (FR-15, FR-16) and
            // guessing a conversion rate would be inventing a fact (BR-05, BR-02).
        };
    }

    private PostingExtraction BuildFailedRun(JobPosting posting, string? failureReason)
    {
        // NFR-06: the posting survives an unavailable extractor. FR-19: it becomes Pending
        // and the reason is recorded, so the candidate can see why and retry.
        posting.Status = PostingStatus.Pending;

        return new PostingExtraction
        {
            JobPostingId = posting.Id,
            IsCurrent = true,
            RunStatus = ExtractionRunStatus.Failed,
            ExtractedAt = DateTimeOffset.UtcNow,
            ExtractionConfigVersion = _extractor.ConfigVersion,
            FailureReason = Truncate(failureReason ?? "Extraction failed for an unspecified reason.", 1024)
        };
    }

    private void AddConfidences(PostingExtraction extraction, ExtractedPosting extracted)
    {
        foreach (var confidence in extracted.Confidences)
        {
            if (string.IsNullOrWhiteSpace(confidence.FieldName))
                continue;

            _db.ExtractionFieldConfidences.Add(new ExtractionFieldConfidence
            {
                PostingExtractionId = extraction.Id,
                FieldName = confidence.FieldName,
                Confidence = confidence.Confidence,
                Score = confidence.Score
            });
        }
    }

    /// <summary>
    /// Rewrites the posting's extracted skills. Rows the candidate added by hand are left
    /// alone — a re-extraction must not discard the candidate's own work (BR-03).
    /// </summary>
    private async Task ReplaceExtractedSkillsAsync(
        JobPosting posting,
        ExtractedPosting extracted,
        CancellationToken cancellationToken)
    {
        var existing = await _db.PostingSkills
            .Where(s => s.JobPostingId == posting.Id)
            .ToListAsync(cancellationToken);

        _db.PostingSkills.RemoveRange(existing.Where(s => s.Source == PostingSkillSource.Extracted));

        var keptByUser = existing
            .Where(s => s.Source == PostingSkillSource.UserAdded)
            .Select(s => s.MasterSkillId)
            .ToHashSet();

        if (extracted.Skills.Count == 0)
            return;

        var resolutions = await _skills.ResolveManyAsync(
            extracted.Skills.Select(s => s.RawText), cancellationToken);

        // Resolutions come back in input order, so they pair up with the extracted skills.
        var pairs = extracted.Skills.Zip(resolutions, (skill, resolution) => (skill, resolution));

        var seen = new HashSet<int>(keptByUser);
        var unresolved = new List<string>();

        foreach (var (skill, resolution) in pairs)
        {
            if (!resolution.IsResolved)
            {
                // Never create a MasterSkill from extracted text — that would let the
                // extractor define the master list, which is what BR-04 forbids. An
                // administrator adds it (FR-57) and re-extraction picks it up.
                unresolved.Add(skill.RawText);
                continue;
            }

            // UX_PostingSkills_Posting_Skill is unique on (posting, master skill), so a
            // skill named twice — or already held as a user-added row — is skipped rather
            // than inserted a second time.
            if (!seen.Add(resolution.MasterSkillId!.Value))
                continue;

            _db.PostingSkills.Add(new PostingSkill
            {
                JobPostingId = posting.Id,
                MasterSkillId = resolution.MasterSkillId.Value,
                SkillType = skill.SkillType,
                RawText = skill.RawText,                     // provenance only, never identity
                Source = PostingSkillSource.Extracted
            });
        }

        if (unresolved.Count > 0)
        {
            _logger.LogInformation(
                "Posting {Reference}: {Count} extracted skill(s) not in the master list and skipped: {Skills}.",
                posting.Reference, unresolved.Count, string.Join(", ", unresolved));
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
