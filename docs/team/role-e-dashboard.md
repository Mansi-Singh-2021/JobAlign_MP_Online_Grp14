# Role E — Skill gaps, roadmap and dashboard

**Story:** US-07 · **Points:** 6 · **Branch:** `feat/e-dashboard`

> Read [00-SHARED-BRIEF.md](00-SHARED-BRIEF.md) and [01-CONTRACTS.md](01-CONTRACTS.md) first.

---

## What you own

Everything the candidate actually looks at once the machinery behind it works. You are the
last link in the chain and **the face of the demo** — if your screens are clear, the project
looks finished; if they are not, nobody sees the good work underneath.

You also own the shared layout and the postings list, so you are the integration point for
other members' nav links.

## Requirements

| ID | Requirement | Pri |
|---|---|---|
| FR-42 | Display, per posting, the skills the candidate holds and the skills they lack | M |
| FR-43 | Distinguish missing **required** skills from missing **preferred** skills | M |
| FR-45 | Identify the skills most frequently missing across all saved postings | S |
| FR-46 | Produce a learning roadmap ordering missing skills by frequency and type | S |
| FR-47 | Mark a roadmap skill in progress or completed | S |
| FR-50 | Filter saved postings by work mode, location, experience range | M |
| FR-51 | Sort saved postings by match score, salary or date captured | M |
| FR-52 | Dashboard presenting match scores, current skill gaps, posting counts | S |
| FR-53 | Record application status: Saved, Applied, Interview, Rejected, Closed | S |
| FR-54 | Exclude `Pending` postings from comparison and dashboard figures | M |

Business rules: **BR-08** (Pending excluded), **BR-09** (owner filtering),
**BR-10** (null scores excluded from sorting, not treated as zero). NFR: **NFR-02**
(list and dashboard render within 2 seconds for 500 postings), **NFR-10** (responsive).

## Files you own

Create:
```
src/JobAlign.Core/Abstractions/ISkillGapService.cs
src/JobAlign.Infrastructure/Services/SkillGapService.cs
src/JobAlign.Web/Controllers/DashboardController.cs
src/JobAlign.Web/Controllers/RoadmapController.cs
src/JobAlign.Web/Models/Dashboard/DashboardViewModels.cs
src/JobAlign.Web/Views/Dashboard/Index.cshtml
src/JobAlign.Web/Views/Roadmap/Index.cshtml
src/JobAlign.Web/Views/Shared/_MatchScoreCard.cshtml
tests/JobAlign.Tests/SkillGapServiceTests.cs
```

Own and edit:
- `src/JobAlign.Web/Views/Shared/_Layout.cshtml` — **you are the only one who edits this.**
  Others will ask you for nav links; add them.
- `src/JobAlign.Web/Views/Postings/Index.cshtml` — add filter, sort, match-score column
- `src/JobAlign.Web/Controllers/PostingsController.cs` — **A also edits this.** Coordinate:
  A adds `Extract`/`Review`/`Confirm`, you add filter/sort parameters to `Index` and an
  `ApplicationStatus` action. Agree who merges first.
- `src/JobAlign.Infrastructure/DependencyInjection.cs` — append one line

**Do not touch:** extraction internals, `ScoreCalculator`, profile services, skill admin.

---

## Task list

### 1. `SkillGapService`

`GetGapsForPostingAsync` reads the `SkillGap` rows D writes, with `MasterSkill` included so
you can show names. Filter by owner (BR-09).

### 2. Match score display (FR-40, FR-42, FR-43)

A partial, `_MatchScoreCard.cshtml`, reused on the posting details page and the dashboard.
Show the overall score **and all three components** — FR-40 exists so a score can be
explained rather than asserted.

Handling nulls is the part to get right:

- A null component renders as **"Not measurable"**, never as `0` or an empty bar. A posting
  listing no preferred skills genuinely has no preferred score (BR-02).
- A null overall score means the posting stated too little to measure. Say so; do not show
  a zero-length bar that reads as a bad match.

Skills split into three groups: held, missing required, missing preferred. **Missing
required must be visually distinct from missing preferred** — that is FR-43, and it is the
single most useful thing on the screen. Colour alone is not enough; use a label or icon too.

### 3. Roadmap (FR-45, FR-46, FR-47)

`RebuildRoadmapAsync` aggregates every `SkillGap` across the candidate's **Confirmed**
postings (BR-08):

1. Group gaps by `MasterSkillId`.
2. Count occurrences where `SkillType = Required` into `RequiredOccurrenceCount`, and
   `Preferred` into `PreferredOccurrenceCount`.
3. Order by required count descending, then preferred count descending, then skill name.
4. `Priority` is the 1-based rank.
5. **Preserve status.** A skill the candidate already marked `InProgress` or `Completed`
   keeps that status through a rebuild. Losing it on every rescore would be maddening.

`SetRoadmapStatusAsync` (FR-47): marking an item `Completed` does **not** add the skill to
the profile. The candidate confirms that separately, and only then does C's service create a
`ProfileSkill` with `Source = RoadmapCompleted`. A roadmap item alone never moves a match
score (BR-06). Offer a "I've learned this — add to my profile" action that calls C's
`AddSkillAsync`.

### 4. Dashboard (FR-52)

One page:

- Posting counts by `ApplicationStatus`
- Average and best match score across confirmed postings
- Top 5 roadmap skills
- Recent postings with their scores

**Exclude `Pending` postings from every figure** (BR-08, FR-54). Say so on the page — a
count that silently omits rows is confusing; a count labelled "excludes 2 pending" is not.

### 5. Filter and sort (FR-50, FR-51)

On the postings list. Filter by work mode (`RemotePolicy`), location, experience range.
Sort by match score, salary or date captured.

**Sorting with nulls is where BR-10 bites.** A posting with no salary is excluded from
salary sorting, not sorted as zero. Same for match score. Two defensible options:

- Sort them to the end of the list under a "Not scored" divider, or
- Filter them out when that sort is active, with a note saying how many were omitted

Pick one and be consistent. What you must not do is treat null as `0` and rank an
unmeasured posting below a genuinely bad one.

### 6. Application status (FR-53)

A dropdown on the posting row and details page cycling `Saved` / `Applied` / `Interview` /
`Rejected` / `Closed`. This is `JobPosting.ApplicationStatus`, **completely independent of
`JobPosting.Status`** — do not conflate them (BR-08). One is where the candidate is in
applying; the other is whether extraction succeeded.

### 7. Navigation

You own `_Layout.cshtml`. The candidate nav should end up roughly:

`Dashboard · My postings · Add a posting · My profile · Roadmap`

plus `Skills` and `Users` for administrators only. Use `User.IsInRole(RoleNames.Candidate)`
and `RoleNames.Administrator`, as the existing layout already does.

---

## Acceptance criteria

- [ ] Posting details shows overall **and** all three component scores
- [ ] A null component reads "Not measurable", never `0`
- [ ] Held, missing-required and missing-preferred skills are visually distinguishable
- [ ] The roadmap orders skills by how often they are missing, required first
- [ ] Roadmap status survives a rebuild
- [ ] Marking a roadmap skill completed does not change any match score by itself
- [ ] The dashboard excludes Pending postings and says that it does
- [ ] Sorting by match score does not rank unscored postings as zero
- [ ] Filter by work mode and experience range works
- [ ] Application status can be changed and persists
- [ ] Every page is usable at 375px wide (NFR-10)

## Tests to write

```
RebuildRoadmap_orders_required_gaps_above_preferred
RebuildRoadmap_counts_occurrences_across_postings
RebuildRoadmap_preserves_an_InProgress_status
RebuildRoadmap_ignores_pending_postings
SetRoadmapStatus_rejects_another_users_item
GetGapsForPosting_filters_by_owner
```

## Dependencies

| You need | From | Until then |
|---|---|---|
| `MatchResult` and `SkillGap` rows | D | **Biggest dependency.** Build the views against hand-inserted rows first |
| Confirmed postings with skills | A | same |
| `FeedbackText` on `MatchResult` | F | Show it if present, hide the panel if null |
| `ICandidateProfileService.AddSkillAsync` | C | For the roadmap "add to profile" action |

You are last in the chain, which means **you are the most likely to be squeezed.** Protect
yourself: on day 1 morning, insert a `MatchResult` and a few `SkillGap` rows directly with
sqlcmd and build every screen against those. Do not sit waiting for D.

**You provide to others:** nav links in `_Layout.cshtml`. Respond quickly when asked.

## Demo slice

Dashboard with real scores → click a posting → score card with components, held and missing
skills → Roadmap ordered by frequency → mark one in progress.

## Stretch, if you finish early

- **FR-49** — compare selected postings side by side (S)
- **FR-23** — a posting quality indicator from `PostingQualityAssessment` (C)
