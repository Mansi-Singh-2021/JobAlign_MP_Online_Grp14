# Schema deviations from `Database_Design.pdf`

The implemented schema follows the Database Design document table for table, column for
column, index for index, with four exceptions. Each is recorded here with the reason, so none
of them is mistaken for an oversight.

Implemented: migration `InitialSchema`, 26 tables, SQL Server, EF Core 10.

---

## 1. No recruiter role — `Organizations`, `Recruiters` and `SavedJobs` are not built

**Document:** §4.2 defines `Organizations` and `Recruiters`; §4.5 gives `JobPostings` a
`RecruiterId` and defines `SavedJobs` as the candidate-to-posting junction.

**Implemented:** none of the three tables exist. `JobPostings.CandidateId` replaces
`RecruiterId`.

**Why:** the recruiter role is out of scope for this build. That decision cascades:

- `Organizations` and `Recruiters` have no subjects once recruiters are gone.
- `SavedJobs` existed to link a candidate to a posting somebody *else* authored. When the
  candidate captures the posting themselves, they already own the row, and a
  candidate-to-posting junction records nothing that `JobPostings.CandidateId` does not.
  Keeping it would mean every posting had exactly one `SavedJobs` row pointing back at its
  own owner.

**Requirements still met:** FR-04 ("save and analyze job postings") is served by
`JobPostings.CandidateId` plus `JobAnalyses`. FR-09 to FR-12 are recruiter requirements;
FR-12 (manage required and preferred skills) survives as `JobSkills.RequirementType`, and
FR-11 (review completeness and edit extracted detail) survives with the candidate as the
reviewer — `JobExtractions.ReviewedByUserId` and `JobPostings.CompletenessScore` are both
built.

**Cost of reversing this:** one additive migration. Add `Organizations` and `Recruiters`, add
a nullable `RecruiterId` to `JobPostings`, and reintroduce `SavedJobs`. No table below would
need to change shape.

### Index consequence

§6 lists `IX_SavedJobs_CandidateId`, justified by NFR-02 (the saved-jobs list must load within
two seconds for up to 500 postings). That index is replaced by **`IX_JobPostings_CandidateId`**,
which serves the identical query against the table the postings now live in. The NFR-02
requirement is unaffected. `UQ_SavedJobs_Candidate_Job` has no replacement and needs none —
a candidate cannot capture the same row twice.

---

## 2. ASP.NET Core Identity supplies `Users` and `Roles`

**Document:** §4.1 defines `Users` with a `PasswordHash nvarchar(255)` column and a
`RoleId` foreign key to `Roles`.

**Implemented:** `ApplicationUser : IdentityUser<int>` and `ApplicationRole : IdentityRole<int>`,
mapped to tables named `Users` and `Roles`, with the document's extra columns added
(`FullName`, `IsActive`, `CreatedAt`, `UpdatedAt` on `Users`; `Description` on `Roles`).

**Why:** NFR-04 requires passwords to be securely hashed. Identity supplies the
`PasswordHash` column the document names, filled by PBKDF2 with a per-user salt, plus lockout
and password-reset tokens. Hand-writing a `Users` class with a `PasswordHash` string to match
the column list would mean hand-writing the hashing, which makes NFR-04 weaker rather than
stronger. The table names, keys and columns are the document's; only the mechanism filling
`PasswordHash` differs.

**The one real difference:** role assignment goes through a `UserRoles` junction table rather
than a `Users.RoleId` column, because `RoleManager` and `SignInManager` require it. The
document's intent — one role per user, drawn from a fixed list — is enforced in application
code instead of by the schema. The five extra tables this brings (`UserRoles`, `UserClaims`,
`UserLogins`, `UserTokens`, `RoleClaims`) are framework infrastructure, not domain tables.

`UQ_Users_Email` from §6 is implemented on the normalized email column, so two addresses
differing only in case cannot both register.

**Roles seeded:** Candidate and Administrator. Recruiter is not seeded (see deviation 1).

---

## 3. `JobPostings.CandidateId` is `NO ACTION`, not `CASCADE`

**Document:** §5 gives `Recruiters → JobPostings` as `NO ACTION`, with the note that postings
survive a recruiter's departure.

**Implemented:** `Candidates → JobPostings` is also `NO ACTION`.

**Why:** two reasons, and the second is binding.

1. It mirrors the document's own rule for the same relationship.
2. §5 already puts the cascade into `JobAnalyses` on the `Candidates` side, with
   `JobPostings → JobAnalyses` as `NO ACTION`, precisely because SQL Server rejects multiple
   cascade paths to one table. Making `Candidates → JobPostings` cascade would create a second
   route from `Candidates` into `JobAnalyses` and the migration would not apply.

**Consequence for NFR-08** (users shall be able to delete their data): account deletion is an
**ordered application-layer operation**, not a single `DELETE`. Comparisons, then analyses,
then postings, then the candidate row. The direct child tables of `Candidates` — `Education`,
`Experience`, `Projects`, `Certifications`, `Resumes`, `CandidateSkills` — all cascade as the
document specifies, so those need no explicit handling. This is a limitation of SQL Server's
cascade rules, acknowledged in §5 of the document itself, not a gap in the design.

---

## 4. Added: `UQ_AnalysisFeedback_AnalysisId`, `UQ_JobComparisonItems_Comparison_Analysis`,
`UQ_Candidates_UserId`, `UQ_JobExtractions_JobId`, `UQ_SkillCategories_CategoryName`,
`IX_AuditLogs_Timestamp`

**Document:** §4 states these uniqueness rules in prose — "UQ" against `Candidates.UserId`,
`JobExtractions.JobId` and `AnalysisFeedback.AnalysisId`, and 1:1 cardinality in §5 — but §6
does not name them in the indexing table.

**Implemented:** each is a named constraint, so the rule is enforced by the database rather
than by application code remembering it. `IX_AuditLogs_Timestamp` is additional, supporting
the date-ranged queries FR-16 reporting will need.

**Why:** these are the document's own stated constraints, given names. Nothing here changes
the design; it makes constraints the document asserts actually hold.

---

## Traceability check

Every index named in Database Design §6 is present, replaced, or explicitly retired:

| §6 index | Status |
|---|---|
| `UQ_Users_Email` | Present (on normalized email) |
| `UQ_Skills_SkillName` | Present |
| `UQ_SkillAliases_AliasName` | Present |
| `UQ_CandidateSkills_Candidate_Skill` | Present |
| `UQ_JobSkills_Job_Skill` | Present |
| `UQ_SavedJobs_Candidate_Job` | Retired — no `SavedJobs` (deviation 1) |
| `IX_JobAnalyses_Candidate_Job` | Present |
| `IX_SavedJobs_CandidateId` | Replaced by `IX_JobPostings_CandidateId` (deviation 1) |
| `IX_JobPostings_Status` | Present |
| `IX_AnalysisSkills_SkillId` | Present |
| `IX_JobSkills_SkillId` | Present |

Every delete rule in §5 that survives the recruiter drop is implemented as specified, verified
against `sys.foreign_keys`.
