using System.Text.Json.Serialization;

namespace SpamDetector.Models;

/// <summary>
/// Classification result returned by the classifier and deserialized directly from OpenAI's
/// strict Structured Output.
/// </summary>
public sealed class SpamResult
{
    [JsonPropertyName("spam")]
    public bool Spam { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("promptInjectionDetected")]
    public bool PromptInjectionDetected { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}
