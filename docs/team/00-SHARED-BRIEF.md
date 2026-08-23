# Shared brief — read this before writing any code

**Every member's AI assistant must read this file and [01-CONTRACTS.md](01-CONTRACTS.md)
before touching the repository.** Then read your own role handout.

---

## 1. What JobAlign is

A candidate-side job-hunting tool. A candidate pastes a job posting as raw text. An AI
service extracts it into structured fields. The system scores the posting against the
candidate's skill profile and reports match scores, missing skills and a learning roadmap.

The controlled requirements baseline is **`docs/JobAlign_Requirement_Analysis.pdf`
(JA-SRS-001 v1.0)** — FR-01 to FR-60, BR-01 to BR-10, NFR-01 to NFR-13.

> **Trap:** the repository root also contains `Requirement Analysis.pdf`, an earlier and
> different brief that numbers FR-01 to FR-20 only. The code follows **JA-SRS-001**.
> If a requirement ID in your handout does not match what you are reading, you have the
> wrong PDF open.

## 2. Stack and layering

C# / **.NET 10**, ASP.NET Core MVC, EF Core 10 code-first, SQL Server, Bootstrap 5.

```
JobAlign.Core            entities, enums, service INTERFACES   — references nothing
JobAlign.Infrastructure  EF Core, service IMPLEMENTATIONS      -> Core
JobAlign.Web             MVC controllers, views, view models   -> Core, Infrastructure
JobAlign.Api             Web API (unused so far)               -> Core, Infrastructure
JobAlign.Tests           xUnit
```

**`JobAlign.Core` must never reference EF Core or ASP.NET.** Interfaces and domain types
go in Core; anything that touches `DbContext` goes in Infrastructure. If you find yourself
adding `using Microsoft.EntityFrameworkCore;` to a file in Core, stop — the class belongs
in the other project.

## 3. The eight non-negotiable rules

These come from the SRS. Do not break them for convenience. An AI that "simplifies" one of
these has broken the product, not improved it.

1. **Raw text is immutable.** `JobPosting.RawText` is written once, never modified (BR-01).
   It has a private setter and a test that fails if anyone adds a public one.
2. **Never invent data.** Anything a posting does not state is `null` in the database and
   renders as **"Not specified"**. Never `0`, never `""`, never a guess. Every extracted
   column is nullable *on purpose* — adding `IsRequired()` to one breaks the rule
   (BR-02, FR-17, NFR-07).
3. **User corrections win.** A candidate's correction overrides the extracted value and
   survives re-extraction. Corrections attach to the **posting**, not to an extraction run
   — that is why `PostingFieldCorrections` has its foreign key to `JobPostings` (BR-03).
4. **Every skill resolves to a `MasterSkill` foreign key.** Never store a free-text skill
   as an identity. `RawText` on a skill row is provenance only (BR-04, FR-14).
5. **Resume-extracted skills are suggestions only.** They live in `ResumeSkillSuggestions`
   and do not affect scoring until confirmed into `ProfileSkills` (BR-06, FR-32).
6. **Never bind AI output directly to EF entities.** Deserialize to a DTO, validate enums
   and ranges, then map. AI responses are untrusted input.
7. **Extract once, store the result.** Viewing a posting must never trigger an AI call
   (NFR-13).
8. **Ownership is enforced server-side, in the service layer.** Every query for postings,
   resumes and profiles filters by the authenticated user. Administrators manage accounts
   but may **not** read candidate postings or resumes (BR-09, NFR-04).

## 4. Two things that look like one thing

**Two independent statuses on a posting.** Conflating them breaks BR-08.

- `JobPosting.Status` — extraction lifecycle: `New` -> `Pending` (on failure) ->
  `Confirmed`. Only `Pending` is excluded from scoring, comparison and the dashboard.
- `JobPosting.ApplicationStatus` — `Saved` / `Applied` / `Interview` / `Rejected` / `Closed`.

**Null is not zero, anywhere.** `MatchResult`'s four scores are all nullable. A posting
listing no preferred skills has **no** preferred-skill score; storing `0` would be
inventing a fact. Treat null as "not measurable". A posting with a null `OverallScore` is
excluded from match-score sorting, exactly as a posting with no salary is excluded from
salary sorting (BR-10).

## 5. Conventions

- Async all the way down; suffix async methods `Async`.
- **Controllers stay thin.** Business logic lives behind a Core interface, implemented in
  Infrastructure. A controller that queries `DbContext` directly is a review failure.
- Enums are stored as **strings** in the database, not ints.
- `Restrict` on master-data foreign keys: skills and locations are deactivated, never deleted.
- Cite requirement IDs in commit messages and in comments on non-obvious code.
- API keys go in **user-secrets** locally and environment variables in deployment. Never in
  `appsettings.json`. The `JobAlignDb` connection string uses Windows auth and holds no
  secret, which is why it is committed.
- One EF migration per logical change, named descriptively.
- Comments explain **why**, not what. Match the density of the existing code.

## 6. Existing code you will build on

Already implemented and working — read it before you write anything similar:

| Thing | Where |
|---|---|
| Posting capture service (the pattern to copy) | `Infrastructure/Services/JobPostingService.cs` |
| Ownership-enforcing interface | `Core/Abstractions/IJobPostingService.cs` |
| Controller shape, `CurrentUserId` | `Web/Controllers/PostingsController.cs` |
| Auth, roles, seeding | `Web/Controllers/AccountController.cs`, `Infrastructure/Identity/RoleSeeder.cs` |
| DI registration | `Infrastructure/DependencyInjection.cs` |
| View style, Bootstrap usage | `Views/Postings/Index.cshtml`, `Details.cshtml` |
| Test style | `tests/JobAlign.Tests/JobPostingTests.cs` |

`IJobPostingService` is the template for every service you write: **the owner id is a
parameter on every method**, so ownership is enforced at the service boundary and no caller
can bypass it.

## 7. The entities already exist — do not create new ones

All 23 domain entities and the 31-table schema are built and migrated. Your job is to *use*
them, not to add to them. Read the entity and its XML comments before writing code against
it; they record which requirement each field serves.

If you genuinely need a schema change, **ask the lead first**. An unannounced migration
will collide with someone else's.

### The 23 domain tables — this list is authoritative

| Area | Tables |
|---|---|
| Skills | `MasterSkills`, `SkillAliases`, `Locations`, `LocationAliases` |
| Postings | `JobPostings`, `PostingExtractions`, `ExtractionFieldConfidences`, `PostingFieldCorrections`, `PostingSkills`, `PostingRelations`, `PostingQualityAssessments` |
| Profiles | `CandidateProfiles`, `EducationEntries`, `WorkExperienceEntries`, `ProjectEntries`, `CertificationEntries`, `ProfileSkills`, `Resumes`, `ResumeSkillSuggestions` |
| Matching | `MatchResults`, `SkillGaps`, `RoadmapItems` |
| Admin | `AuditEntries` |
| Identity | `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims` |

### Names that look real but are not

`Database_Design.pdf` describes an **earlier** schema. These names appear in it and in older
notes, and **none of them exists** — code written against them will not compile:

`SkillCategories` · `SkillCategory` · `Candidates` · `CandidateSkills` · `JobAnalyses` ·
`AnalysisSkills` · `AnalysisFeedback` · `JobSkills` · `JobExtractions` · `SavedJobs` ·
`Organizations` · `Recruiters` · `JobComparisons` · `JobComparisonItems` · `AuditLogs` ·
`IJobPostingRepository`

This has already cost the team a full rebuild once. `Schema_Deviations.md` in the repository
root maps every one of them to the table that does exist — read it before writing data-access
code, and **check a name against the list above before you use it**. When in doubt:

```bash
sqlcmd -S localhost -E -C -I -d JobAlign -Q "SELECT name FROM sys.tables ORDER BY name;"
```

Property names differ too. `MasterSkill` has `Name`, not `SkillName`, and `Category` as a
plain string, not `CategoryId`. `SkillAlias` has `Alias` and `MasterSkillId`, not `AliasName`
and `SkillId`. Timestamps are `DateTimeOffset`, not `DateTime`. Open the entity and read it.

Enum reference (all stored as strings):

| Enum | Members |
|---|---|
| `PostingStatus` | New, Pending, Confirmed |
| `ApplicationStatus` | Saved, Applied, Interview, Rejected, Closed |
| `PostingCaptureMethod` | PastedText, Link |
| `ExtractionRunStatus` | Succeeded, Failed |
| `RemotePolicy` | Remote, Hybrid, Onsite, Unclear |
| `SalaryPeriod` | Year, Month, Hour |
| `ConfidenceLevel` | High, Medium, Low |
| `SkillType` | Required, Preferred |
| `PostingSkillSource` | Extracted, UserAdded |
| `ProficiencyLevel` | Beginner, Intermediate, Advanced, Expert |
| `ProfileSkillSource` | Manual, ResumeConfirmed, RoadmapCompleted |
| `RoadmapItemStatus` | NotStarted, InProgress, Completed |
| `ResumeExtractionStatus` | Pending, Succeeded, Failed |
| `PostingRelationType` | SuspectedDuplicate, SameRole |
| `PostingRelationResolution` | Unresolved, KeptBoth, DiscardedNew, LinkedAsSameRole |

## 8. Commands

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project src/JobAlign.Web
```

```bash
dotnet ef migrations add YourMigrationName --project src/JobAlign.Infrastructure --startup-project src/JobAlign.Web
```

```bash
dotnet ef database update --project src/JobAlign.Infrastructure --startup-project src/JobAlign.Web
```

Inspecting the database — the `-I` flag is required, because filtered indexes need
`QUOTED_IDENTIFIER ON` and sqlcmd leaves it off by default:

```bash
sqlcmd -S localhost -E -C -I -d JobAlign -Q "SELECT name FROM sys.tables ORDER BY name;"
```

## 9. Working agreement

- Branch `feat/<letter>-<slug>`, merge to `develop`, never straight to `main`.
- Only edit files your handout says you own. Need a change elsewhere? Ask in the chat.
- **0 warnings** is the standard, not 0 errors. The build is currently clean; keep it so.
- Push at least every two hours so blockers surface early.
- If you are blocked for more than 30 minutes, say so in the chat rather than inventing an
  interface someone else owns.

## 10. Instructions for the AI assistant

If you are an AI working on this repository:

- Read this file, `01-CONTRACTS.md`, your role handout, and `CLAUDE.md` before editing.
- **Stay inside your lane.** Your handout lists the files you own. Editing another member's
  files causes merge conflicts that cost the team more than your change saves.
- Do not "clean up", reformat, or refactor code outside your scope, however tempting.
- Do not weaken a nullable column, add a public setter to a write-once property, or simplify
  away a `NoAction` delete behaviour. Each of those encodes a business rule.
- If a requirement and the existing code disagree, **stop and ask the human** rather than
  picking one yourself.
- Write the tests. A slice with no tests is not done.
