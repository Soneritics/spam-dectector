namespace SpamDetector.Application;

/// <summary>
/// Strongly-typed configuration bound from the <c>SpamDetector</c> configuration section.
/// </summary>
public sealed class SpamDetectorOptions
{
    public const string SectionName = "SpamDetector";

    /// <summary>Maximum accepted request body size in bytes (default 1 MB).</summary>
    public long MaxRequestBodyBytes { get; set; } = 1_048_576;

    /// <summary>Finite timeout applied to the OpenAI classification call, in seconds.</summary>
    public int OpenAiTimeoutSeconds { get; set; } = 30;

    /// <summary>Model used when the caller does not supply <c>x-openai-model</c>.</summary>
    public string DefaultModel { get; set; } = "gpt-5.6-luna";
}
