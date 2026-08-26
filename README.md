# JobAlign

AI-powered job and skill alignment platform. Captures unstructured job postings, uses an
external AI service to extract and normalize them into structured data, and scores how well a
candidate's skill profile matches each role. Candidates see match scores, skill gaps, and a
prioritized learning roadmap.

Built against SRS `JA-SRS-001 v1.0` — see [docs/JobAlign_Requirement_Analysis.pdf](docs/JobAlign_Requirement_Analysis.pdf).

---

## Current status

The core candidate journey is complete end to end: register, capture a posting, extract it with
a real AI call, correct and confirm the detail, maintain a profile, and receive match scores,
skill gaps and a learning roadmap.

| Area | State |
|---|---|
| Solution, 5 projects, layered references | Complete |
| Domain entities (all of SRS §10) | Complete |
| EF Core DbContext, configurations, `InitialSchema` migration | Complete — 31 tables |
| Identity, role seeding, register / sign-in / sign-out / password reset | Complete |
| Capture a posting — paste, list, view, archive, delete | Complete |
| Extraction, review, correction, confirmation | Complete |
| AI integration (Gemini, behind a provider-agnostic seam) | Complete |
| Master skills, aliases, resolution, administrator screens | Complete |
| Candidate profile — skills, education, experience, projects, certifications | Complete |
| Match scoring — required, preferred, experience, overall | Complete |
| Skill gaps and learning roadmap | Complete |
| Candidate dashboard | Complete |
| Posting list filtering and sorting | Complete |
| Salary and location normalization | Not built |
| Side-by-side posting comparison | Not built |
| Resume upload and parsing | Not built |
| Duplicate detection and posting quality checker | Not built |
| Link-based capture | Not built |
| Web API project (`JobAlign.Api`) | Scaffolded only — no controllers |

Test suite: **156 tests, all passing.**

Without an AI API key the application still runs in full. A stub extractor and stub feedback
generator stand in, so no contributor is blocked on obtaining a key.

---

## Prerequisites

| Requirement | Version verified | Check with |
|---|---|---|
| .NET SDK | 10.0.400 | `dotnet --version` |
| SQL Server | 2025 (17.0), local instance | `sqlcmd -S localhost -E -C -Q "SELECT @@VERSION"` |
| EF Core CLI tools | 10.0.10 | `dotnet ef --version` |
| Git | 2.49 | `git --version` |

If `dotnet ef` is missing:

```bash
dotnet tool install --global dotnet-ef
```

> **On framework versions:** this project targets **.NET 10**, not .NET 8. A `net8.0` build
> succeeds but will not start without the ASP.NET Core 8 runtime installed. SRS §9.3 mandates
> the stack — C#, ASP.NET Core MVC and Web API, EF Core, SQL Server, Bootstrap — but does not
> pin a framework version, so .NET 10 is compliant.

---

## First-time setup

### 1. Restore and build

```bash
dotnet build
```

Expect `Build succeeded. 0 Warning(s) 0 Error(s)`.

### 2. Point at your SQL Server

The connection string lives in [src/JobAlign.Web/appsettings.json](src/JobAlign.Web/appsettings.json)
under `ConnectionStrings:JobAlignDb`. The default targets the local default instance using
Windows authentication:

```
Server=localhost;Database=JobAlign;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true
```

Change `Server=` if you use a named instance — for example `Server=localhost\MSSQLSERVER01`.

This string contains no secret, which is why it is safe in `appsettings.json`. **API keys are
different** — see the next step.

### 3. Configure the AI provider (optional)

Provider, model and limits are not secrets and live in `appsettings.json` under `Ai`:

```json
"Ai": {
  "Provider": "Gemini",
  "Model": "gemini-3.6-flash",
  "BaseUrl": "https://generativelanguage.googleapis.com/v1beta",
  "TimeoutSeconds": 30,
  "MaxOutputTokens": 8192
}
```

The API key must never be committed. Store it in user-secrets locally:

```bash
dotnet user-secrets set "Ai:ApiKey" "<your-key>" --project src/JobAlign.Web
```

In deployment, supply it as an environment variable using a double underscore rather than a
colon:

```
Ai__ApiKey=<your-key>
```

If no key is configured, the application falls back to the stub extractor and stub feedback
generator and runs normally. Extraction returns deterministic placeholder data instead of real
analysis.

`Provider` accepts `Gemini` (default) or `Anthropic`. Both sit behind the same `IAiChatClient`
interface, so changing provider is a configuration edit, never a code change. Model names are
withdrawn by vendors from time to time; that is an `Ai:Model` edit, also never a code change.

### 4. Create the database

```bash
dotnet ef database update --project src/JobAlign.Infrastructure --startup-project src/JobAlign.Web
```

This creates the `JobAlign` database and all 31 tables.

### 5. Confirm it worked

```bash
sqlcmd -S localhost -E -C -I -d JobAlign -Q "SELECT COUNT(*) AS TableCount FROM sys.tables;"
```

Expect `31`.

### 6. Run the application

```bash
dotnet run --project src/JobAlign.Web
```

Open the URL it prints, typically `https://localhost:7xxx`. Stop with `Ctrl+C`.

Roles and the master skill list (46 skills, 35 aliases) are seeded automatically at startup.
Seeding is idempotent and safe to run on every boot.

---

## Architecture

Four projects on .NET 10, layered so that dependencies point inward only:

```
JobAlign.Web (MVC)   ──┐
                       ├──▶  JobAlign.Infrastructure  ──▶  JobAlign.Core
JobAlign.Api (Web API) ┘                                   (no dependencies)
```

**JobAlign.Core** holds domain entities, enums, service interfaces and pure business logic. It
references no packages — no EF Core, no ASP.NET, no HTTP. `ScoreCalculator` is a static class
of scoring arithmetic with no dependencies, which is why it is the most heavily unit-tested
component in the solution.

**JobAlign.Infrastructure** holds every implementation that touches the outside world: the EF
Core `DbContext`, entity configurations, migrations, service implementations, AI clients and
startup seeders. A single `AddJobAlignInfrastructure()` extension registers all of it, so hosts
never hand-wire dependencies one by one.

**JobAlign.Web** is the ASP.NET Core MVC front end and the EF startup project — seven
controllers, matching Razor view folders, and per-feature view models.

**JobAlign.Api** is scaffolded for the Web API service layer but has no controllers yet.

### Key abstractions

Two interfaces carry most of the architectural weight:

- **`IJobExtractor`** — implemented by `StubExtractor` (deterministic, no network) and
  `AiExtractor` (live provider call). Which one resolves is decided at registration time by
  whether an API key is present.
- **`IAiChatClient`** — sits beneath `AiExtractor` and hides each provider's wire format.
  `GeminiClient` and `AnthropicClient` are interchangeable; `AiExtractor` never learns which
  one answered.

### Request flow

1. **Capture** — the candidate pastes raw text. `JobPostingService` stores it and assigns a
   human-readable reference. No AI call occurs at this stage.
2. **Extraction** — `ExtractionService` loads the posting filtered by owner, passes only the
   raw text to `IJobExtractor`, deserializes the JSON response into a validated DTO, then maps
   to entities inside a transaction. Skills resolve through `SkillResolver` to master-skill
   foreign keys. Previous runs are retained as history.
3. **Review** — the candidate corrects any extracted field. Corrections attach to the posting
   rather than the extraction run, so they survive re-extraction. Confirming marks the posting
   eligible for scoring.
4. **Scoring** — `MatchScoringService` produces four scores via `ScoreCalculator`.
5. **Gaps and roadmap** — `SkillGapService` derives skill gaps from those scores and builds a
   prioritized roadmap; `AiFeedbackGenerator` produces the accompanying narrative feedback.
6. **Recalculation** — any profile change rescores the entire posting library automatically.

### Match scoring

| Component | Weight | Null when |
|---|---|---|
| Required skills | 0.60 | the posting lists no required skills |
| Preferred skills | 0.15 | the posting lists no preferred skills |
| Experience | 0.25 | either side leaves years unstated |
| **Overall** | — | all three components are null |

The overall score is a weighted mean across only the components that are present, so a posting
that lists no preferred skills is not penalised for the omission. A null score means "not
measurable" and is never treated as zero.

### Security model

Authorization fails closed. A fallback policy in `Program.cs` requires an authenticated user
for every endpoint, so a newly added controller is protected by default and must opt out
explicitly with `[AllowAnonymous]`. Controllers then narrow further by role. Ownership is
enforced at the service boundary: every service method takes the owner's user id as a
parameter, and every posting, profile and resume query filters on it server-side.
Administrators manage skills and accounts but cannot read candidate postings or resumes.

---

## Project structure

```
src/
  JobAlign.Core/            Domain entities, enums, interfaces, business rules
    Abstractions/           Service contracts (13 interfaces)
    Entities/
      Identity/             ApplicationUser, ApplicationRole, RoleNames
      Postings/             JobPosting, PostingExtraction, corrections, skills, quality
      Profiles/             CandidateProfile, education/work/projects/certifications, Resume
      Skills/               MasterSkill, SkillAlias, Location, LocationAlias
      Matching/             MatchResult, SkillGap, RoadmapItem
      Admin/                AuditEntry
    Enums/                  Posting, Skill, Profile and Matching enums
    Extraction/             Extraction DTOs and field constants
    Matching/               ScoreCalculator, ScoringWeights
  JobAlign.Infrastructure/
    Ai/                     IAiChatClient, GeminiClient, AnthropicClient, extractors, prompts
    Data/                   JobAlignDbContext, configurations, seeders
    Identity/               Role seeding, candidate registration
    Migrations/             InitialSchema and model snapshot
    Services/               Service implementations
  JobAlign.Web/             ASP.NET Core MVC — controllers, views, view models
  JobAlign.Api/             ASP.NET Core Web API — service layer
tests/
  JobAlign.Tests/           Unit tests (156)
docs/
  JobAlign_Requirement_Analysis.pdf
```

---

## Everyday commands

```bash
dotnet build
```

```bash
dotnet run --project src/JobAlign.Web
```

```bash
dotnet test
```

### Changing the schema

Edit the entity in `JobAlign.Core`, adjust its configuration in
`JobAlign.Infrastructure/Data/Configurations/`, then create **one migration per logical change**
with a descriptive name:

```bash
dotnet ef migrations add AddPostingTagging --project src/JobAlign.Infrastructure --startup-project src/JobAlign.Web
```

```bash
dotnet ef database update --project src/JobAlign.Infrastructure --startup-project src/JobAlign.Web
```

Undo a migration you have created but not yet applied:

```bash
dotnet ef migrations remove --project src/JobAlign.Infrastructure --startup-project src/JobAlign.Web
```

### Resetting the database

Destructive — drops the database and everything in it:

```bash
dotnet ef database drop --force --project src/JobAlign.Infrastructure --startup-project src/JobAlign.Web
```

```bash
dotnet ef database update --project src/JobAlign.Infrastructure --startup-project src/JobAlign.Web
```

### Inspecting the database

The schema can be browsed in SQL Server Management Studio or the JetBrains Rider database tool
by connecting to `localhost` with Windows authentication and opening the `JobAlign` database.
From the command line:

```bash
sqlcmd -S localhost -E -C -I -d JobAlign -Q "SELECT name FROM sys.tables ORDER BY name;"
```

The `-I` flag matters. It turns on `QUOTED_IDENTIFIER`, which sqlcmd leaves off by default;
without it, any insert into `PostingExtractions` fails because that table carries a filtered
index. The .NET SQL client sets this option on automatically, so this affects sqlcmd only.

---

## Development guidelines

These constraints come from the requirements document and are enforced by the shape of the
schema as well as by code. Do not relax them for convenience.

**Raw text is immutable.** `JobPosting.RawText` and `.Reference` have private setters and are
assigned only in the constructor. Extractions are derived, disposable and regenerable; the
captured original is never rewritten.

**Never invent data.** Anything a posting does not state is stored as `null` and displayed as
"Not specified" — never `0`, never an empty string, never a guess. Every extracted column is
nullable on purpose. Adding `IsRequired()` to one of them breaks this rule.

**User corrections win.** A candidate's manual correction overrides the extracted value and
survives re-extraction. `PostingFieldCorrections` has its foreign key to `JobPostings`, not to
`PostingExtractions`, so deleting and regenerating extractions cannot touch corrections. That
is the entire reason for the table's shape.

**All skills resolve to a master skill.** Posting skills, resume skills and profile skills all
pass through the alias table and are stored as `MasterSkillId` foreign keys. `RawText` on a
skill row is provenance only and is never the identity of a skill.

**Resume-extracted skills are suggestions only.** They land in `ResumeSkillSuggestions`, a
different table from `ProfileSkills`. Scoring reads only `ProfileSkills`, so an unconfirmed
suggestion is structurally incapable of affecting a score.

**Never bind AI output directly to entities.** Deserialize to a DTO, validate enums and ranges,
then map. AI responses are untrusted input.

**Extract once, store the result.** Viewing a posting must never trigger a new AI call.

**Ownership is enforced server-side.** Every query for postings, resumes and profiles filters
by the authenticated user. Never rely on the UI to hide another user's data.

### Two independent status concepts

Conflating these breaks the exclusion rules, so they are separate columns:

- **`JobPosting.Status`** — the extraction lifecycle: `New` → `Pending` (on failure) →
  `Confirmed`. Only `Pending` is excluded from scoring, comparison and the dashboard. Note that
  `New` is not a statement about whether there is anything to score: a *successful* extraction
  leaves a posting at `New`, since it is confirmation that advances it, and re-extracting a
  `Confirmed` posting returns it to `New`. Scoring therefore keys on the current extraction's
  `RunStatus`, not on `Status`.
- **`JobPosting.ApplicationStatus`** — `Saved` / `Applied` / `Interview` / `Rejected` /
  `Closed`. Independent of extraction entirely.

### Schema conventions

- Enums are stored as **strings**, not integers, so the data is legible without a lookup table.
- `Restrict` on master-data foreign keys: skills and locations are deactivated, never deleted.
- Where two foreign keys point at the same table, one is `Cascade` and one is `NoAction`.
  SQL Server rejects multiple cascade paths, so this is a requirement, not a preference.
- A filtered unique index on `PostingExtractions` permits at most one current run per posting
  while retaining every prior run as history.

### Code conventions

- Async all the way down; suffix async methods with `Async`.
- Controllers stay thin — business logic belongs in `JobAlign.Core` services.
- Reference requirement IDs in commit messages and comments.
- One EF migration per logical schema change, named descriptively.

---

## Remaining work

Roughly in dependency order:

1. Salary and location normalization (FR-15, FR-16) — the raw values are captured and the
   normalized columns exist, but nothing populates them yet.
2. Side-by-side posting comparison (FR-49, FR-51).
3. Resume upload and parsing (FR-30 to FR-32).
4. Duplicate detection and posting quality checker (FR-22 to FR-26).
5. Link-based capture (FR-07) — most fragile, best left until last.
6. Web API controllers in `JobAlign.Api`.

Two known deviations from the stated design are worth addressing:

- Extraction currently blocks the HTTP request rather than running as a background job
  (NFR-01). The user waits out the full provider round-trip.
- `CandidateProfileService` invokes match scoring through reflection. That was a deliberate
  workaround so profile work could compile before the scoring service merged. The scoring
  service has since landed, so this can now be a plain constructor injection of
  `IMatchScoringService`.

---

## Troubleshooting

**`You must install or update .NET to run this application`, naming `Microsoft.AspNetCore.App 8.0.0`**
A project is targeting `net8.0` and only the .NET 10 runtime is installed. Set
`<TargetFramework>net10.0</TargetFramework>`, or install the .NET 8 SDK and ASP.NET Core 8
runtime.

**`INSERT failed because the following SET options have incorrect settings: 'QUOTED_IDENTIFIER'`**
You are in sqlcmd without `-I`. Add it. See "Inspecting the database" above.

**`A network-related or instance-specific error occurred while establishing a connection`**
SQL Server is not running, or the instance name is wrong. Check which instances are up:

```bash
powershell -Command "Get-Service | Where-Object { $_.Name -like 'MSSQL*' }"
```

Then match `Server=` in `appsettings.json` to a running instance.

**`Unable to create a 'DbContext' of type ...`**
Both `--project` and `--startup-project` are required on every `dotnet ef` command. The
DbContext lives in `JobAlign.Infrastructure`; the host and connection string live in
`JobAlign.Web`.

**`The Entity Framework tools version is older than that of the runtime`**
Harmless. To silence it:

```bash
dotnet tool update --global dotnet-ef
```

**`Introducing FOREIGN KEY constraint may cause cycles or multiple cascade paths`**
A new relationship added a second cascade path to a table SQL Server already cascades into. Set
one end to `DeleteBehavior.NoAction` in its configuration — `PostingRelations` and
`MatchResults` are existing examples of the fix.

**Extraction returns obviously placeholder data**
No API key is configured, so the stub extractor is active. See "Configure the AI provider"
above.

**Extraction fails with truncated or unparseable JSON**
The reply hit the token ceiling before the object closed. Raise `Ai:MaxOutputTokens`, or set
`Ai:ThinkingBudget` to `0` so the whole ceiling is spent on the answer rather than on
reasoning.
