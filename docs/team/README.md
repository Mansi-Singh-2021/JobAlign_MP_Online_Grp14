# JobAlign — 2-Day Parallel Build Plan

Six members, two days, AI-assisted. This file is for the **team lead**. Each member
gets their own handout; every member's AI reads [00-SHARED-BRIEF.md](00-SHARED-BRIEF.md)
and [01-CONTRACTS.md](01-CONTRACTS.md) first.

---

## Where the project stands

| Build-order step | State |
|---|---|
| 0. Schema, migration, database (31 tables) | Done |
| 1. Auth with roles — register, sign-in, sign-out, reset (FR-01 to FR-05) | Done |
| 2. Paste a posting → save → list → archive → delete (FR-06, FR-08 to FR-11) | Done |
| 3–13. Everything else | **This plan** |

Build is clean (0 warnings), 14 tests pass, database applied.

## What we are building in two days

The sprint plan's own MVP: user stories **US-02, US-03, US-05, US-06, US-07, US-08**
(36 of 42 story points; US-01 and US-04 are the work already done). That is one story
per member.

Everything else in the SRS — link capture, resume upload, duplicate detection, quality
scoring, posting comparison, admin reports — is listed as **Stretch** inside the relevant
member's handout. Nothing is deleted from scope; it is ordered.

---

## The split

| | Member | Story | Handout | Owns | Pts |
|---|---|---|---|---|---|
| **A** | | US-05a | [role-a-extraction.md](role-a-extraction.md) | Extraction pipeline, stub extractor, review & correct UI | 5 |
| **B** | | US-03 | [role-b-skills.md](role-b-skills.md) | Master skills, aliases, resolver, skill admin | 5 |
| **C** | | US-02 | [role-c-profile.md](role-c-profile.md) | Candidate profile, profile skills, experience total | 5 |
| **D** | | US-06 | [role-d-matching.md](role-d-matching.md) | Match scoring engine — the four scores | 8 |
| **E** | | US-07 | [role-e-dashboard.md](role-e-dashboard.md) | Skill gaps, roadmap, dashboard, filter & sort | 6 |
| **F** | | US-05b + US-08 | [role-f-ai-services.md](role-f-ai-services.md) | Real AI client: extractor, feedback, summaries | 7 |

Write the names in yourself. **A and B are the two critical-path roles** — give them
your strongest members, because four other people are blocked until their contracts land.

---

## The dependency problem, and how we get around it

The build order in `CLAUDE.md` says "work in sequence; do not jump ahead". It says that
because the real dependencies are these:

```
        B (skill resolver)  ─────┬──────────────┐
                                 │              │
        A (extraction) ──────────┼──> D ──> E   │
                                 │     (matching → gaps/dashboard)
        C (profile skills) ──────┘     │
                                       │
        F (real AI) ──> replaces A's stub, and consumes D's output for feedback
```

Four of six members would be blocked on day 1 if we simply started.

**The fix is contract-first.** Before anyone builds a feature, every interface in
[01-CONTRACTS.md](01-CONTRACTS.md) lands on `main` together with a stub implementation
that returns plausible data. From then on, each member codes against an interface that
already compiles, and swaps the stub for the real thing when it arrives.

This is the single thing that decides whether two days works. Do not skip it.

---

## Schedule

### Wave 0 — before anyone else starts (~90 minutes)

**Owners: A and B, pairing. Everyone else reads their handout and sets up.**

Land on `main`, in one PR:

1. Every interface and DTO in [01-CONTRACTS.md](01-CONTRACTS.md), exactly as written.
2. `StubExtractor` returning a fixed, realistic `ExtractedPosting`.
3. `MasterSkillSeeder` with ~60 common skills and their aliases, run at startup.
4. `SkillResolver` doing exact + alias lookup (the simple version — B refines later).
5. All of it registered in `AddJobAlignInfrastructure`.

**Gate: `dotnet build` clean and `dotnet test` green before anyone pulls.**

### Day 1

| When | Everyone |
|---|---|
| AM | Wave 0 lands. Others read handouts, pull, write failing tests against the stubs. |
| PM | Build your slice against the contracts. Commit early, push often. |
| End of day | **Checkpoint 1** — every branch merges to `develop`. Build must be green. |

### Day 2

| When | Everyone |
|---|---|
| AM | Replace stubs with real implementations. B's resolver, F's AI client, D's scorer. |
| Midday | **Checkpoint 2** — full end-to-end run: register → profile → paste → extract → review → score → dashboard. |
| PM | Fix what checkpoint 2 broke. Tests, README, demo rehearsal. |

If checkpoint 2 fails and cannot be fixed in an hour, **fall back to the stub extractor
for the demo**. A working end-to-end flow on canned extraction beats a broken flow on
real AI. That is why the stub exists.

---

## Rules that keep six people out of each other's way

1. **One branch per member**, named `feat/<letter>-<slug>` — e.g. `feat/d-match-scoring`.
2. **Own your files.** Each handout lists files you create and files you must not touch.
   If you need a change in someone else's file, ask them in the group chat; do not edit it.
3. **Never edit another member's migration.** One migration per logical change, named
   descriptively. If two of you generate migrations the same morning, the second one
   rebases and regenerates.
4. **Merge to `develop`, not `main`.** `main` stays demo-able.
5. **Green build is the merge gate.** `dotnet build` 0 warnings, `dotnet test` all pass.

### Shared files — coordinate before touching

| File | Who may edit | Note |
|---|---|---|
| `Infrastructure/DependencyInjection.cs` | anyone, one line each | append only, never reorder |
| `Web/Program.cs` | A and B only | others request changes |
| `Views/Shared/_Layout.cshtml` | E only | others request nav links |
| `Views/Postings/Index.cshtml` | E owns | A adds a status column, coordinate |
| `Core/Enums/*` | nobody | enums are complete; if you need a new one, ask the lead |

---

## Definition of done, per member

- [ ] `dotnet build` clean — 0 errors, **0 warnings**
- [ ] `dotnet test` green, including new tests for your slice
- [ ] Every FR you own is demonstrable in the running app
- [ ] Requirement IDs cited in commit messages and on non-obvious code
- [ ] No business rule in [00-SHARED-BRIEF.md](00-SHARED-BRIEF.md) broken
- [ ] Merged to `develop` with a green build

---

## Honest risk assessment

| Risk | Likelihood | What we do |
|---|---|---|
| Wave 0 slips, four people idle | **High** | A and B start it first thing; nobody else waits on anything else |
| Six AIs invent six different interface shapes | **High** without contracts | 01-CONTRACTS.md is copied verbatim, not paraphrased |
| Migration conflicts | Medium | Only B and C should need migrations; both announce before generating |
| Real AI integration eats day 2 | Medium | Stub fallback is the demo safety net |
| Merge hell at checkpoint 1 | Medium | File ownership table above; merge to `develop` at end of day 1, not day 2 |
| Two days is optimistic for 36 points | **Real** | Stretch items are explicitly deferrable; MVP core is 6 stories |

The plan is achievable but has no slack. If something has to give, cut **Stretch**
sections first, then FR-48 (summaries) and FR-47 (roadmap status), in that order.
Do not cut tests — a broken demo is worse than a thin one.
