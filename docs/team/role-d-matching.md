# Role D — Match scoring engine

**Story:** US-06 · **Points:** 8 · **Branch:** `feat/d-match-scoring`

> Read [00-SHARED-BRIEF.md](00-SHARED-BRIEF.md) and [01-CONTRACTS.md](01-CONTRACTS.md) first.

---

## What you own

The four scores that are the entire point of JobAlign. Yours is the least visual slice and
the most algorithmic — almost all of it is testable without a browser, so **write tests
first and lean on them.**

You also write the `SkillGap` rows that E reads. Your output is E's input.

## Requirements

| ID | Requirement | Pri |
|---|---|---|
| FR-35 | Compare profile skills against each posting's required and preferred skills | M |
| FR-36 | Required-skill score — proportion of required skills the candidate holds | M |
| FR-37 | Preferred-skill score — proportion of preferred skills held | M |
| FR-38 | Experience score — candidate's total experience against the posting's requirement | M |
| FR-39 | Overall score from the three components | M |
| FR-40 | Display component scores alongside the overall, so the result can be explained | S |
| FR-41 | Recalculate all scores whenever the profile changes | M |

Business rules: **BR-02** (never invent), **BR-07** (required weighs more than preferred),
**BR-08** (Pending postings are never scored), **BR-10** (unmeasurable is excluded, not zero).
NFR: **NFR-03** (recalculate all within 30 seconds), **NFR-08** (store the weights version).

## Files you own

Create:
```
src/JobAlign.Core/Abstractions/IMatchScoringService.cs
src/JobAlign.Core/Matching/ScoringWeights.cs
src/JobAlign.Core/Matching/ScoreCalculator.cs
src/JobAlign.Infrastructure/Services/MatchScoringService.cs
tests/JobAlign.Tests/ScoreCalculatorTests.cs
tests/JobAlign.Tests/MatchScoringServiceTests.cs
```

Edit (announce first):
- `src/JobAlign.Infrastructure/DependencyInjection.cs` — append one line

**Do not touch:** views, controllers, extraction, profile or skill-admin code. E displays
your scores; A and C call your service. You write no UI at all.

---

## The one thing to get right

**Null is not zero.** Every component score on `MatchResult` is nullable, and that is
deliberate:

> A proportion is undefined when the posting states nothing to measure. A posting listing no
> preferred skills has **no** preferred-skill score. Storing `0` would say "the candidate
> matches none of the preferred skills", which is inventing a fact the posting never
> supported (BR-02, NFR-07).

An AI assistant will want to "simplify" this by defaulting to zero. Do not let it. If you
read one line of this handout twice, make it this one.

---

## Task list

### 1. `ScoreCalculator` — pure, no database

Put the arithmetic in a static class in **Core**, taking plain arguments and returning plain
values. No `DbContext`, no entities. This makes it exhaustively testable and is where almost
all your tests will live.

```csharp
public static decimal? RequiredSkillScore(int requiredCount, int heldCount);
public static decimal? PreferredSkillScore(int preferredCount, int heldCount);
public static decimal? ExperienceScore(decimal? candidateYears, decimal? postingMinYears);
public static decimal? OverallScore(decimal? required, decimal? preferred, decimal? experience);
```

Rules:

| Score | Formula | Null when |
|---|---|---|
| Required | `held / total * 100` | the posting lists **no** required skills |
| Preferred | `held / total * 100` | the posting lists **no** preferred skills |
| Experience | `100` if candidate >= required; else `candidate / required * 100` | either side is null |
| Overall | weighted mean of the **non-null** components, weights renormalized | all three are null |

Scores are **0–100 decimals**, not 0–1. Round to 2 decimal places at the end, not midway.

**Overall with renormalization** — this is the subtle part. Weights are Required `0.60`,
Preferred `0.15`, Experience `0.25` (BR-07: required outweighs preferred). If a component is
null, drop it and rescale the rest so they still sum to 1:

> Required 80, Preferred null, Experience 100
> -> present weights 0.60 and 0.25, sum 0.85
> -> `(80 x 0.60 + 100 x 0.25) / 0.85` = **85.88**

Not `(80 x 0.60 + 0 x 0.15 + 100 x 0.25) / 1.00` = 73.00 — that silently penalises the
candidate for something the posting never asked for.

### 2. `MatchScoringService.ScoreAsync`

1. Load the posting filtered by `ownerUserId` (BR-09). Return null if not found.
2. **Return null if `posting.Status != Confirmed`.** Pending postings are never scored
   (BR-08, FR-54). `New` postings have no extracted skills yet, so there is nothing to score.
3. Load the candidate's `CandidateProfile` with its `ProfileSkill` rows.
4. Load the posting's `PostingSkill` rows, split by `SkillType`.
5. Compare on **`MasterSkillId`**, never on name strings. That is the whole reason B's
   resolver exists (BR-04). A string comparison here would work in your tests and fail in
   the demo.
6. Compute the four scores via `ScoreCalculator`.
7. Upsert the `MatchResult`: one per posting. Set `ScoringConfigVersion =
   ScoringWeights.Version` and `CalculatedAt` (NFR-08).
8. Replace the `SkillGap` rows: one per posting skill the candidate does **not** hold, with
   its `SkillType` carried over so E can distinguish missing required from missing preferred
   (FR-42, FR-43).
9. **Do not touch `FeedbackText`** — that is F's field. An upsert that blanks it will wipe
   F's work.

### 3. `RecalculateAllAsync` (FR-41, NFR-03)

Called whenever the profile changes. Rescore every `Confirmed` posting for that candidate.

**Do not loop calling `ScoreAsync`** — that is N+1 queries and will miss the 30-second
budget. Load the profile skills once into a `HashSet<int>` of master skill ids, load all
confirmed postings with their skills in one query, compute in memory, then write.

Return the number of postings rescored.

---

## Worked example — use this as your first test

Posting requires: C#, ASP.NET Core, SQL Server, REST API, Docker, Azure
Posting prefers: Kubernetes, Terraform
Candidate holds: C#, ASP.NET Core, SQL Server, REST API
Posting wants 5 years; candidate has 3.

```
Required  = 4/6 x 100 = 66.67
Preferred = 0/2 x 100 = 0.00     (measurable: the posting DID list preferred skills)
Experience= 3/5 x 100 = 60.00
Overall   = 66.67x0.60 + 0.00x0.15 + 60.00x0.25 = 54.00
Gaps      = Docker (Required), Azure (Required), Kubernetes (Preferred), Terraform (Preferred)
```

Note `Preferred = 0.00`, not null — the posting listed preferred skills and the candidate
holds none of them. That is a real measurement of zero. **Null is only for "nothing to
measure".** Getting this distinction right in both directions is the core of your slice.

---

## Acceptance criteria

- [ ] All four scores are stored on `MatchResult`, not just the overall
- [ ] A posting with no preferred skills yields `PreferredSkillScore = null`, and the overall
      is renormalized over the remaining components
- [ ] A posting whose preferred skills the candidate wholly lacks yields `0.00`, not null
- [ ] A profile with null `TotalExperienceYears` yields `ExperienceScore = null`
- [ ] A `Pending` posting is not scored at all (BR-08)
- [ ] Skills are matched on `MasterSkillId`, and "c sharp" in a profile matches "C#" in a posting
- [ ] `SkillGap` rows are written with the right `SkillType`
- [ ] Changing the profile rescores every confirmed posting
- [ ] Rescoring 50 postings issues a constant number of queries, not 50
- [ ] `ScoringConfigVersion` is stored on every result
- [ ] Rescoring does not blank `FeedbackText`

## Tests to write

`ScoreCalculatorTests` — pure, fast, exhaustive. This is the bulk of your work:

```
RequiredScore_is_null_when_the_posting_lists_no_required_skills
RequiredScore_is_zero_when_the_candidate_holds_none_of_them
RequiredScore_is_the_proportion_held
PreferredScore_is_null_when_the_posting_lists_none
ExperienceScore_is_null_when_the_candidate_total_is_null
ExperienceScore_is_null_when_the_posting_states_no_requirement
ExperienceScore_is_100_when_the_candidate_meets_or_exceeds
ExperienceScore_is_proportional_when_short
OverallScore_renormalizes_over_present_components
OverallScore_weights_required_above_preferred
OverallScore_is_null_only_when_every_component_is_null
OverallScore_never_treats_a_null_component_as_zero
```

`MatchScoringServiceTests`:

```
ScoreAsync_refuses_a_Pending_posting
ScoreAsync_refuses_another_users_posting
ScoreAsync_writes_gaps_with_the_correct_skill_type
ScoreAsync_preserves_existing_FeedbackText
RecalculateAll_rescores_every_confirmed_posting
```

## Dependencies

| You need | From | Until then |
|---|---|---|
| `PostingSkill` rows on confirmed postings | A | **`ScoreCalculator` needs none of this** — build and test it first |
| `ProfileSkill` rows and `TotalExperienceYears` | C | same |
| Seeded master skills | B (Wave 0) | same |

You have the best-insulated slice on the team: the calculator is pure and can be finished
and fully tested on day 1 morning while everyone else is still wiring. Do that, then wire
the service when A and C land their data.

**You provide to others:** `MatchResult` (E displays, F explains) and `SkillGap` rows
(E's roadmap). E is blocked on you — land gaps before the day-1 checkpoint.
