namespace JobAlign.Infrastructure.Ai;

/// <summary>
/// Configuration for the Anthropic client (Member F, NFR-11).
/// Bound from configuration under the "Ai" section. <see cref="ApiKey"/> comes from
/// user-secrets locally and an environment variable in deployment — never from
/// appsettings.json (shared brief §5). <see cref="Model"/> and <see cref="BaseUrl"/>
/// may live in appsettings.json; they are not secrets.
/// </summary>
public sealed class AiClientOptions
{
    public const string SectionName = "Ai";

    /// <summary>
    /// Null or blank means "no key configured". DependencyInjection uses this to fall
    /// back to the stub implementations rather than crash — five teammates without a
    /// key still need the app to run.
    /// </summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-sonnet-5";

    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Anthropic API version header, versioned independently of the SDK/model.</summary>
    public string AnthropicVersion { get; set; } = "2023-06-01";
}
