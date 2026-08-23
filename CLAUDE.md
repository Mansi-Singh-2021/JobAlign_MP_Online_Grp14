# JobAlign — AI-Powered Job & Skill Alignment

## What this is
A web application that captures unstructured job postings, uses an AI service to extract and
normalize them into structured data, and scores how well a candidate's skill profile matches
each role. Candidates see match scores, skill gaps, and a prioritized learning roadmap.

Full requirements: `docs/JobAlign_Requirement_Analysis.pdf` (SRS JA-SRS-001 v1.0 —
FR-01 to FR-60, BR-01 to BR-10, NFR-01 to NFR-13).
When implementing, reference requirements by ID in commit messages and comments.

## Stack
- C# / **.NET 10**, ASP.NET Core MVC (UI) + Web API (services)
- Entity Framework Core 10, code-first migrations
- SQL Server
- Bootstrap 5
- External AI API for extraction, normalization, summarization and feedback

> The original handoff specified .NET 8. This machine has no ASP.NET Core 8 runtime — a
> `net8.0` web app builds but will not start. SRS §9.3 pins the stack (C#, ASP.NET Core MVC +
> Web API, EF Core, SQL Server, Bootstrap) but never pins a framework version, so .NET 10 is
> compliant. If a rubric later demands .NET 8, install the .NET 8 SDK + ASP.NET Core 8 runtime
> and change `<TargetFramework>` in all five projects.

## Project layout
```
src/
  JobAlign.Web/         MVC controllers, views, view models  (EF startup project)
  JobAlign.Api/         Web API controllers
  JobAlign.Core/        Domain entities, enums, interfaces, business rules
  JobAlign.Infrastructure/  EF Core DbContext, configurations, repositories, AI client
tests/
  JobAlign.Tests/       Unit tests
docs/
  JobAlign_Requirement_Analysis.pdf
```

## Non-negotiable rules
These come from the requirements document. Do not violate them for convenience.

1. **Raw text is immutable.** `JobPosting.RawText` is written once and never modified.
   Extractions are derived, disposable, and regenerable. (BR-01)
2. **Never invent data.** Anything not stated in a posting is `null` in the database and
   renders as "Not specified". Never default to 0, empty string, or a guessed value.
   Nullable columns are deliberate — do not make them non-nullable. (BR-02, FR-17, NFR-07)
3. **User corrections win.** A candidate's manual correction overrides the extracted value
   and survives re-extraction. Track corrected fields explicitly. (BR-03)
4. **All skills resolve to a master skill.** Posting skills, resume skills, and profile skills
   all go through the alias table. Never insert a free-text skill string. (BR-04, FR-14)
5. **Resume-extracted skills are suggestions only.** They do not enter the profile or affect
   match scores until the candidate confirms them. (BR-06, FR-32)
6. **Never bind AI output directly to EF entities.** Deserialize to a DTO, validate enums and
   ranges, then map. AI responses are untrusted input.
7. **Extract once, store the result.** Viewing a posting must never trigger a new AI call. (NFR-13)
8. **Ownership is enforced server-side.** Every query for postings, resumes and profiles filters
   by the authenticated user. Never rely on the UI to hide another user's data. (BR-09, NFR-04)
   Administrators manage accounts but may **not** read candidate postings or resumes.

## Database

Schema is complete and applied — migration `InitialSchema`, 31 tables (23 domain + 8 Identity).
Connection string `JobAlignDb` in `src/JobAlign.Web/appsettings.json`, local SQL Server,
Windows auth.

### Where the rules live in the schema
Several business rules are enforced by the shape of the schema rather than by remembering to
write the right code. Do not "simplify" these away.

- **BR-01** — `JobPosting.RawText` and `.Reference` have private setters and are assigned only
  in the constructor. There is no code path that mutates them.
- **BR-02** — every extracted column on `PostingExtractions` is nullable. Adding `IsRequired()`
  to any of them breaks the rule.
- **BR-03** — `PostingFieldCorrections` has its foreign key to **`JobPostings`, not to
  `PostingExtractions`**. Deleting and regenerating extractions therefore cannot touch
  corrections. This is the whole reason for the table's shape.
- **BR-04** — `PostingSkills`/`ProfileSkills` hold `MasterSkillId` as a foreign key. `RawText`
  is provenance only and is never the identity of a skill.
- **BR-06** — resume output lands in `ResumeSkillSuggestions`, a different table from
  `ProfileSkills`. Scoring reads only `ProfileSkills`, so an unconfirmed suggestion is
  structurally incapable of affecting a score.
- **NFR-08** — extraction runs are retained as history. A filtered unique index
  (`UX_PostingExtractions_CurrentPerPosting`, `WHERE IsCurrent = 1`) permits at most one
  current run per posting.

### Two independent status concepts
The SRS defines two, and conflating them breaks BR-08. They are separate columns:
- `JobPosting.Status` — extraction lifecycle: `New` → `Pending` (on failure) → `Confirmed`.
  Only `Pending` is excluded from scoring, comparison and the dashboard (BR-08, FR-54).
- `JobPosting.ApplicationStatus` — `Saved` / `Applied` / `Interview` / `Rejected` / `Closed` (FR-53).

### Nullable scores
`MatchResult`'s four scores are all nullable. A proportion is undefined when the posting states
nothing to measure — a posting listing no preferred skills has no preferred-skill score, and
storing 0 would be inventing a fact (BR-02). Treat null as "not measurable", never as zero.
A posting with a null `OverallScore` is excluded from match-score sorting, the same way a
posting with no salary is excluded from salary sorting (BR-10).

### Conventions
- Enums are stored as **strings**, not ints, so the data is legible without a lookup.
- `Restrict` on master-data foreign keys: skills and locations are deactivated, never deleted.
- Two foreign keys pointing at the same table (`PostingRelations`, and `MatchResults` →
  profile/posting) have one `Cascade` and one `NoAction`. SQL Server rejects multiple cascade
  paths, so this is required, not a preference.

## AI extraction contract
One call per posting. Strict JSON, no prose, no markdown fences. System prompt must state:
- Return `null` for anything not explicitly stated — never estimate
- Normalize salary to a yearly figure, but return the originally stated value and period too
- `"unclear"` is a valid and correct answer for enum fields

Enums (do not accept free text):
- `remotePolicy`: remote | hybrid | onsite | unclear
- `salaryPeriod`: year | month | hour | null
- `skillType`: required | preferred

## Match scoring
Four scores, all stored, all displayed (FR-36 to FR-40):
- `RequiredSkillScore` — proportion of required skills the candidate holds
- `PreferredSkillScore` — proportion of preferred skills held
- `ExperienceScore` — candidate years vs posting requirement
- `OverallScore` — weighted combination; required skills weigh more than preferred (BR-07)

Recalculate all scores when the profile changes (FR-41). Postings with status `Pending`
are excluded from scoring, comparison and dashboard figures (BR-08).

## Build order
Work in this sequence. Do not jump ahead.

0. ~~Schema: entities, DbContext, `InitialSchema` migration, database created.~~ **Done.**
   Built in full rather than incrementally, so later steps add code and not schema churn.
   From here on, follow the one-migration-per-logical-change rule below.
1. ~~Auth with roles: registration, sign-in, role seeding, `[Authorize]` (FR-01 to FR-05)~~
   **Done.** `RoleSeeder` runs at startup; `AccountController` covers register, sign-in,
   sign-out and password reset. Authorization fails closed via a fallback policy, so a new
   controller is protected unless it opts out with `[AllowAnonymous]`.
2. ~~Paste a posting → save → list it. **No AI at all.**~~ (FR-06, FR-08, FR-09) **Done.**
   `IJobPostingService` takes the owner id on every method, so BR-09 is enforced at the
   service boundary rather than in the controller.
3. ~~`IJobExtractor` interface + `StubExtractor`. Wire the full review/correct UI
   against the stub.~~ (FR-12, FR-18, FR-19, FR-20, FR-21) **Done.** Extraction runs are
   history; corrections attach to the posting, so they survive a re-run (BR-03).
4. `AiExtractor` real implementation behind the same interface. Keep the stub for tests.
5. ~~Master skills, aliases, skill resolution~~ (FR-14, FR-57, FR-58) **Done.**
   `MasterSkillSeeder` seeds 46 skills and 35 aliases at startup; `SkillResolver` does
   exact + alias lookup and follows merges; `/SkillsAdmin` covers add, edit, deactivate,
   aliases and merge. Merging repoints stored posting and profile skills, so scoring sees
   one skill afterwards.
6. Salary and location normalization (FR-15, FR-16)
7. Candidate profile and skills (FR-27 to FR-29, FR-33)
8. Match scoring (FR-35 to FR-41)
9. Skill gap and roadmap (FR-42 to FR-46)
10. Dashboard, filter, sort, compare (FR-48 to FR-54)
11. Resume upload and parsing (FR-30 to FR-32)
12. Duplicate detection and quality checker (FR-22 to FR-26)
13. Link-based capture (FR-07) — do this last, it's the most fragile

## Conventions
- Async all the way down; suffix async methods with `Async`
- Controllers stay thin — business logic lives in `JobAlign.Core` services
- Extraction runs as a background job, not a blocking request (NFR-01)
- API keys in user-secrets locally, environment variables in deployment. Never in appsettings.json
  (the `JobAlignDb` connection string uses Windows auth and holds no secret)
- One EF migration per logical schema change, named descriptively

## Commands
```bash
dotnet build
dotnet run --project src/JobAlign.Web
dotnet ef migrations add <Name> --project src/JobAlign.Infrastructure --startup-project src/JobAlign.Web
dotnet ef database update --project src/JobAlign.Infrastructure --startup-project src/JobAlign.Web
dotnet test
```

Inspecting the database directly (note `-I`: filtered indexes need `QUOTED_IDENTIFIER ON`,
which sqlcmd leaves off by default — .NET SqlClient sets it on, so this affects sqlcmd only):
```bash
sqlcmd -S localhost -E -C -I -d JobAlign -Q "SELECT name FROM sys.tables ORDER BY name;"
```
