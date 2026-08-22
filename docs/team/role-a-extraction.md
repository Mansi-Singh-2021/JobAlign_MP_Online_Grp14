# Role A — Extraction pipeline and review UI

**Story:** US-05a · **Points:** 5 · **Branch:** `feat/a-extraction`
**Critical path:** yes — you and B own Wave 0. Four people are blocked until it lands.

> Read [00-SHARED-BRIEF.md](00-SHARED-BRIEF.md) and [01-CONTRACTS.md](01-CONTRACTS.md) first.

---

## What you own

The machinery that turns a saved posting's raw text into stored structured detail, and the
screen where the candidate reviews and corrects it. **You do not talk to a real AI service**
— that is Member F. You build the pipeline and a stub that fills it, so the entire flow can
be demonstrated and tested without a network call.

## Requirements

| ID | Requirement | Pri |
|---|---|---|
| FR-12 | Extract job title, company, location, work mode, required experience, salary, responsibilities | M |
| FR-13 | Extract stated skills and classify each required or preferred | M |
| FR-17 | Record any detail not stated as "Not specified"; never invent | M |
| FR-18 | Present all extracted detail for review; allow any detail to be corrected | M |
| FR-19 | Where extraction fails, retain the posting and record why | M |
| FR-21 | Allow re-extraction against the stored original text | S |

Business rules in your lane: **BR-01** (raw text untouched), **BR-02** (nothing invented),
**BR-03** (corrections attach to the posting), **BR-09** (owner filtering).
NFRs: **NFR-06** (AI failure never loses a posting), **NFR-08** (config version stored),
**NFR-13** (extract once, store).

## Files you own

Create:
```
src/JobAlign.Core/Extraction/ExtractedPosting.cs
src/JobAlign.Core/Extraction/ExtractionOutcome.cs
src/JobAlign.Core/Abstractions/IJobExtractor.cs
src/JobAlign.Core/Abstractions/IExtractionService.cs
src/JobAlign.Infrastructure/Extraction/StubExtractor.cs
src/JobAlign.Infrastructure/Services/ExtractionService.cs
src/JobAlign.Web/Models/Postings/ExtractionViewModels.cs
src/JobAlign.Web/Views/Postings/Review.cshtml
tests/JobAlign.Tests/ExtractionServiceTests.cs
```

Edit (announce first):
- `src/JobAlign.Web/Controllers/PostingsController.cs` — add `Extract`, `Review`, `Confirm` actions
- `src/JobAlign.Web/Views/Postings/Details.cshtml` — replace the "Not extracted yet" card
- `src/JobAlign.Infrastructure/DependencyInjection.cs` — append two lines

**Do not touch:** anything under `Skills/`, `Profiles/`, `Matching/`; `Views/Postings/Index.cshtml`
(E owns it); `Program.cs` beyond what Wave 0 needs.

---

## Wave 0 — do this first, with Member B, before anything else

1. Create every file in section A of `01-CONTRACTS.md`, copied verbatim.
2. Write `StubExtractor`: returns a fixed, realistic `ExtractedPosting` with
   `ConfigVersion = "stub-v1"`. **Its skills must exist in B's seeded master list** —
   agree the list with B before you hardcode it. Six to eight skills, split across
   `Required` and `Preferred`.
3. Make one field deliberately null (say `CompanyName`) so the "Not specified" path is
   exercised from day one.
4. Register both in `AddJobAlignInfrastructure`.
5. `dotnet build` clean, `dotnet test` green, push, tell the team.

Target: 90 minutes. Everything else you do today is downstream of this.

---

## Task list

### 1. `ExtractionService.RunAsync`

The orchestrator. In order:

1. Load the posting **filtered by `ownerUserId`** — copy the pattern from
   `JobPostingService`. Return null if not found.
2. Call `IJobExtractor.ExtractAsync(posting.RawText, ct)`. Never pass anything else — the
   raw text is the only input (BR-01, NFR-09).
3. **On failure:** create a `PostingExtraction` with `RunStatus = Failed`,
   `FailureReason` set, `ExtractionConfigVersion` from the extractor. Set
   `posting.Status = Pending`. Save and return it. The posting is never lost (FR-19, NFR-06).
4. **On success:** create a `PostingExtraction` with `RunStatus = Succeeded`, mapping every
   field from the DTO. Copy nulls as nulls (BR-02).
5. Mark previous runs `IsCurrent = false` and the new one `true`. Only one current run per
   posting — a filtered unique index enforces this, so get the order right or the save throws.
6. Write `ExtractionFieldConfidence` rows from `Confidences` (FR-20).
7. Resolve `Skills` through `ISkillResolver.ResolveManyAsync` and write `PostingSkill` rows
   with `Source = Extracted`, `MasterSkillId` from the resolution, `RawText` as provenance.
   **Skip unresolved skills** — never invent a `MasterSkill` (BR-04). Log what you skipped.
8. Leave `posting.Status` as `New` on success. It becomes `Confirmed` only when the
   candidate confirms the review (FR-18).

Keep the whole thing in one transaction.

### 2. Re-extraction (FR-21)

`RunAsync` on an already-extracted posting must work. Previous runs stay in the table as
history (NFR-08); the new one becomes current. Delete and rewrite `PostingSkill` rows where
`Source = Extracted`, but **never touch rows where `Source = UserAdded`** (BR-03).

### 3. The review screen (FR-18)

`Views/Postings/Review.cshtml`. Show every extracted field in an editable form. Rules:

- A null field renders as the placeholder **"Not specified"**, never as `0` or blank text
  that looks like data (FR-17, BR-02).
- Show the confidence indicator beside each field; visually flag `Low` (FR-20, NFR-06).
- Show extracted skills grouped into Required and Preferred.
- A "Confirm details" button posts corrections and sets the posting to `Confirmed`.
- A "Re-run extraction" button calls `RunAsync` again.

### 4. Corrections (`ApplyCorrectionsAsync`, BR-03)

This is the rule most easily got wrong. For each field the candidate changed, write a row to
**`PostingFieldCorrections`**, whose foreign key is to **`JobPostings`, not to
`PostingExtractions`**. That is the entire reason the table is shaped that way: deleting and
regenerating extractions must not destroy corrections.

When displaying a posting, a correction **overrides** the extracted value.

Then set `posting.Status = Confirmed` and `posting.ConfirmedAt = UtcNow`.

### 5. Notify the scorer

After a posting becomes `Confirmed`, its match score can be calculated. Call
`IMatchScoringService.ScoreAsync(postingId, ownerUserId, ct)` from your `Confirm` action.
D's stub returns quickly; the real one lands day 2. Wrap it so a scoring failure does not
roll back the confirmation.

---

## Acceptance criteria

- [ ] A candidate can click "Extract" on a saved posting and see structured detail
- [ ] Fields the posting did not state show "Not specified", never `0` or `""`
- [ ] Confidence shows per field; low confidence is visually flagged
- [ ] Skills appear split into Required and Preferred, each resolved to a master skill
- [ ] Correcting a field and confirming persists the correction and sets status `Confirmed`
- [ ] Re-running extraction keeps the correction (BR-03) and keeps the old run as history
- [ ] With the extractor forced to fail, the posting survives, status is `Pending`, and the
      reason is recorded and shown (FR-19)
- [ ] Another user's posting id returns 404, not the posting (BR-09)

## Tests to write

```
ExtractionService_marks_only_the_newest_run_as_current
ExtractionService_stores_a_failed_run_and_sets_the_posting_to_Pending
ExtractionService_maps_unstated_fields_as_null_not_zero
ExtractionService_skips_skills_that_do_not_resolve
ExtractionService_preserves_UserAdded_posting_skills_across_re_extraction
ApplyCorrections_writes_the_correction_against_the_posting_not_the_run
ApplyCorrections_sets_the_posting_to_Confirmed
```

For a fake extractor in tests, implement `IJobExtractor` inline — do not mock what you own.

## Dependencies

| You need | From | Until then |
|---|---|---|
| `ISkillResolver` + seeded skills | B | Wave 0 lands it; agree the stub skill list with B |
| `IMatchScoringService` | D | Wrap the call in try/catch; it is fire-and-forget |
| Real `AiExtractor` | F | Your stub is the fallback. Do not wait for F. |

**You provide to others:** `IJobExtractor` (F implements it), and `Confirmed` postings with
`PostingSkill` rows (D scores them). D cannot test anything real until your pipeline writes
posting skills — so land that before the day-1 checkpoint.

## Demo slice

Paste a posting → Extract → review screen with fields, confidences, skills → correct the
title → Confirm → status becomes Confirmed.
