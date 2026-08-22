using JobAlign.Core.Abstractions;
using JobAlign.Core.Enums;
using JobAlign.Core.Extraction;

namespace JobAlign.Infrastructure.Extraction;

/// <summary>
/// Extraction without an AI service (build order step 3).
/// </summary>
/// <remarks>
/// Exists so the whole capture → extract → review → confirm → score flow can be built,
/// tested and demonstrated before any provider is wired up, and so it keeps working when
/// the provider is unavailable or nobody has an API key. The real implementation
/// (<c>AiExtractor</c>, Member F) sits behind the same <see cref="IJobExtractor"/>
/// interface, which is what NFR-11 asks for.
///
/// Two fields are derived from the text — the title from its first line, and the skills by
/// looking for known names that genuinely appear. Everything else is a fixed, plausible
/// value. That keeps results deterministic for tests while making a demo look like
/// something actually read the posting.
///
/// <see cref="ExtractedPosting.CompanyName"/> is deliberately always null: it exercises the
/// "Not specified" path on the review screen from day one, so nobody ships a UI that
/// renders an unstated field as an empty box (BR-02, FR-17).
/// </remarks>
public class StubExtractor : IJobExtractor
{
    public string ConfigVersion => "stub-v1";

    /// <summary>
    /// Skill names this stub can recognise, paired with how a posting usually frames them.
    /// Every name here must exist in the seeded master skill list, or resolution drops it
    /// and the downstream scoring has nothing to compare — agreed with Member B.
    /// </summary>
    private static readonly (string Name, SkillType Type)[] KnownSkills =
    [
        ("C#", SkillType.Required),
        ("ASP.NET Core", SkillType.Required),
        ("SQL Server", SkillType.Required),
        ("REST API", SkillType.Required),
        ("Entity Framework Core", SkillType.Required),
        ("JavaScript", SkillType.Required),
        ("TypeScript", SkillType.Required),
        ("React", SkillType.Required),
        ("Python", SkillType.Required),
        ("Java", SkillType.Required),
        ("Docker", SkillType.Preferred),
        ("Azure", SkillType.Preferred),
        ("AWS", SkillType.Preferred),
        ("Kubernetes", SkillType.Preferred),
        ("Terraform", SkillType.Preferred),
        ("CI/CD", SkillType.Preferred),
        ("Git", SkillType.Preferred),
        ("Agile", SkillType.Preferred)
    ];

    /// <summary>
    /// Used when the pasted text mentions nothing recognisable. Matches the worked example
    /// in section 12 of the SRS, so the demo reproduces the document.
    /// </summary>
    private static readonly (string Name, SkillType Type)[] FallbackSkills =
    [
        ("C#", SkillType.Required),
        ("ASP.NET Core", SkillType.Required),
        ("SQL Server", SkillType.Required),
        ("REST API", SkillType.Required),
        ("Docker", SkillType.Required),
        ("Azure", SkillType.Required),
        ("Kubernetes", SkillType.Preferred),
        ("Terraform", SkillType.Preferred)
    ];

    public Task<ExtractionOutcome> ExtractAsync(string rawText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return Task.FromResult(ExtractionOutcome.Failure("The posting has no text to extract from."));

        var skills = FindSkills(rawText);

        var posting = new ExtractedPosting
        {
            JobTitle = FirstLineAsTitle(rawText),

            // Always null. See the class remarks — this is the "Not specified" test case.
            CompanyName = null,

            RawLocationText = "Pune, India",
            RemotePolicy = RemotePolicy.Hybrid,

            ExperienceMinYears = 3m,
            ExperienceMaxYears = 6m,

            SalaryMinRaw = 1_200_000m,
            SalaryMaxRaw = 1_800_000m,
            SalaryCurrencyRaw = "INR",
            SalaryPeriodRaw = SalaryPeriod.Year,

            Responsibilities =
                "Design, build and maintain backend services. Collaborate with product and QA. "
                + "Participate in code review and technical design.",

            Summary = "Backend-focused .NET role with cloud exposure, hybrid in Pune, 3–6 years' experience.",

            Skills = skills
                .Select(s => new ExtractedSkill(s.Name, s.Type, ConfidenceLevel.High))
                .ToList(),

            Confidences =
            [
                new ExtractedFieldConfidence(nameof(ExtractedPosting.JobTitle), ConfidenceLevel.High, 0.95m),
                new ExtractedFieldConfidence(nameof(ExtractedPosting.RawLocationText), ConfidenceLevel.Medium, 0.70m),
                new ExtractedFieldConfidence(nameof(ExtractedPosting.RemotePolicy), ConfidenceLevel.Medium, 0.65m),
                new ExtractedFieldConfidence(nameof(ExtractedPosting.ExperienceMinYears), ConfidenceLevel.High, 0.90m),

                // Deliberately low, so the review screen's low-confidence flag (FR-20, NFR-06)
                // has something to show without anyone having to fake it.
                new ExtractedFieldConfidence(nameof(ExtractedPosting.SalaryMinRaw), ConfidenceLevel.Low, 0.35m)
            ]
        };

        return Task.FromResult(ExtractionOutcome.Success(posting));
    }

    /// <summary>
    /// Known skill names that actually appear in the text. Falls back to the SRS worked
    /// example when the text mentions none, so a demo never shows an empty skill list.
    /// </summary>
    private static (string Name, SkillType Type)[] FindSkills(string rawText)
    {
        var found = KnownSkills
            .Where(s => rawText.Contains(s.Name, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return found.Length > 0 ? found : FallbackSkills;
    }

    /// <summary>
    /// First non-blank line, where it is short enough to plausibly be a title. A long first
    /// line is prose, not a heading, and guessing one would be inventing a detail (BR-02).
    /// </summary>
    private static string? FirstLineAsTitle(string rawText)
    {
        var firstLine = rawText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return firstLine is { Length: > 0 and <= 80 } ? firstLine : null;
    }
}
