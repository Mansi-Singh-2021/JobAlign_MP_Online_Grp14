using System.Text.Json;
using System.Text.RegularExpressions;
using JobAlign.Core.Abstractions;
using JobAlign.Core.Enums;
using JobAlign.Core.Extraction;
using JobAlign.Infrastructure.Ai.Dtos;
using Microsoft.Extensions.Logging;

namespace JobAlign.Infrastructure.Ai;

/// <summary>
/// Real extraction behind <see cref="IJobExtractor"/> (Member F, build order step 4, NFR-11).
/// Drops in behind <see cref="JobAlign.Infrastructure.Extraction.StubExtractor"/> — same
/// interface, same contract, nothing else in the app changes (shared brief rule 6).
///
/// Never throws for a provider problem (NFR-06): every failure path returns
/// <see cref="ExtractionOutcome.Failure"/> with a reason, and <c>ExtractionService</c> turns
/// that into a Pending posting. AI output is treated as untrusted input throughout — it is
/// deserialized onto <see cref="AiExtractionResponse"/>, never onto <see cref="ExtractedPosting"/>
/// or an EF entity directly, and every field is validated before it is mapped.
/// </summary>
public sealed class AiExtractor : IJobExtractor
{
    private static readonly Regex FencedJson = new(
        @"^\s*```(?:json)?\s*(?<body>.*?)\s*```\s*$",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly HashSet<string> NotSpecifiedLiterals = new(StringComparer.OrdinalIgnoreCase)
    {
        "not specified", "n/a", "na", "unknown", "tbd", "none", "null", ""
    };

    private readonly AnthropicClient _client;
    private readonly ILogger<AiExtractor> _logger;

    public AiExtractor(AnthropicClient client, ILogger<AiExtractor> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>Bumped whenever the extraction prompt changes (NFR-08).</summary>
    public string ConfigVersion => ExtractionPrompt.ConfigVersion;

    public async Task<ExtractionOutcome> ExtractAsync(string rawText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return ExtractionOutcome.Failure("The posting has no text to extract from.");

        var result = await _client.SendAsync(
            ExtractionPrompt.System,
            ExtractionPrompt.BuildUserMessage(rawText),
            maxTokens: 2048,
            cancellationToken);

        if (!result.Succeeded)
            return ExtractionOutcome.Failure(result.FailureReason ?? "The AI service could not be reached.");

        AiExtractionResponse? parsed;
        try
        {
            parsed = ParseJson(result.Text!);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "AI extraction response was not valid JSON.");
            return ExtractionOutcome.Failure("The AI service returned a response that could not be parsed.");
        }

        if (parsed is null)
            return ExtractionOutcome.Failure("The AI service returned an empty response.");

        var posting = Map(parsed);
        return ExtractionOutcome.Success(posting);
    }

    /// <summary>
    /// Parses the model's reply as JSON. Models sometimes wrap valid JSON in ```json fences
    /// despite the prompt saying not to — strip those and retry once before giving up.
    /// </summary>
    private static AiExtractionResponse? ParseJson(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<AiExtractionResponse>(text, JsonOptions);
        }
        catch (JsonException)
        {
            var fenced = FencedJson.Match(text.Trim());
            if (!fenced.Success)
                throw;

            return JsonSerializer.Deserialize<AiExtractionResponse>(fenced.Groups["body"].Value, JsonOptions);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Validates and maps the untrusted response onto the DTO the rest of the app expects.
    /// Every field degrades to null on anything unexpected rather than throwing (BR-02) —
    /// an extraction that gets nine fields right and drops one bad one is more useful to a
    /// candidate than an extraction that fails outright.
    /// </summary>
    private ExtractedPosting Map(AiExtractionResponse response)
    {
        var skills = new List<ExtractedSkill>();
        foreach (var s in response.Skills)
        {
            var name = CleanText(s.Name);
            if (string.IsNullOrWhiteSpace(name))
                continue; // FR-13/task-list: drop a skill with an empty name

            var type = ParseSkillType(s.Type);
            if (type is null)
            {
                _logger.LogInformation("Dropping skill '{Skill}' with unrecognised type '{Type}'.", name, s.Type);
                continue; // unknown enum value -> do not throw, do not guess a type
            }

            skills.Add(new ExtractedSkill(name, type.Value, ParseConfidence(s.Confidence)));
        }

        var confidences = new List<ExtractedFieldConfidence>();
        foreach (var c in response.FieldConfidences)
        {
            var field = CleanText(c.Field);
            var level = ParseConfidence(c.Confidence);
            if (field is null || level is null)
                continue;

            confidences.Add(new ExtractedFieldConfidence(field, level.Value, null));
        }

        return new ExtractedPosting
        {
            JobTitle = CleanText(response.JobTitle),
            CompanyName = CleanText(response.CompanyName),
            RawLocationText = CleanText(response.Location),
            RemotePolicy = ParseRemotePolicy(response.RemotePolicy),

            ExperienceMinYears = NonNegative(response.ExperienceMinYears),
            ExperienceMaxYears = NonNegative(response.ExperienceMaxYears),

            // Only the raw, as-stated figures map onto ExtractedPosition (contracts, section A) —
            // yearly normalization is a separate build step (FR-15/FR-16) owned elsewhere.
            SalaryMinRaw = NonNegative(response.Salary?.MinRaw),
            SalaryMaxRaw = NonNegative(response.Salary?.MaxRaw),
            SalaryCurrencyRaw = CleanText(response.Salary?.CurrencyRaw),
            SalaryPeriodRaw = ParseSalaryPeriod(response.Salary?.PeriodRaw),

            Responsibilities = CleanText(response.Responsibilities),
            Summary = CleanText(response.Summary),

            Skills = skills,
            Confidences = confidences
        };
    }

    /// <summary>
    /// Treats the literal strings models use in place of null — "Not specified", "N/A",
    /// "unknown", etc. — as null (BR-02, task list item 2). Also trims and blanks-to-null.
    /// </summary>
    private static string? CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return NotSpecifiedLiterals.Contains(trimmed) ? null : trimmed;
    }

    /// <summary>Negative or absurd values become null rather than being stored (task list item 2).</summary>
    private static decimal? NonNegative(decimal? value) =>
        value is >= 0 and <= 1_000_000_000 ? value : null;

    private static RemotePolicy? ParseRemotePolicy(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "remote" => RemotePolicy.Remote,
        "hybrid" => RemotePolicy.Hybrid,
        "onsite" => RemotePolicy.Onsite,
        "unclear" => RemotePolicy.Unclear,
        _ => null
    };

    private static SalaryPeriod? ParseSalaryPeriod(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "year" => SalaryPeriod.Year,
        "month" => SalaryPeriod.Month,
        "hour" => SalaryPeriod.Hour,
        _ => null
    };

    private static SkillType? ParseSkillType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "required" => SkillType.Required,
        "preferred" => SkillType.Preferred,
        _ => null
    };

    private static ConfidenceLevel? ParseConfidence(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "high" => ConfidenceLevel.High,
        "medium" => ConfidenceLevel.Medium,
        "low" => ConfidenceLevel.Low,
        _ => null
    };
}
