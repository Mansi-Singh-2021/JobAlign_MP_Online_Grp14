# Contracts — copy these verbatim

These interfaces land on `main` in **Wave 0**, before any feature work starts. Members A
and B write them; everyone else codes against them.

**Copy them exactly.** Do not rename a method, reorder a parameter, or "improve" a
signature. Six people are compiling against these. A paraphrase is a merge conflict.

If a contract turns out to be genuinely wrong, the person who finds it raises it in the
group chat, the lead decides, and the change is announced. Nobody changes a shared
interface unilaterally.

All of these live in `src/JobAlign.Core/Abstractions/`, alongside the existing
`IJobPostingService`, `IPostingReferenceGenerator`, `ICandidateRegistrationService` and
`IAppEmailSender`. Implementations live in `src/JobAlign.Infrastructure/`.

---

## A. Extraction — owner: Member A

### `Core/Extraction/ExtractedPosting.cs`

The DTO the extractor returns. **Never bound to EF directly** (rule 6). Every field is
nullable because a posting that does not state something yields `null`, not a guess (BR-02).

```csharp
using JobAlign.Core.Enums;

namespace JobAlign.Core.Extraction;

/// <summary>
/// Structured detail read out of one posting's raw text (FR-12, FR-13).
/// A plain DTO: validated and mapped onto PostingExtraction by the caller,
/// never handed to EF directly. Untrusted input (shared brief, rule 6).
/// </summary>
public sealed class ExtractedPosting
{
    public string? JobTitle { get; init; }
    public string? CompanyName { get; init; }

    /// <summary>Location exactly as stated. Normalization to a LocationId is a separate step (FR-16).</summary>
    public string? RawLocationText { get; init; }

    /// <summary>Unclear is a valid answer and is NOT the same as null (null = the posting said nothing).</summary>
    public RemotePolicy? RemotePolicy { get; init; }

    public decimal? ExperienceMinYears { get; init; }
    public decimal? ExperienceMaxYears { get; init; }

    public decimal? SalaryMinRaw { get; init; }
    public decimal? SalaryMaxRaw { get; init; }
    public string? SalaryCurrencyRaw { get; init; }
    public SalaryPeriod? SalaryPeriodRaw { get; init; }

    public string? Responsibilities { get; init; }

    /// <summary>Short summary of a long description (FR-48). Null unless the provider produced one.</summary>
    public string? Summary { get; init; }

    public IReadOnlyList<ExtractedSkill> Skills { get; init; } = [];
    public IReadOnlyList<ExtractedFieldConfidence> Confidences { get; init; } = [];
}

/// <summary>One skill as the posting worded it, before resolution to a MasterSkill (FR-13, BR-04).</summary>
public sealed record ExtractedSkill(string RawText, SkillType SkillType, ConfidenceLevel? Confidence);

/// <summary>Confidence for one named field of ExtractedPosting, e.g. "JobTitle" (FR-20).</summary>
public sealed record ExtractedFieldConfidence(string FieldName, ConfidenceLevel Confidence, decimal? Score);
```

### `Core/Extraction/ExtractionOutcome.cs`

```csharp
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
```

### `Core/Abstractions/IJobExtractor.cs`

```csharp
using JobAlign.Core.Extraction;

namespace JobAlign.Core.Abstractions;

/// <summary>
/// Reads structured detail out of a posting's raw text (FR-12, FR-13).
/// Behind an interface so the AI provider can be replaced without touching the
/// application (NFR-11), and so the whole review flow can be built and tested
/// against a stub.
/// </summary>
public interface IJobExtractor
{
    /// <summary>
    /// Identifies the prompt/model configuration, stored on every run so a result
    /// can be reproduced and explained later (NFR-08). For example "stub-v1".
    /// </summary>
    string ConfigVersion { get; }

    /// <summary>Never throws for a provider failure — returns ExtractionOutcome.Failure instead.</summary>
    Task<ExtractionOutcome> ExtractAsync(string rawText, CancellationToken cancellationToken = default);
}
```

### `Core/Abstractions/IExtractionService.cs`

The orchestrator: runs the extractor, resolves skills, persists the run.

```csharp
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
```

---

## B. Skill resolution — owner: Member B

### `Core/Abstractions/ISkillResolver.cs`

```csharp
namespace JobAlign.Core.Abstractions;

/// <summary>
/// Resolves a free-text skill name to exactly one master skill (FR-14, FR-29, BR-04).
/// "C#", "C Sharp" and "C-Sharp" must all return the same MasterSkillId.
///
/// Used identically by posting skills, profile skills and resume skills — the rule
/// is the same everywhere, so there is one implementation.
/// </summary>
public interface ISkillResolver
{
    Task<SkillResolution> ResolveAsync(string rawSkillText, CancellationToken cancellationToken = default);

    /// <summary>Batch form. One database round trip, not N — extraction resolves a dozen at a time.</summary>
    Task<IReadOnlyList<SkillResolution>> ResolveManyAsync(
        IEnumerable<string> rawSkillTexts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The lookup form of a name: lowercased, punctuation and whitespace stripped.
    /// "C#" -> "csharp", "ASP .NET Core" -> "aspnetcore". Public because MasterSkill
    /// and SkillAlias rows must be written with exactly this normalization applied.
    /// </summary>
    string Normalize(string rawSkillText);
}

/// <summary>
/// Outcome of resolving one skill name. Unresolved is a normal result, not an error:
/// a posting may name a skill the master list does not carry yet. The caller decides
/// whether to skip it or raise it for an administrator (FR-57).
/// </summary>
/// <param name="RawText">Exactly what was supplied, kept as provenance (BR-04).</param>
/// <param name="MasterSkillId">Null when unresolved.</param>
/// <param name="CanonicalName">The approved name, e.g. "C#". Null when unresolved.</param>
public sealed record SkillResolution(string RawText, int? MasterSkillId, string? CanonicalName)
{
    public bool IsResolved => MasterSkillId.HasValue;
}
```

Resolution order: exact `NormalizedName` on an active `MasterSkill`, then
`NormalizedAlias` on `SkillAlias`, then follow `MergedIntoMasterSkillId` if the hit was
merged (FR-58). No fuzzy matching in the MVP.

---

## C. Candidate profile — owner: Member C

### `Core/Abstractions/ICandidateProfileService.cs`

```csharp
using JobAlign.Core.Entities.Profiles;
using JobAlign.Core.Enums;

namespace JobAlign.Core.Abstractions;

/// <summary>
/// The signed-in candidate's own profile (FR-27, FR-28, FR-33, FR-34).
/// User id on every method — a profile is visible only to its owner (BR-09).
/// A profile row is created at registration, so GetAsync never returns null for a candidate.
/// </summary>
public interface ICandidateProfileService
{
    Task<CandidateProfile?> GetAsync(int userId, CancellationToken cancellationToken = default);

    Task UpdateDetailsAsync(int userId, ProfileDetails details, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a skill, resolving it through ISkillResolver first (FR-28, FR-29, BR-04).
    /// Returns the resolution so the caller can tell the candidate their skill was
    /// not recognised. Adding a skill already held updates its proficiency.
    /// </summary>
    Task<SkillResolution> AddSkillAsync(
        int userId, string rawSkillText, ProficiencyLevel level,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveSkillAsync(int userId, int profileSkillId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes CandidateProfile.TotalExperienceYears from the work-experience entries
    /// (FR-33). Null when nothing is recorded — which is not the same as zero years (BR-02).
    /// Call after any change to work experience.
    /// </summary>
    Task RecalculateTotalExperienceAsync(int userId, CancellationToken cancellationToken = default);
}

/// <summary>Editable profile header fields (FR-27).</summary>
public sealed record ProfileDetails(
    string? FullName,
    string? Headline,
    string? CurrentRole,
    string? PhoneNumber);
```

---

## D. Match scoring — owner: Member D

### `Core/Abstractions/IMatchScoringService.cs`

```csharp
using JobAlign.Core.Entities.Matching;

namespace JobAlign.Core.Abstractions;

/// <summary>
/// Scores a candidate profile against postings (FR-35 to FR-41).
///
/// Every posting is scored EXCEPT those with status Pending, which are excluded from
/// scoring, comparison and dashboard figures (BR-08, FR-54).
///
/// Note New is scored, not skipped. A posting that has been extracted but not yet
/// confirmed still has skills worth measuring, and re-extraction resets a Confirmed
/// posting to New — skipping New would silently drop it from the dashboard.
/// A posting with no extraction yet has no skills, so its scores come out null, which
/// is the correct "not measurable" answer rather than a special case (BR-02).
/// </summary>
public interface IMatchScoringService
{
    /// <summary>
    /// Scores one posting and stores the MatchResult, replacing any previous one.
    /// Also writes the SkillGap rows for that result (FR-42, FR-43).
    /// Returns null when the posting is not this owner's, or its status is Pending.
    /// </summary>
    Task<MatchResult?> ScoreAsync(int postingId, int ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rescores every non-Pending posting for this candidate (FR-41). Called whenever the
    /// profile changes. Returns how many postings were rescored. Must complete within
    /// 30 seconds for a realistic library (NFR-03) — one pass over the data, not N+1.
    /// </summary>
    Task<int> RecalculateAllAsync(int ownerUserId, CancellationToken cancellationToken = default);
}
```

### `Core/Matching/ScoringWeights.cs`

```csharp
namespace JobAlign.Core.Matching;

/// <summary>
/// The weightings behind the overall score (FR-39, BR-07). Held in one place and
/// versioned, so a stored score can be explained against the rules that produced it
/// (NFR-08) — MatchResult.ScoringConfigVersion records which version was used.
/// </summary>
public static class ScoringWeights
{
    public const string Version = "weights-v1";

    /// <summary>Required skills weigh more than preferred — this is BR-07, not a preference.</summary>
    public const decimal Required = 0.60m;
    public const decimal Preferred = 0.15m;
    public const decimal Experience = 0.25m;
}
```

**Scoring rules — read these carefully, they are where BR-02 is easiest to break:**

- Scores are **0–100 decimals**, not 0–1.
- `RequiredSkillScore` = held required skills / total required skills × 100.
  **Null** when the posting lists no required skills.
- `PreferredSkillScore` — same, for preferred. **Null** when none listed.
- `ExperienceScore` — candidate `TotalExperienceYears` against the posting's
  `ExperienceMinYears`. Meets or exceeds = 100; otherwise proportional.
  **Null** when either side is null.
- `OverallScore` — weighted mean of whichever components are **not null**, with the
  weights renormalized over those present. **Null** only when all three are null.
- Never substitute 0 for a null component. A missing measurement is not a score of zero.

---

## E. Skill gaps and roadmap — owner: Member E

### `Core/Abstractions/ISkillGapService.cs`

```csharp
using JobAlign.Core.Entities.Matching;
using JobAlign.Core.Enums;

namespace JobAlign.Core.Abstractions;

/// <summary>
/// Skill gaps per posting and the roadmap across all of them (FR-42 to FR-47).
/// </summary>
public interface ISkillGapService
{
    /// <summary>
    /// Gaps for one posting, required ones distinguishable from preferred (FR-42, FR-43).
    /// Written by IMatchScoringService; this reads them back with MasterSkill included.
    /// </summary>
    Task<IReadOnlyList<SkillGap>> GetGapsForPostingAsync(
        int postingId, int ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds the roadmap: every skill missing across the candidate's Confirmed
    /// postings, ordered by how often it is missing and whether it is required
    /// (FR-45, FR-46). Replaces existing items but preserves the Status of any skill
    /// the candidate had already marked InProgress or Completed (FR-47).
    /// </summary>
    Task<IReadOnlyList<RoadmapItem>> RebuildRoadmapAsync(
        int ownerUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoadmapItem>> GetRoadmapAsync(
        int ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a roadmap skill in progress or completed (FR-47). Completing an item does
    /// NOT by itself add the skill to the profile — the candidate confirms that
    /// separately, at which point a ProfileSkill with source RoadmapCompleted is
    /// created. A roadmap item alone never moves a match score (BR-06).
    /// </summary>
    Task<bool> SetRoadmapStatusAsync(
        int roadmapItemId, int ownerUserId, RoadmapItemStatus status,
        CancellationToken cancellationToken = default);
}
```

Roadmap ordering (FR-46): sort by `RequiredOccurrenceCount` descending, then
`PreferredOccurrenceCount` descending, then skill name. `Priority` is 1-based rank.

---

## F. AI services — owner: Member F

### `Core/Abstractions/IFeedbackGenerator.cs`

```csharp
namespace JobAlign.Core.Abstractions;

/// <summary>
/// Written strengths-and-gaps feedback for one scored posting (FR-44).
/// Generated once and stored on MatchResult.FeedbackText — viewing a posting must
/// never trigger a fresh AI call (NFR-13).
/// </summary>
public interface IFeedbackGenerator
{
    /// <summary>Returns null when the provider is unavailable. Never throws for a provider failure.</summary>
    Task<string?> GenerateAsync(FeedbackRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Only what the model needs to write the feedback. Deliberately not the whole posting
/// or profile — NFR-09 limits what is sent to the AI service to the content required.
/// </summary>
public sealed record FeedbackRequest(
    string? JobTitle,
    decimal? OverallScore,
    IReadOnlyList<string> MatchedSkills,
    IReadOnlyList<string> MissingRequiredSkills,
    IReadOnlyList<string> MissingPreferredSkills);
```

`IJobExtractor` (section A) is the other contract F implements — as `AiExtractor`,
registered in place of `StubExtractor`.

---

## Wave 0 stubs

Land these with the interfaces so nobody is blocked:

| Interface | Wave 0 stub | Real implementation |
|---|---|---|
| `IJobExtractor` | `StubExtractor` — fixed realistic `ExtractedPosting`, `ConfigVersion = "stub-v1"` | F, day 2 |
| `ISkillResolver` | `SkillResolver` — exact + alias lookup, real from the start | B refines |
| `IFeedbackGenerator` | `StubFeedbackGenerator` — canned paragraph | F, day 2 |
| `IExtractionService` | real; it only orchestrates | A |
| `ICandidateProfileService` | real | C |
| `IMatchScoringService` | real | D |
| `ISkillGapService` | real | E |

`StubExtractor` must return something a demo can survive on: a plausible title, company,
location, remote policy, salary range, and six to eight skills split across
`Required`/`Preferred` that **exist in the seeded master skill list**. If the stub returns
skills B has not seeded, D's scoring has nothing to match and four people debug a
non-problem.

## DI registration

Everyone appends one line to `AddJobAlignInfrastructure` in
`src/JobAlign.Infrastructure/DependencyInjection.cs`. **Append at the end of the existing
block; never reorder.** That keeps the merge to one line each.

```csharp
services.AddScoped<IJobExtractor, StubExtractor>();              // A — F swaps to AiExtractor
services.AddScoped<IExtractionService, ExtractionService>();     // A
services.AddScoped<ISkillResolver, SkillResolver>();             // B
services.AddScoped<ICandidateProfileService, CandidateProfileService>();  // C
services.AddScoped<IMatchScoringService, MatchScoringService>(); // D
services.AddScoped<ISkillGapService, SkillGapService>();         // E
services.AddScoped<IFeedbackGenerator, StubFeedbackGenerator>(); // F swaps to AiFeedbackGenerator
```
