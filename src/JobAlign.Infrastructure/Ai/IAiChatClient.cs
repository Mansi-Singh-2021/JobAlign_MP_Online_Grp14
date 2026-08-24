namespace JobAlign.Infrastructure.Ai;

/// <summary>
/// One call to a chat-completion provider, reduced to what this project actually needs:
/// a system prompt, a user message, a token ceiling, and text back.
///
/// The seam exists so the provider is a configuration choice rather than a code change
/// (NFR-11). <see cref="AiExtractor"/> and <see cref="AiFeedbackGenerator"/> depend on
/// this interface, never on a wire format — swapping Gemini for Anthropic changes which
/// implementation is registered and nothing else.
///
/// Implementations must never throw for a provider problem (NFR-06). A timeout, a non-2xx,
/// a missing key or an unparseable body all come back as <see cref="AiResult.Failure"/>
/// with a reason the caller can store and show. Caller-requested cancellation is the one
/// exception: that propagates, because it is intent rather than failure.
/// </summary>
public interface IAiChatClient
{
    /// <summary>Name of the provider behind this client, for logging and diagnostics.</summary>
    string ProviderName { get; }

    Task<AiResult> SendAsync(
        string systemPrompt,
        string userMessage,
        int maxTokens,
        AiResponseFormat responseFormat,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What the caller expects back. <see cref="Json"/> lets a provider that supports
/// constrained decoding enforce the JSON contract at the API rather than hoping the
/// prompt held — the extraction contract requires strict JSON with no prose and no
/// markdown fences. Providers without the feature ignore it and rely on the prompt.
/// </summary>
public enum AiResponseFormat
{
    Text = 0,
    Json = 1
}

/// <summary>Outcome of one provider call. Failure is a normal result, not an exception.</summary>
public sealed class AiResult
{
    public bool Succeeded { get; private init; }
    public string? Text { get; private init; }
    public string? FailureReason { get; private init; }

    public static AiResult Success(string text) => new() { Succeeded = true, Text = text };
    public static AiResult Failure(string reason) => new() { Succeeded = false, FailureReason = reason };
}
