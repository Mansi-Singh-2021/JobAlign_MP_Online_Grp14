namespace JobAlign.Infrastructure.Ai;

/// <summary>Chat-completion provider this deployment talks to.</summary>
public enum AiProvider
{
    Gemini = 0,
    Anthropic = 1
}

/// <summary>
/// Configuration for the AI client (Member F, NFR-11).
/// Bound from configuration under the "Ai" section. <see cref="ApiKey"/> comes from
/// user-secrets locally and an environment variable in deployment — never from
/// appsettings.json (shared brief §5). <see cref="Model"/> and <see cref="BaseUrl"/>
/// may live in appsettings.json; they are not secrets.
/// </summary>
public sealed class AiClientOptions
{
    public const string SectionName = "Ai";

    /// <summary>
    /// Which provider to use. Selecting one is all it takes — <see cref="Model"/> and
    /// <see cref="BaseUrl"/> both fall back to that provider's defaults, so a working
    /// configuration is a provider and a key.
    /// </summary>
    public AiProvider Provider { get; set; } = AiProvider.Gemini;

    /// <summary>
    /// Null or blank means "no key configured". DependencyInjection uses this to fall
    /// back to the stub implementations rather than crash — five teammates without a
    /// key still need the app to run.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Null or blank means "whatever <see cref="ResolveModel"/> picks for the provider".</summary>
    public string? Model { get; set; }

    /// <summary>
    /// Null or blank means "whatever <see cref="ResolveBaseUrl"/> picks for the provider".
    /// Note the two providers mean different things by it: for Anthropic this is the whole
    /// endpoint, for Gemini it is the API root and the client appends
    /// <c>/models/{model}:generateContent</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Ceiling on the reply. Extraction has to fit a whole posting — title, responsibilities,
    /// summary, every skill and every confidence — so this is generous on purpose. Too low
    /// and the JSON is cut mid-object and fails to parse.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 8192;

    /// <summary>
    /// Gemini only. Null leaves the field off the request entirely; 0 turns thinking off so
    /// the whole <see cref="MaxOutputTokens"/> ceiling is spent on the answer. Thinking
    /// models bill reasoning against the same ceiling, which is a common cause of a reply
    /// that stops mid-JSON.
    /// </summary>
    public int? ThinkingBudget { get; set; }

    /// <summary>Anthropic API version header, versioned independently of the SDK/model.</summary>
    public string AnthropicVersion { get; set; } = "2023-06-01";

    public string ResolveModel() =>
        string.IsNullOrWhiteSpace(Model)
            ? Provider switch
            {
                AiProvider.Anthropic => "claude-sonnet-5",
                _ => "gemini-3.6-flash"
            }
            : Model;

    public string ResolveBaseUrl() =>
        string.IsNullOrWhiteSpace(BaseUrl)
            ? Provider switch
            {
                AiProvider.Anthropic => "https://api.anthropic.com/v1/messages",
                _ => "https://generativelanguage.googleapis.com/v1beta"
            }
            : BaseUrl;
}
