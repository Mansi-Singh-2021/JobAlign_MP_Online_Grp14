# JobAlign — How to Use

AI-powered job and skill alignment platform. Captures unstructured job postings, extracts them
into structured data, and scores how well a candidate's skill profile matches each role.

Built against SRS `JA-SRS-001 v1.0` — see [docs/JobAlign_Requirement_Analysis.pdf](docs/JobAlign_Requirement_Analysis.pdf).

---

## Current status

**The database layer is complete. The user interface is not built yet.**

Read that before following the run instructions, so the result is not a surprise.

| Area | State |
|---|---|
| Solution, 5 projects, references | Done |
| Domain entities (all of SRS §10) | Done |
| EF Core DbContext + configurations | Done |
| `InitialSchema` migration, applied | Done — 31 tables |
| Identity + roles registered in `Program.cs` | Done |
| Register / sign-in screens | **Not built** |
| Posting capture, extraction, matching, dashboard | **Not built** |
| AI integration | **Not built** — deliberately deferred |

Running the app right now gives you the default ASP.NET Core MVC welcome page. It connects to
the database successfully, but there are no JobAlign screens behind it yet. The next step is
step 1 in the build order at the bottom of this file.

---

## Prerequisites

| Requirement | Version used here | Check with |
|---|---|---|
| .NET SDK | 10.0.301 | `dotnet --version` |
| SQL Server | 2025 (17.0), local instance | `sqlcmd -S localhost -E -C -Q "SELECT @@VERSION"` |
| EF Core CLI tools | 10.0.10+ | `dotnet ef --version` |
| Git | 2.49 | `git --version` |

If `dotnet ef` is missing:

```bash
dotnet tool install --global dotnet-ef
```

> **On .NET versions:** this project targets **.NET 10**, not .NET 8. A `net8.0` build succeeds
> but will not start on this machine — there is no ASP.NET Core 8 runtime installed. SRS §9.3
> mandates the stack but not a framework version, so .NET 10 is compliant.

---

## First-time setup

### 1. Restore and build

```bash
dotnet build
```

Expect `Build succeeded. 0 Warning(s) 0 Error(s)`.

### 2. Point at your SQL Server

The connection string lives in [src/JobAlign.Web/appsettings.json](src/JobAlign.Web/appsettings.json)
under `ConnectionStrings:JobAlignDb`. The default uses the local default instance with Windows
authentication:

```
Server=localhost;Database=JobAlign;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true
```

Change `Server=` if you use a named instance — for example `Server=localhost\MSSQLSERVER01`.

This string contains no secret, which is why it is safe in `appsettings.json`. **API keys are
different** — those go in user-secrets locally and environment variables in deployment, never
in a committed file.

### 3. Create the database

```bash
dotnet ef database update --project src/JobAlign.Infrastructure --startup-project src/JobAlign.Web
```

This creates the `JobAlign` database and all 31 tables.

### 4. Confirm it worked

```bash
sqlcmd -S localhost -E -C -I -d JobAlign -Q "SELECT COUNT(*) AS Tables FROM sys.tables;"
```

Expect `31`.

### 5. Run the app

```bash
dotnet run --project src/JobAlign.Web
```

Then open the URL it prints (typically `https://localhost:7xxx`). Stop with `Ctrl+C`.

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

```bash
sqlcmd -S localhost -E -C -I -d JobAlign -Q "SELECT name FROM sys.tables ORDER BY name;"
```

The `-I` flag matters. It turns on `QUOTED_IDENTIFIER`, which sqlcmd leaves off by default;
without it, any insert into `PostingExtractions` fails because that table carries a filtered
index. The .NET SQL client sets this on automatically, so this affects sqlcmd only.

---

## Project structure

```
src/
  JobAlign.Core/            Domain entities, enums, interfaces, business rules
    Entities/
      Identity/             ApplicationUser, ApplicationRole, RoleNames
      Postings/             JobPosting, PostingExtraction, corrections, skills, duplicates, quality
      Profiles/             CandidateProfile, education/work/projects/certs, ProfileSkill, Resume
      Skills/               MasterSkill, SkillAlias, Location, LocationAlias
      Matching/             MatchResult, SkillGap, RoadmapItem
      Admin/                AuditEntry
    Enums/                  Posting, Skill, Profile and Matching enums
  JobAlign.Infrastructure/  EF Core — DbContext, configurations, migrations
    Data/
      JobAlignDbContext.cs
      Configurations/       One IEntityTypeConfiguration per entity
    Migrations/             InitialSchema + model snapshot
  JobAlign.Web/             ASP.NET Core MVC — the UI, and the EF startup project
  JobAlign.Api/             ASP.NET Core Web API — service layer
tests/
  JobAlign.Tests/           Unit tests
docs/
  JobAlign_Requirement_Analysis.pdf
```

`Infrastructure` depends on `Core`. `Web` and `Api` depend on both. `Core` depends on nothing —
business rules stay free of EF and ASP.NET.

---

## Working on this project

[CLAUDE.md](CLAUDE.md) is the working brief: non-negotiable rules, the AI extraction contract,
scoring definitions, and the build order. **Read it before changing anything.** A few rules are
easy to break by accident:

- Extracted columns are nullable on purpose. A detail the posting never stated is `null` and
  displays as "Not specified" — never `0`, never `""`, never a guess (BR-02, FR-17).
- `JobPosting.RawText` is written once and never modified (BR-01).
- Candidate corrections attach to the **posting**, not to an extraction run, so they survive
  re-extraction (BR-03).
- Every skill resolves to a `MasterSkill` foreign key. Never store a free-text skill as an
  identity (BR-04).
- Every posting/profile/resume query filters by the signed-in user server-side (BR-09, NFR-04).

### Build order

Work in sequence; do not jump ahead.

0. ~~Schema, migration, database~~ — **done**
1. Auth with roles — registration, sign-in, role seeding (FR-01 to FR-05) ← **next**
2. Paste a posting → save → list it, with no AI at all (FR-06, FR-08, FR-09)
3. `IJobExtractor` + `StubExtractor`, and the full review/correct UI against the stub (FR-12, FR-18)
4. Real `AiExtractor` behind the same interface
5. Master skills, aliases, resolution (FR-14, FR-57, FR-58)
6. Salary and location normalization (FR-15, FR-16)
7. Candidate profile and skills (FR-27 to FR-29, FR-33)
8. Match scoring (FR-35 to FR-41)
9. Skill gap and roadmap (FR-42 to FR-46)
10. Dashboard, filter, sort, compare (FR-48 to FR-54)
11. Resume upload and parsing (FR-30 to FR-32)
12. Duplicate detection and quality checker (FR-22 to FR-26)
13. Link-based capture (FR-07) — last, it is the most fragile

---

## Troubleshooting

**`You must install or update .NET to run this application` naming `Microsoft.AspNetCore.App 8.0.0`**
A project is targeting `net8.0`. Only the .NET 10 runtime is installed. Set
`<TargetFramework>net10.0</TargetFramework>`, or install the .NET 8 SDK and ASP.NET Core 8 runtime.

**`INSERT failed because the following SET options have incorrect settings: 'QUOTED_IDENTIFIER'`**
You are in sqlcmd without `-I`. Add it. See "Inspecting the database" above.

**`A network-related or instance-specific error occurred while establishing a connection`**
SQL Server is not running, or the instance name is wrong. Check:

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
one end to `DeleteBehavior.NoAction` in its configuration — `PostingRelations` and `MatchResults`
are existing examples of the fix.
