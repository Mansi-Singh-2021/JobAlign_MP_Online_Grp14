# JobAlign — Role C implementation

Story: US-02 — Candidate profile and skills.

## Files

- `src/JobAlign.Core/Abstractions/ICandidateProfileService.cs`
- `src/JobAlign.Core/Abstractions/IProfileEntryService.cs`
- `src/JobAlign.Infrastructure/Services/CandidateProfileService.cs`
- `src/JobAlign.Web/Controllers/ProfileController.cs`
- `src/JobAlign.Web/Models/Profile/ProfileViewModels.cs`
- `src/JobAlign.Web/Views/Profile/Index.cshtml`
- `src/JobAlign.Web/Views/Profile/Skills.cshtml`
- `src/JobAlign.Web/Views/Profile/Experience.cshtml`
- `tests/JobAlign.Tests/CandidateProfileServiceTests.cs`

## DI

See `DI-CHANGE.txt`. The shared `ICandidateProfileService` contract is unchanged. `IProfileEntryService` is an additional Role C seam for the FR-27 collection CRUD that is not exposed by the fixed shared contract.

## Run

From the repository root:

```bash
dotnet build
dotnet test
```

No migration is required: Role C uses the existing profile schema.
