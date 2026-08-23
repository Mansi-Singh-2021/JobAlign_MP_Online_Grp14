# Role B — Master skills, aliases and resolution

**Story:** US-03 · **Points:** 5 · **Branch:** `feat/b-skills`
**Critical path:** yes — you and A own Wave 0. A, C and D all depend on your resolver.

> **STATUS: complete.** `SkillResolver`, `MasterSkillSeeder` (46 skills, 35 aliases), the
> FR-57/FR-58 administrator screens and 23 tests are on `main` and green. Nothing in this
> lane is outstanding.
>
> One addition worth knowing about: **merging repoints existing `PostingSkills` and
> `ProfileSkills` to the surviving skill.** Scoring compares `MasterSkillId` directly, so
> without that the merge would be invisible to match scores — see `SkillAdminService`.

> Read [00-SHARED-BRIEF.md](00-SHARED-BRIEF.md) and [01-CONTRACTS.md](01-CONTRACTS.md) first.

---

## What you own

The single source of truth for what a skill *is*. **BR-04 says every skill anywhere in the
system — posting, resume or profile — resolves to exactly one `MasterSkill` row.** You build
the thing that makes that true, plus the administrator screens for maintaining it.

Your resolver is the most-called piece of code in the project. Three other members compile
against it.

## Requirements

| ID | Requirement | Pri |
|---|---|---|
| FR-14 | Resolve skill name variations to a single master skill — "C Sharp", "C-Sharp", "C#" are one skill | M |
| FR-29 | Resolve profile skills by the same rules as posting skills | M |
| FR-57 | Administrator can add, edit and deactivate master skill entries | M |
| FR-58 | Administrator can maintain aliases and merge two master skills | M |

Business rule: **BR-04**. Convention: master data is **deactivated, never deleted**
(the foreign keys are `Restrict` — a delete will throw, by design).

## Files you own

Create:
```
src/JobAlign.Core/Abstractions/ISkillResolver.cs
src/JobAlign.Infrastructure/Services/SkillResolver.cs
src/JobAlign.Infrastructure/Data/MasterSkillSeeder.cs
src/JobAlign.Web/Controllers/SkillsAdminController.cs
src/JobAlign.Web/Models/Admin/SkillAdminViewModels.cs
src/JobAlign.Web/Views/SkillsAdmin/Index.cshtml
src/JobAlign.Web/Views/SkillsAdmin/Edit.cshtml
src/JobAlign.Web/Views/SkillsAdmin/Aliases.cshtml
src/JobAlign.Web/Views/SkillsAdmin/Merge.cshtml
tests/JobAlign.Tests/SkillResolverTests.cs
```

Edit (announce first):
- `src/JobAlign.Infrastructure/DependencyInjection.cs` — append one line
- `src/JobAlign.Web/Program.cs` — call the seeder next to `RoleSeeder.SeedAsync`

**Do not touch:** extraction, profile, matching or dashboard code.

---

## Wave 0 — do this first, with Member A, before anything else

1. `ISkillResolver` and `SkillResolution`, copied verbatim from `01-CONTRACTS.md`.
2. `SkillResolver` with exact + alias lookup working. This is not a stub — get it right now,
   refine later.
3. `MasterSkillSeeder` with **~60 skills and their aliases**, idempotent, called at startup
   next to `RoleSeeder.SeedAsync`.
4. Give Member A the list of seeded skill names so `StubExtractor` returns skills that
   actually resolve. **Do this explicitly** — if the stub returns skills you have not
   seeded, D's scoring silently produces nothing and four people debug a non-problem.
5. Build clean, tests green, push, tell the team.

Target: 90 minutes.

---

## Task list

### 1. Normalization — get this right, everything rests on it

```csharp
string Normalize(string rawSkillText);
```

Lowercase, then strip everything that is not a letter or a digit.

| Input | Normalized |
|---|---|
| `C#` | `csharp` |
| `C Sharp` | `csharp` |
| `C-Sharp` | `csharp` |
| `ASP .NET Core` | `aspnetcore` |
| `Node.js` | `nodejs` |
| `  react  ` | `react` |

`C#` -> `csharp` needs a special case: stripping `#` leaves `c`, which would collide with
the C language. Map `#` to `sharp` and `+` to `plus` **before** stripping, so `C#` -> `csharp`
and `C++` -> `cplusplus`.

This method is on the public interface because every `MasterSkill.NormalizedName` and
`SkillAlias.NormalizedAlias` row must be written using exactly it. If seeding normalizes
differently from lookup, nothing resolves and the failure is silent.

### 2. `SkillResolver` lookup order

1. Active `MasterSkill` where `NormalizedName` matches -> hit.
2. `SkillAlias` where `NormalizedAlias` matches -> its `MasterSkill`.
3. If the hit has `MergedIntoMasterSkillId` set, follow it (FR-58). Follow at most a few
   hops and guard against a cycle.
4. Otherwise return `SkillResolution(rawText, null, null)` — unresolved.

**Unresolved is a normal outcome, not an error.** Never auto-create a `MasterSkill` from
extracted text; that would let AI output define the master list, which is exactly what BR-04
forbids. Log unresolved names so an administrator can add them.

`ResolveManyAsync` must be **one database round trip**, not one per skill. Normalize all the
inputs, then a single `WHERE NormalizedName IN (...)` and a single alias query. Extraction
resolves a dozen skills at a time and A will call this on every run.

### 3. Seed data

~60 skills across categories, each with realistic aliases. Suggested coverage:

- **Language:** C#, Java, Python, JavaScript, TypeScript, SQL, Go, C++, Kotlin, PHP, Ruby
- **Framework:** ASP.NET Core, .NET, Entity Framework Core, React, Angular, Vue, Node.js,
  Spring Boot, Django, Flask
- **Cloud:** Azure, AWS, GCP, Docker, Kubernetes, Terraform
- **Data:** SQL Server, PostgreSQL, MySQL, MongoDB, Redis, Power BI
- **Practice:** REST API, Microservices, CI/CD, Git, Agile, Scrum, Unit Testing, TDD
- **Tools:** Visual Studio, Azure DevOps, Jira, Jenkins, GitHub Actions

Aliases that matter: `C Sharp`/`C-Sharp`/`.NET C#` -> C#; `ASP.NET`/`ASP .NET Core`/
`AspNet Core` -> ASP.NET Core; `EF Core`/`EntityFramework` -> Entity Framework Core;
`K8s` -> Kubernetes; `Postgres` -> PostgreSQL; `MSSQL`/`MS SQL Server` -> SQL Server;
`JS` -> JavaScript; `TS` -> TypeScript; `RESTful API`/`REST` -> REST API.

Seeder must be **idempotent** — check before insert, same shape as `RoleSeeder`. It runs on
every startup.

### 4. Admin screens (FR-57, FR-58)

`[Authorize(Roles = RoleNames.Administrator)]` on the controller. Section 4.3 of the SRS
gives administrators master-data management and explicitly **denies** them candidate
postings and resumes — do not add any posting or profile query here (BR-09).

- **Index** — list, search, filter active/inactive
- **Edit** — add or edit name, category; deactivate/reactivate. Never a delete button.
- **Aliases** — add and remove aliases for a skill; reject an alias that already resolves
  elsewhere (the unique index will throw — catch it and show a message)
- **Merge** (FR-58) — pick source and target; set `MergedIntoMasterSkillId` on the source,
  move its aliases to the target, deactivate the source. **Do not delete the source row** —
  existing postings and profiles reference it and must stay explainable.

---

## Acceptance criteria

- [ ] "C#", "C Sharp", "C-Sharp" and "c sharp" all resolve to the same `MasterSkillId`
- [ ] An unrecognised skill returns unresolved rather than throwing or creating a row
- [ ] `ResolveManyAsync` on 12 skills issues a constant number of queries, not 12
- [ ] Seeder runs twice with no duplicates
- [ ] An administrator can add a skill, add an alias, and see the alias resolve
- [ ] Merging A into B makes A's aliases resolve to B, with A's row still present
- [ ] A candidate hitting `/SkillsAdmin` gets Access Denied

## Tests to write

```
Normalize_maps_csharp_variants_to_one_form
Normalize_maps_hash_to_sharp_and_plus_to_plus
Resolve_finds_a_skill_by_its_canonical_name
Resolve_finds_a_skill_by_an_alias
Resolve_follows_a_merged_skill_to_its_target
Resolve_returns_unresolved_for_an_unknown_skill
Resolve_ignores_a_deactivated_skill
ResolveMany_returns_one_result_per_input_in_order
```

`Normalize` is pure — test it directly and heavily. It is the highest-leverage function in
your slice.

## Dependencies

| You need | From | Until then |
|---|---|---|
| Nothing | — | You are the root of the dependency graph |

**You provide to others:** `ISkillResolver` to A (posting skills), C (profile skills) and
indirectly D (scoring compares `MasterSkillId` values). Seeded skills to A's stub.

You are unblocked from minute one. Every delay of yours is four people's delay.

## Stretch, if you finish early

- Fuzzy matching for near-misses ("Kubernets" -> Kubernetes) — behind a confidence threshold,
  surfaced as a suggestion, never applied silently
- An "unresolved skills" admin report from your logs, feeding FR-59
