# Role C — Candidate profile and skills

**Story:** US-02 · **Points:** 5 · **Branch:** `feat/c-profile`

> Read [00-SHARED-BRIEF.md](00-SHARED-BRIEF.md) and [01-CONTRACTS.md](01-CONTRACTS.md) first.

---

## What you own

Everything the candidate says about themselves: personal details, education, work
experience, projects, certifications, and the confirmed skill list that match scoring reads
from. Without your slice, D has nothing to score against — **half the demo is yours.**

## Requirements

| ID | Requirement | Pri |
|---|---|---|
| FR-27 | Maintain a profile with personal details, education, work experience, projects, certifications | M |
| FR-28 | Add skills to the profile and record a proficiency level | M |
| FR-29 | Resolve profile skills to the master list by the same rules as posting skills | M |
| FR-33 | Calculate total years of experience from the profile, for scoring | M |
| FR-34 | Remove a skill or an uploaded resume from the profile | M |

Business rules: **BR-04** (skills resolve to master), **BR-06** (only confirmed skills
score), **BR-09** (a profile is visible only to its owner).

## Files you own

Create:
```
src/JobAlign.Core/Abstractions/ICandidateProfileService.cs
src/JobAlign.Infrastructure/Services/CandidateProfileService.cs
src/JobAlign.Web/Controllers/ProfileController.cs
src/JobAlign.Web/Models/Profile/ProfileViewModels.cs
src/JobAlign.Web/Views/Profile/Index.cshtml
src/JobAlign.Web/Views/Profile/Skills.cshtml
src/JobAlign.Web/Views/Profile/Experience.cshtml
tests/JobAlign.Tests/CandidateProfileServiceTests.cs
```

Edit (announce first):
- `src/JobAlign.Infrastructure/DependencyInjection.cs` — append one line

**Do not touch:** `Views/Shared/_Layout.cshtml` (ask E for the nav link), extraction,
matching or skill-admin code.

---

## Entities you work with — they already exist

All under `Core/Entities/Profiles/`:

| Entity | Notes |
|---|---|
| `CandidateProfile` | Created at registration, one per user. `FullName`, `Headline`, `CurrentRole`, `PhoneNumber`, `TotalExperienceYears` |
| `EducationEntry` | Institution, qualification, dates |
| `WorkExperienceEntry` | Employer, role, dates — **the source of FR-33** |
| `ProjectEntry` | Name, description |
| `CertificationEntry` | Name, issuer, dates |
| `ProfileSkill` | `MasterSkillId` FK, `ProficiencyLevel`, `Source`, `ConfirmedAt` |
| `Resume` / `ResumeSkillSuggestion` | Stretch only |

Read each entity's XML comments before writing against it. **Do not add fields or generate
a migration** — if you think you need one, ask the lead.

The profile row already exists for every candidate: `CandidateRegistrationService` creates
it inside the registration transaction. `GetAsync` should never return null for a candidate.

---

## Task list

### 1. `CandidateProfileService`

Copy the shape of `Infrastructure/Services/JobPostingService.cs`. **Every method takes
`userId` and filters on it** (BR-09). The controller passes `CurrentUserId` read from the
auth cookie — never from the request.

Note the profile is keyed by `UserId`, and scoring works with `CandidateProfileId`. Resolve
one to the other inside the service; do not make callers do it.

### 2. Profile details (FR-27)

One page with sections for details, education, work experience, projects, certifications.
Add/edit/remove rows in each. Keep it plain — Bootstrap tables and modals or simple
sub-pages both work. This must be responsive (NFR-10); use the grid, not fixed widths.

### 3. Profile skills (FR-28, FR-29) — the important one

Adding a skill:

1. Candidate types a skill name and picks a `ProficiencyLevel`
   (`Beginner`/`Intermediate`/`Advanced`/`Expert`).
2. Call `ISkillResolver.ResolveAsync(rawText)` — **B's resolver, the same one extraction
   uses.** FR-29 exists precisely so profile and posting skills resolve identically.
3. **Resolved:** insert a `ProfileSkill` with `MasterSkillId`, the proficiency,
   `Source = Manual`, `ConfirmedAt = UtcNow`. Adding a skill already held updates the
   proficiency rather than creating a duplicate.
4. **Unresolved:** do **not** insert anything and do **not** create a `MasterSkill`. Tell
   the candidate the skill was not recognised and offer the closest matches. Free-text
   skills as identities are exactly what BR-04 forbids.

Display the **canonical** name from `MasterSkill`, not what the candidate typed. Someone who
types "c sharp" should see "C#" — that is the feature working, and it demos well.

Removal (FR-34): hard delete of the `ProfileSkill` row is correct here; it is the
candidate's own data, not master data.

### 4. Total experience (FR-33)

`RecalculateTotalExperienceAsync` sums `WorkExperienceEntry` durations into
`CandidateProfile.TotalExperienceYears`. Call it after **any** change to work experience.

- Overlapping jobs must not double-count. Merge overlapping date ranges, then sum.
- A current role with no end date runs to today.
- **No work experience at all means `null`, not `0`.** Null is "not recorded"; zero is
  "definitely no experience". D's `ExperienceScore` treats them differently, and BR-02
  forbids inventing the zero.

### 5. Trigger rescoring (FR-41)

Any change to profile skills or experience invalidates every match score. After a successful
change, call:

```csharp
await _matchScoring.RecalculateAllAsync(userId, ct);
```

Wrap it so a scoring failure does not roll back the profile change. Until D lands the real
service, the stub returns immediately — call it anyway so the wiring is proven early.

---

## Acceptance criteria

- [ ] A candidate can view and edit their profile details
- [ ] Education, work experience, projects and certifications can each be added and removed
- [ ] Adding "c sharp" stores the C# master skill and displays "C#"
- [ ] Adding an unrecognised skill stores nothing and explains why
- [ ] Adding a skill already held updates its proficiency instead of duplicating
- [ ] Removing a skill removes it from the profile
- [ ] Total experience is recomputed on every work-experience change
- [ ] Two overlapping jobs do not double-count
- [ ] A profile with no work experience has `TotalExperienceYears` null, not 0
- [ ] Signing in as another user shows that user's profile, never this one's (BR-09)

## Tests to write

```
AddSkill_resolves_through_the_master_list_and_stores_the_foreign_key
AddSkill_rejects_an_unresolved_skill_without_creating_a_master_skill
AddSkill_updates_proficiency_when_the_skill_is_already_held
RemoveSkill_only_removes_the_callers_own_skill
TotalExperience_is_null_when_no_work_experience_is_recorded
TotalExperience_sums_sequential_roles
TotalExperience_does_not_double_count_overlapping_roles
TotalExperience_treats_an_open_ended_role_as_running_to_today
```

The overlap arithmetic is pure logic — test it hard. It is the part most likely to be
quietly wrong.

## Dependencies

| You need | From | Until then |
|---|---|---|
| `ISkillResolver` + seeded skills | B (Wave 0) | Blocked on Wave 0 — do the details/education/experience screens first, they need nothing |
| `IMatchScoringService.RecalculateAllAsync` | D | Call the stub; wrap in try/catch |

**You provide to others:** `ProfileSkill` rows and `TotalExperienceYears` — D's scorer reads
both. Nothing D does works until a profile has skills, so **get a demo profile with skills
in by the day-1 checkpoint**.

## Demo slice

Sign in → Profile → add three work-experience entries → total experience appears → add "c
sharp", "react", "docker" → they display as C#, React, Docker with proficiencies.

## Stretch, if you finish early

**Resume upload (FR-30, FR-31, FR-32 — all S priority).** Only start this once everything
above is merged and green.

- Upload a `.pdf` or `.docx` to `Resume`, with `ResumeExtractionStatus`
- Extract skills, education, work experience
- **Extracted skills go to `ResumeSkillSuggestions`, never straight to `ProfileSkills`.**
  They must not affect a match score until the candidate confirms each one, at which point a
  `ProfileSkill` with `Source = ResumeConfirmed` is created. This is BR-06, and the schema is
  shaped to make violating it hard — do not route around it.
- FR-34 also covers removing an uploaded resume.
