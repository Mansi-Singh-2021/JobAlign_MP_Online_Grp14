using JobAlign.Core.Extraction;

namespace JobAlign.Core.Abstractions;

/// <summary>
/// Reads structured detail out of a posting's raw text (FR-12, FR-13).
/// Behind an interface so the AI provider can be replaced without touching the
/// application (NFR-11), and so the whole review flow can be built and tested
/// against a stub.
/// </summary>
public interface IJobExtractor
{
    /// <summary>
    /// Identifies the prompt/model configuration, stored on every run so a result
    /// can be reproduced and explained later (NFR-08). For example "stub-v1".
    /// </summary>
    string ConfigVersion { get; }

    /// <summary>Never throws for a provider failure — returns ExtractionOutcome.Failure instead.</summary>
    Task<ExtractionOutcome> ExtractAsync(string rawText, CancellationToken cancellationToken = default);
}
