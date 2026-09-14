namespace SpamDetector.Application;

/// <summary>
/// Resolves the OpenAI model to use for a request. A missing, empty, or whitespace caller value
/// falls back to the configured default; any other value is passed through unchanged (no allow-list).
/// </summary>
public static class ModelResolver
{
    public static string Resolve(string? requestedModel, string defaultModel) =>
        string.IsNullOrWhiteSpace(requestedModel) ? defaultModel : requestedModel;
}
