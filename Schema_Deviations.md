# Schema deviations from `Database_Design.pdf`

How the implemented database differs from the design document, and why.

**The implemented schema is `InitialSchema` — 30 tables (23 domain + 7 ASP.NET Identity).**
Verify with:

```bash
sqlcmd -S localhost -E -C -I -d JobAlign -Q "SELECT name FROM sys.tables ORDER BY name;"
```

> **Read this before generating any code against the database.** The design document names
> tables that were **not built** — `SkillCategories`, `JobAnalyses`, `Candidates`,
> `JobSkills`, `SavedJobs`, `AnalysisFeedback`, `Organizations`, `Recruiters`. Writing code
> against those names will not compile. The mapping below gives the table that actually
> exists for each one.

---

## 1. Table-by-table mapping

Every table in `Database_Design.pdf` §4, and what it became.

| Design document §4 | Implemented | Note |
|---|---|---|
| `Roles` | `AspNetRoles` | ASP.NET Identity — deviation 2 |
| `Users` | `AspNetUsers` | ASP.NET Identity — deviation 2 |
| `Organizations` | **not built** | deviation 3 — no recruiter role |
| `Recruiters` | **not built** | deviation 3 |
| `Candidates` | `CandidateProfiles` | keyed by `UserId` |
| `Education` | `EducationEntries` | |
| `Experience` | `WorkExperienceEntries` | |
| `Projects` | `ProjectEntries` | |
| `Certifications` | `CertificationEntries` | |
| `Resumes` | `Resumes` | same name |
| `SkillCategories` | **not built** | deviation 4 — folded into a column |
| `Skills` | `MasterSkills` | |
| `SkillAliases` | `SkillAliases` | same name |
| `CandidateSkills` | `ProfileSkills` | |
| `JobPostings` | `JobPostings` | same name, `OwnerUserId` replaces `RecruiterId` |
| `JobExtractions` | `PostingExtractions` | now many-per-posting — deviation 5 |
| `JobSkills` | `PostingSkills` | |
| `SavedJobs` | **not built** | deviation 3 |
| `JobAnalyses` | `MatchResults` | |
| `AnalysisSkills` | `SkillGaps` | |
| `AnalysisFeedback` | **not built** | deviation 6 — folded into a column |
| `JobComparisons` | **not built** | deviation 7 |
| `JobComparisonItems` | **not built** | deviation 7 |
| `AuditLogs` | `AuditEntries` | |

### Tables with no counterpart in the design document

Added to serve requirements the document's schema did not cover:

| Table | Why |
|---|---|
| `Locations`, `LocationAliases` | Location normalization (FR-16). The document stored location as free text. |
| `ExtractionFieldConfidences` | Per-field confidence (FR-20). Rows rather than a column per field, so adding an extracted field later needs no migration. |
| `PostingFieldCorrections` | Candidate corrections (FR-18, BR-03) — see deviation 5. |
| `PostingRelations` | Duplicate detection and same-role links (FR-24 to FR-26). |
| `PostingQualityAssessments` | Completeness assessment (FR-22, FR-23). |
| `ResumeSkillSuggestions` | Unconfirmed resume skills (BR-06) — see deviation 8. |
| `RoadmapItems` | Learning roadmap (FR-46, FR-47). |
| `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims` | Identity framework infrastructure, not domain tables. |

---

## 2. ASP.NET Core Identity supplies users and roles

**Document:** §4.1 defines `Users` with a `PasswordHash nvarchar(255)` column and a `RoleId`
foreign key to `Roles`.

**Implemented:** `ApplicationUser : IdentityUser<int>` and `ApplicationRole : IdentityRole<int>`,
in the default `AspNetUsers` / `AspNetRoles` tables, with the document's extra columns added
(`IsActive`, `CreatedAt`, `DeactivatedAt` on users; `Description` on roles).

**Why:** NFR-05 requires a salted one-way password hash. Identity fills the `PasswordHash`
column the document names, using PBKDF2 with a per-user salt, and adds lockout and
password-reset tokens for free. Hand-writing a `Users` class to match the column list exactly
would mean hand-writing the hashing, which makes NFR-05 weaker, not stronger.

**The real difference:** role assignment goes through the `AspNetUserRoles` junction rather
than a `Users.RoleId` column, because `RoleManager` and `SignInManager` require it. The
document's intent — one role per user from a fixed list — is enforced in application code:
`RoleSeeder` seeds exactly `RoleNames.All`, and registration always assigns `Candidate`.

`UQ_Users_Email` from §6 is implemented on the normalized email column, so two addresses
differing only in case cannot both register.

**Roles seeded:** Candidate and Administrator. Recruiter is not seeded — see deviation 3.

---

## 3. No recruiter role — `Organizations`, `Recruiters` and `SavedJobs` are not built

**Document:** §4.2 defines `Organizations` and `Recruiters`; §4.5 gives `JobPostings` a
`RecruiterId`, and defines `SavedJobs` as the candidate-to-posting junction.

**Implemented:** none of the three. `JobPostings.OwnerUserId` replaces `RecruiterId`.

**Why:** the recruiter role is out of scope. In JA-SRS-001 the **candidate** captures the
posting (FR-06), so there is no second party authoring it. That decision cascades:

- `Organizations` and `Recruiters` have no subjects once recruiters are gone.
- `SavedJobs` existed to link a candidate to a posting somebody *else* authored. When the
  candidate captures it themselves they already own the row, so the junction would hold
  exactly one row per posting pointing back at its own owner.

**Index consequence:** §6 lists `IX_SavedJobs_CandidateId`, justified by NFR-02 (the
saved-postings list must load within two seconds for up to 500 postings). It is replaced by
**`IX_JobPostings_OwnerUserId_CapturedAt`**, which serves the same query against the table the
postings now live in. `UQ_SavedJobs_Candidate_Job` needs no replacement — a candidate cannot
capture the same row twice.

**Cost of reversing:** one additive migration. Add `Organizations` and `Recruiters`, add a
nullable `RecruiterId` to `JobPostings`, reintroduce `SavedJobs`. No existing table changes shape.

---

## 4. `SkillCategories` is a column, not a table

**Document:** §4.4 defines a `SkillCategories` table with `SkillCategoryId` / `CategoryName`,
referenced by `Skills.CategoryId`.

**Implemented:** `MasterSkills.Category`, a nullable `nvarchar` holding the category name
directly. There is no category table and no `CategoryId` column anywhere.

**Why:** a category here carries no data beyond its own name and nothing hangs off it. A
lookup table would add a join to every skill query to retrieve a string the row could hold
itself. `UQ_SkillCategories_CategoryName` from the document is therefore not implemented —
there is no table to constrain.

**Consequence:** categories are not a controlled vocabulary at the database level. If FR-14
later needs an administrator screen for managing categories, promote the column to a table
in one additive migration.

---

## 5. Extraction is many-per-posting, and corrections hang off the posting

**Document:** §4.5 defines `JobExtractions` with `UQ_JobExtractions_JobId` — exactly one
extraction row per posting, edited in place when a reviewer corrects it.

**Implemented:** `PostingExtractions` keeps **every** run as history. A filtered unique index,
`UX_PostingExtractions_CurrentPerPosting` (`WHERE IsCurrent = 1`), permits at most one
*current* run per posting. Corrections live in a separate table, `PostingFieldCorrections`.

**Why:** two requirements the one-row design cannot satisfy together.

- **NFR-08** requires a stored result to remain reproducible against the extraction
  configuration that produced it. Overwriting the row on every re-run destroys that history.
- **BR-03** requires a candidate's correction to survive re-extraction. If corrections were
  columns on the extraction row, regenerating the row would erase them.

`PostingFieldCorrections` therefore has its foreign key to **`JobPostings`, not to
`PostingExtractions`**. Deleting and regenerating extractions structurally cannot touch
corrections. That is the entire reason for the table's shape — do not "tidy" it onto the
extraction row.

Reading a posting means: take the current extraction, then overlay every correction on top.

---

## 6. `AnalysisFeedback` is a column on `MatchResults`

**Document:** §4.6 defines `AnalysisFeedback` as a 1:1 table against `JobAnalyses`, with
`UQ_AnalysisFeedback_AnalysisId`.

**Implemented:** `MatchResults.FeedbackText` and `MatchResults.FeedbackGeneratedAt`.

**Why:** a strictly 1:1 table with two columns is a join for no benefit. The uniqueness
constraint the document wanted is now structural — one row, one feedback.

---

## 7. `JobComparisons` and `JobComparisonItems` are not built

**Document:** §4.6 defines both, for side-by-side comparison.

**Implemented:** neither. FR-49 (compare postings side by side) is priority **S** and is
scheduled as a stretch item.

**Why:** comparison is a read-time operation over postings the candidate already owns. It
needs no stored rows unless comparisons are to be saved and revisited, which no requirement
asks for. If FR-49 later needs persistence, both tables are additive.

---

## 8. Resume skills land in their own table

**Document:** §4.3 stores AI resume output in `Resumes.ExtractedData` as JSON, from which
skills would presumably be promoted into `CandidateSkills`.

**Implemented:** `ResumeSkillSuggestions`, a separate table, alongside `Resumes.ExtractedData`.

**Why:** **BR-06** — match scores are calculated only from skills the candidate has confirmed.
Scoring reads `ProfileSkills` and nothing else, so a suggestion sitting in a different table
is *structurally incapable* of affecting a score. Promoting a suggestion creates a
`ProfileSkill` with `Source = ResumeConfirmed`. Parsing JSON out of a column and hoping the
scoring code remembers to filter would make the same rule a convention instead of a guarantee.

---

## 9. `JobPostings.OwnerUserId` is `NO ACTION`, not `CASCADE`

**Document:** §5 gives `Recruiters → JobPostings` as `NO ACTION`, noting that postings survive
a recruiter's departure.

**Implemented:** `AspNetUsers → JobPostings` is `CASCADE`; the `NO ACTION` edges are
`MatchResults → CandidateProfiles`, `PostingRelations.RelatedJobPostingId`, and
`PostingFieldCorrections.CorrectedByUserId`.

**Why:** SQL Server rejects multiple cascade paths into one table. Each of those three would
have created a second route into a table already cascaded into, and the migration would not
apply. This is a limitation the document acknowledges in §5, not a design preference.

**Consequence:** deleting a posting must first clear `PostingRelations` rows that point *at*
it — `JobPostingService.DeleteAsync` does this. Account deletion (NFR-09) is likewise an
ordered application-layer operation, not a single `DELETE`.

---

## 10. Enums are stored as strings

**Document:** §4 types status columns as `nvarchar(20)` with allowed values listed in prose.

**Implemented:** the same `nvarchar` columns, populated from C# enums via
`HasConversion<string>()`.

**Why:** the data stays legible without a lookup, and the enum is the single definition of
the allowed values. Storing ints would satisfy the column type but make every ad-hoc query
require a decoder ring.

---

## Traceability — indexes named in §6

| §6 index | Status |
|---|---|
| `UQ_Users_Email` | Present as Identity's unique `UserNameIndex` on `NormalizedUserName`, with the unique `UX_AspNetUsers_NormalizedEmail` |
| `UQ_Skills_SkillName` | Present as `UX_MasterSkills_NormalizedName` |
| `UQ_SkillAliases_AliasName` | Present as `UX_SkillAliases_NormalizedAlias` |
| `UQ_CandidateSkills_Candidate_Skill` | Present as `UX_ProfileSkills_Profile_Skill` |
| `UQ_JobSkills_Job_Skill` | Present as `UX_PostingSkills_Posting_Skill` |
| `UQ_SavedJobs_Candidate_Job` | Retired — no `SavedJobs` (deviation 3) |
| `IX_JobAnalyses_Candidate_Job` | Present as `UX_MatchResults_JobPostingId` (one result per posting) plus `IX_MatchResults_Profile_OverallScore` for FR-51 sorting |
| `IX_SavedJobs_CandidateId` | Replaced by `IX_JobPostings_OwnerUserId_CapturedAt` (deviation 3) |
| `IX_JobPostings_Status` | Present as `IX_JobPostings_OwnerUserId_Status` |
| `IX_AnalysisSkills_SkillId` | Present as `IX_SkillGaps_MasterSkillId` |
| `IX_JobSkills_SkillId` | Present as `IX_PostingSkills_MasterSkillId` |
| `UQ_SkillCategories_CategoryName` | Retired — no category table (deviation 4) |
| `UQ_JobExtractions_JobId` | Replaced by the filtered `UX_PostingExtractions_CurrentPerPosting` (deviation 5) |
| `UQ_AnalysisFeedback_AnalysisId` | Structural — feedback is a column (deviation 6) |
| `UQ_Candidates_UserId` | Present as `UX_CandidateProfiles_UserId` |
