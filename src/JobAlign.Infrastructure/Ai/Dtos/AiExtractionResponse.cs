using System.Text.Json.Serialization;

namespace JobAlign.Infrastructure.Ai.Dtos;

/// <summary>
/// The raw shape returned by the model, matching <see cref="ExtractionPrompt.System"/>
/// exactly. Deliberately all strings for enum-shaped fields — the model can and does send
/// garbage, and a raw string is the only thing guaranteed to deserialize (shared brief
/// rule 6: never bind AI output directly to a typed/EF model). <see cref="AiExtractor"/>
/// validates every field here before mapping onto <c>ExtractedPosting</c>.
/// </summary>
public sealed class AiExtractionResponse
{
    [JsonPropertyName("jobTitle")]
    public string? JobTitle { get; set; }

    [JsonPropertyName("companyName")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    /// <summary>Raw string — validated against the allowed enum values by the caller.</summary>
    [JsonPropertyName("remotePolicy")]
    public string? RemotePolicy { get; set; }

    [JsonPropertyName("experienceMinYears")]
    public decimal? ExperienceMinYears { get; set; }

    [JsonPropertyName("experienceMaxYears")]
    public decimal? ExperienceMaxYears { get; set; }

    [JsonPropertyName("salary")]
    public AiSalary? Salary { get; set; }

    [JsonPropertyName("responsibilities")]
    public string? Responsibilities { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("skills")]
    public List<AiSkill> Skills { get; set; } = [];

    [JsonPropertyName("fieldConfidences")]
    public List<AiFieldConfidence> FieldConfidences { get; set; } = [];
}

public sealed class AiSalary
{
    [JsonPropertyName("minRaw")]
    public decimal? MinRaw { get; set; }

    [JsonPropertyName("maxRaw")]
    public decimal? MaxRaw { get; set; }

    [JsonPropertyName("currencyRaw")]
    public string? CurrencyRaw { get; set; }

    /// <summary>Raw string — "year" | "month" | "hour" | null. Validated by the caller.</summary>
    [JsonPropertyName("periodRaw")]
    public string? PeriodRaw { get; set; }
}

public sealed class AiSkill
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Raw string — "required" | "preferred". Validated by the caller.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Raw string — "high" | "medium" | "low" | null. Validated by the caller.</summary>
    [JsonPropertyName("confidence")]
    public string? Confidence { get; set; }
}

public sealed class AiFieldConfidence
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    /// <summary>Raw string — "high" | "medium" | "low". Validated by the caller.</summary>
    [JsonPropertyName("confidence")]
    public string? Confidence { get; set; }
}
