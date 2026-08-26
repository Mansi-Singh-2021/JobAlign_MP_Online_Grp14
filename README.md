# JobAlign

JobAlign is a job and skill alignment platform. Candidates capture job postings as raw text,
an AI service extracts and structures the detail, and the platform scores how well the
candidate's skill profile matches each role — surfacing match scores, concrete skill gaps, and
a prioritized learning roadmap.

Built to SRS `JA-SRS-001 v1.0`. See [docs/JobAlign_Requirement_Analysis.pdf](docs/JobAlign_Requirement_Analysis.pdf).

---

## Features

**Accounts and access**
Email-based registration, sign-in and sign-out, self-service password reset, and role-based
access control across two roles: Candidate and Administrator. Sessions expire after 60 minutes
of inactivity.

**Posting capture and extraction**
Candidates paste a job posting as raw text. An AI extraction pass structures it into job title,
company, location, remote policy, experience range, salary, responsibilities, summary and a
resolved skill list, recording a per-field confidence score for each. The captured original is
stored immutably and every extraction run is retained as history, so extraction can be re-run
without losing anything.

**Review and correction**
Candidates review the extracted detail field by field and correct anything the extractor got
wrong. Corrections attach to the posting rather than to an extraction run, so they persist
across re-extraction. Confirming a posting makes it eligible for scoring.

**Skill taxonomy**
A curated master skill list with alias resolution — a posting saying "MSSQL", "MS SQL" or
"SQL Server" resolves to a single canonical skill. Administrators maintain the taxonomy:
add, edit and deactivate skills, manage aliases, and merge duplicates.

**Candidate profile**
Skills with proficiency and years, education, work experience, projects and certifications.
Years of experience are derived from the recorded work history.

**Match scoring**
Every confirmed posting is scored on required skills, preferred skills and experience, combined
into a weighted overall score. Scores are versioned against the weighting configuration that
produced them, so any stored score can be explained. Editing a profile rescores the entire
posting library automatically.

**Skill gaps and roadmap**
Gaps behind each score are itemized and rolled up into a prioritized, cross-posting learning
roadmap with per-item status tracking. AI-generated narrative feedback accompanies each match.

**Dashboard and library management**
An overview of total, pending and confirmed postings with average and best match scores, plus
the current roadmap. The posting library supports filtering by work mode, location and
experience range, sorting by date or match score, application status tracking, archiving and
deletion.

---

## Requirements

| Component | Version | Verify with |
|---|---|---|
| .NET SDK | 10.0 | `dotnet --version` |
| SQL Server | 2019 or later (2025 verified) | `sqlcmd -S localhost -E -C -Q "SELECT @@VERSION"` |
| EF Core CLI tools | 10.0 | `dotnet ef --version` |

Install the EF Core CLI if it is missing:

```bash
dotnet tool install --global dotnet-ef
```

> This project targets **.NET 10**. SRS §9.3 mandates the stack — C#, ASP.NET Core MVC and
> Web API, EF Core, SQL Server, Bootstrap — but does not pin a framework version.

---

## Quick start

### 1. Build

```bash
dotnet build
```

### 2. Configure the database connection

Set `ConnectionStrings:JobAlignDb` in
[src/JobAlign.Web/appsettings.json](src/JobAlign.Web/appsettings.json). The default targets a
local default instance with Windows authentication:

```
Server=localhost;Database=JobAlign;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true
```

Change `Server=` for a named instance — for example `Server=localhost\MSSQLSERVER01`.

### 3. Configure the AI provider

```bash
dotnet user-secrets set "Ai:ApiKey" "<your-key>" --project src/JobAlign.Web
```

Without a key the application runs against deterministic stub implementations instead of a live
provider. See [Configuration](#configuration) for the full settings reference.

### 4. Create the database

```bash
dotnet ef database update --project src/JobAlign.Infrastructure --startup-project src/JobAlign.Web
```

This creates the `JobAlign` database and its 31 tables.

### 5. Run

```bash
dotnet run --project src/JobAlign.Web
```

Open the URL printed to the console, typically `https://localhost:7xxx`.

Roles and the master skill taxonomy are seeded automatically at startup. Seeding is idempotent
and runs safely on every boot.

---

## Configuration

All settings bind from the standard ASP.NET Core configuration chain: `appsettings.json`,
`appsettings.{Environment}.json`, user-secrets (Development only), then environment variables.
Later sources win.

In environment variables, replace `:` with a double underscore — `Ai:ApiKey` becomes
`Ai__ApiKey`.

| Setting | Default | Secret | Purpose |
|---|---|---|---|
| `ConnectionStrings:JobAlignDb` | — | Depends on auth mode | SQL Server connection. Required; startup fails without it. |
| `Ai:Provider` | `Gemini` | No | Provider to call. `Gemini` or `Anthropic`. |
| `Ai:ApiKey` | — | **Yes** | Provider API key. Absent means stub mode. |
| `Ai:Model` | Provider default | No | Model identifier. |
| `Ai:BaseUrl` | Provider default | No | API endpoint override. |
| `Ai:TimeoutSeconds` | `30` | No | Per-request timeout. |
| `Ai:MaxOutputTokens` | `8192` | No | Response ceiling. Extraction returns a large JSON object; too low truncates it. |
| `Ai:ThinkingBudget` | `null` | No | Gemini only. `0` spends the full token ceiling on the answer rather than on reasoning. |
| `Seed:AdminEmail` | — | No | Development-only administrator bootstrap. Ignored outside Development. |
| `Seed:AdminPassword` | — | **Yes** | Development-only administrator bootstrap. Ignored outside Development. |

### Secrets handling

`Ai:ApiKey` and any password-bearing connection string must never be committed. Use
user-secrets in development and environment variables or a managed secret store in deployment.
The default connection string carries no credential — it uses Windows integrated authentication
— which is why it is safe in `appsettings.json`.

### Provider selection

`GeminiClient` and `AnthropicClient` both implement `IAiChatClient`, so switching providers is
a configuration change, never a code change. `Ai:Model` and `Ai:BaseUrl` each fall back to the
selected provider's default, so a working configuration is a provider and a key. Vendors retire
model identifiers periodically; that is an `Ai:Model` edit.

### Degraded mode

When `Ai:ApiKey` is absent, `IJobExtractor` and `IFeedbackGenerator` resolve to stub
implementations. The application starts and every screen works, but extraction returns
deterministic placeholder data rather than real analysis. This is a deliberate fallback for
development and CI, not a supported production configuration.

---

## Deployment

### Environment

Set the environment explicitly. Outside Development, HSTS is enabled and the developer
exception page is replaced by the standard error handler.

```
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__JobAlignDb=<connection string>
Ai__ApiKey=<provider key>
```

HTTPS redirection is enforced in all environments; terminate TLS at the host or reverse proxy
and forward the appropriate headers.

### Database migrations

Do not run `dotnet ef database update` against production from a developer machine. Generate an
idempotent script and apply it through your release process:

```bash
dotnet ef migrations script --idempotent --project src/JobAlign.Infrastructure --startup-project src/JobAlign.Web --output migrate.sql
```

The application does not apply migrations at startup. It does run idempotent reference-data
seeding — roles and the master skill taxonomy — on every boot.

### Bootstrapping the first administrator

Public registration always creates a Candidate. The development administrator seeder is
hard-disabled outside the Development environment, by design: a seeded account with a
configured password is a back door anywhere else.

To grant the Administrator role in a deployed environment, register the account through the
application, then promote it directly:

```sql
INSERT INTO AspNetUserRoles (UserId, RoleId)
SELECT u.Id, r.Id
FROM AspNetUsers u, AspNetRoles r
WHERE u.NormalizedEmail = 'ADMIN@EXAMPLE.COM'
  AND r.NormalizedName = 'ADMINISTRATOR'
  AND NOT EXISTS (
      SELECT 1 FROM AspNetUserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId = r.Id
  );
```

Administrators hold no candidate profile and cannot read candidate postings or resumes.

### Email delivery

The shipped `IAppEmailSender` implementation writes messages to the application log rather than
sending them. Password reset therefore does not deliver mail out of the box. Before going live,
register a real implementation — SMTP or a transactional email provider — against
`IAppEmailSender` in `AddJobAlignInfrastructure`, replacing `LoggingEmailSender`.

### Operational notes

- **Outbound network access** to the configured AI provider is required. Provider failures are
  handled gracefully: the posting is preserved, marked `Pending`, and the reason recorded for
  retry.
- **Extraction is synchronous** and holds the request for the duration of the provider call.
  Size request timeouts at the proxy accordingly, allowing for `Ai:TimeoutSeconds` plus one
  retry.
- **Only raw posting text** is transmitted to the AI provider. No account identifier, email
  address or profile data leaves the system.
- **Authentication cookies** use a 60-minute sliding expiration.

---

## Architecture

Four projects on .NET 10, layered so dependencies point inward only:

```
JobAlign.Web (MVC)   ──┐
                       ├──▶  JobAlign.Infrastructure  ──▶  JobAlign.Core
JobAlign.Api (Web API) ┘                                   (no dependencies)
```

**JobAlign.Core** holds domain entities, enums, service contracts and pure business logic. It
references no packages — no EF Core, no ASP.NET, no HTTP. `ScoreCalculator` is a dependency-free
static class, which is why scoring is the most heavily unit-tested area of the solution.

**JobAlign.Infrastructure** holds every implementation that touches the outside world: the EF
Core `DbContext`, entity configurations, migrations, service implementations, AI clients and
reference-data seeders. A single `AddJobAlignInfrastructure()` extension registers all of it.

**JobAlign.Web** is the ASP.NET Core MVC application and the EF Core startup project.

**JobAlign.Api** is the Web API host, sharing the same Core and Infrastructure layers.

### Key abstractions

- **`IJobExtractor`** — implemented by `AiExtractor` (live provider call) and `StubExtractor`
  (deterministic, offline). Resolution is decided at registration time by whether an API key is
  configured.
- **`IAiChatClient`** — sits beneath the extractor and hides each provider's wire format.
  `GeminiClient` and `AnthropicClient` are interchangeable; callers never learn which one
  answered.
- **`ISkillResolver`** — the single path from free text to a canonical `MasterSkill`, applying
  exact match, then alias lookup, then merge redirection.

### Request flow

1. **Capture** — the candidate submits raw text. `JobPostingService` stores it and assigns a
   human-readable reference. No AI call occurs at this stage.
2. **Extraction** — `ExtractionService` loads the posting filtered by owner, passes only the raw
   text to `IJobExtractor`, deserializes the response into a validated DTO, then maps to
   entities inside a transaction. Skills resolve to master-skill foreign keys. Prior runs are
   retained; exactly one is flagged current.
3. **Review** — the candidate corrects any extracted field. Corrections are stored against the
   posting and survive re-extraction. Confirmation makes the posting eligible for scoring.
4. **Scoring** — `MatchScoringService` computes component and overall scores via
   `ScoreCalculator` and stamps the weighting version used.
5. **Gaps and roadmap** — `SkillGapService` itemizes the gaps behind each score and builds a
   prioritized roadmap; `AiFeedbackGenerator` produces the narrative feedback.
6. **Recalculation** — a profile change rescores the entire posting library. A scoring failure
   is logged and never rolls back the profile change that triggered it.

### Match scoring

| Component | Weight | Not scored when |
|---|---|---|
| Required skills | 0.60 | the posting lists no required skills |
| Preferred skills | 0.15 | the posting lists no preferred skills |
| Experience | 0.25 | either side leaves years unstated |
| **Overall** | — | all three components are unscored |

The overall score is a weighted mean across only the components present, so a posting that
lists no preferred skills is not penalised for the omission. An unscored component is null, and
null is never treated as zero. Every stored score records the weighting version that produced
it, so historical scores remain explainable after the weights change.

### Security model

Authorization fails closed. A fallback policy requires an authenticated user for every endpoint,
so a newly added controller is protected by default and must opt out explicitly with
`[AllowAnonymous]`. Controllers narrow further by role.

Ownership is enforced at the service boundary, not in the UI: every service method takes the
owner's user id, and every posting, profile and resume query filters on it server-side.
Passwords are stored as salted one-way hashes via ASP.NET Core Identity, with account lockout
after five failed attempts. All traffic is redirected to HTTPS.

---

## Project structure

```
src/
  JobAlign.Core/            Domain entities, enums, contracts, business rules
    Abstractions/           Service contracts
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
    Ai/                     IAiChatClient, provider clients, extractors, prompts
    Data/                   JobAlignDbContext, entity configurations, seeders
    Identity/               Role seeding, candidate registration
    Migrations/             EF Core migrations and model snapshot
    Services/               Service implementations
  JobAlign.Web/             ASP.NET Core MVC — controllers, views, view models
  JobAlign.Api/             ASP.NET Core Web API
tests/
  JobAlign.Tests/           Unit tests
docs/
  JobAlign_Requirement_Analysis.pdf
```

---

## Development

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project src/JobAlign.Web
```

### Schema changes

Edit the entity in `JobAlign.Core`, adjust its configuration in
`JobAlign.Infrastructure/Data/Configurations/`, then create **one migration per logical change**
with a descriptive name:

```bash
dotnet ef migrations add AddPostingTagging --project src/JobAlign.Infrastructure --startup-project src/JobAlign.Web
```

```bash
dotnet ef database update --project src/JobAlign.Infrastructure --startup-project src/JobAlign.Web
```

Remove a migration created but not yet applied:

```bash
dotnet ef migrations remove --project src/JobAlign.Infrastructure --startup-project src/JobAlign.Web
```

### Resetting a local database

Destructive — drops the database and all data in it:

```bash
dotnet ef database drop --force --project src/JobAlign.Infrastructure --startup-project src/JobAlign.Web
```

### Inspecting the database

Browse the schema in SQL Server Management Studio or the JetBrains Rider database tool by
connecting to the server with Windows authentication and opening the `JobAlign` database. From
the command line:

```bash
sqlcmd -S localhost -E -C -I -d JobAlign -Q "SELECT name FROM sys.tables ORDER BY name;"
```

The `-I` flag matters. It enables `QUOTED_IDENTIFIER`, which sqlcmd leaves off by default;
without it, any insert into `PostingExtractions` fails because that table carries a filtered
index. The .NET SQL client sets this option automatically, so it affects sqlcmd only.

### Conventions

- Async throughout; suffix async methods with `Async`.
- Controllers stay thin — business logic belongs in `JobAlign.Core` services.
- One EF Core migration per logical schema change, named descriptively.
- Reference requirement identifiers in commit messages and comments.

---

## Domain invariants

These rules come from the requirements document and are enforced by the schema as well as by
code. Changing them changes the product's behaviour, not just its implementation.

**Captured text is immutable.** `JobPosting.RawText` and `.Reference` have private setters
assigned only in the constructor. Extractions are derived, disposable and regenerable; the
captured original is never rewritten.

**Nothing is invented.** Anything a posting does not state is stored as `null` and displayed as
"Not specified" — never `0`, never an empty string, never an inferred value. Every extracted
column is nullable deliberately; marking one required breaks this rule.

**User corrections take precedence.** A manual correction overrides the extracted value and
survives re-extraction. `PostingFieldCorrections` keys to `JobPostings` rather than to
`PostingExtractions` specifically so that regenerating extractions cannot disturb corrections.

**Skills are canonical.** Posting, resume and profile skills all resolve through the alias table
and are stored as `MasterSkillId` foreign keys. Raw text on a skill row is provenance only,
never identity.

**Resume-derived skills are suggestions.** They are held separately from profile skills.
Scoring reads confirmed profile skills only, so an unreviewed suggestion cannot affect a score.

**AI output is untrusted input.** Deserialize to a DTO, validate enums and ranges, then map.
Never bind a provider response directly to an entity.

**Extraction is not idempotent-on-read.** Viewing a posting never triggers a provider call;
extraction runs only on explicit request and its result is persisted.

**Ownership is enforced server-side.** Every posting, profile and resume query filters by the
authenticated user. UI-level hiding is not access control.

### Two independent status concepts

These are separate columns and conflating them breaks the exclusion rules:

- **`JobPosting.Status`** — the extraction lifecycle: `New`, `Pending` on extraction failure,
  `Confirmed` once the candidate confirms. Only `Pending` is excluded from scoring, comparison
  and dashboard metrics. Note that `New` says nothing about whether a posting has been
  extracted: a *successful* extraction leaves the posting at `New`, since confirmation is what
  advances it, and re-extracting a `Confirmed` posting returns it to `New`. Scoring therefore
  keys on the current extraction's `RunStatus`, not on `Status`.
- **`JobPosting.ApplicationStatus`** — `Saved`, `Applied`, `Interview`, `Rejected`, `Closed`.
  Entirely independent of extraction.

### Schema conventions

- Enums are persisted as **strings**, not integers, so stored data is legible without a lookup.
- Master-data foreign keys use `Restrict`: skills and locations are deactivated, never deleted.
- Where two foreign keys reach the same table, one is `Cascade` and one is `NoAction`.
  SQL Server rejects multiple cascade paths, so this is a constraint, not a preference.
- A filtered unique index on `PostingExtractions` permits at most one current run per posting
  while retaining every prior run as history.

---

## Troubleshooting

**`You must install or update .NET to run this application`, naming `Microsoft.AspNetCore.App 8.0.0`**
A project targets `net8.0` while only the .NET 10 runtime is installed. Set
`<TargetFramework>net10.0</TargetFramework>`, or install the ASP.NET Core 8 runtime.

**`INSERT failed because the following SET options have incorrect settings: 'QUOTED_IDENTIFIER'`**
You are in sqlcmd without `-I`. Add it. See [Inspecting the database](#inspecting-the-database).

**`A network-related or instance-specific error occurred while establishing a connection`**
SQL Server is not running, or the instance name is wrong. List running instances:

```bash
powershell -Command "Get-Service | Where-Object { $_.Name -like 'MSSQL*' }"
```

Then match `Server=` in the connection string to a running instance.

**`Unable to create a 'DbContext' of type ...`**
Both `--project` and `--startup-project` are required on every `dotnet ef` command. The
DbContext lives in `JobAlign.Infrastructure`; the host and connection string live in
`JobAlign.Web`.

**`Introducing FOREIGN KEY constraint may cause cycles or multiple cascade paths`**
A new relationship added a second cascade path into a table SQL Server already cascades into.
Set one end to `DeleteBehavior.NoAction` in its configuration — `PostingRelations` and
`MatchResults` are existing examples.

**Extraction returns placeholder data**
No API key is configured, so the stub extractor is active. See [Configuration](#configuration).

**Extraction fails with truncated or unparseable JSON**
The response hit the token ceiling before the object closed. Raise `Ai:MaxOutputTokens`, or set
`Ai:ThinkingBudget` to `0` so the full ceiling is spent on the answer rather than on reasoning.

**Password reset emails never arrive**
The default `IAppEmailSender` writes to the application log instead of sending. See
[Email delivery](#email-delivery).
