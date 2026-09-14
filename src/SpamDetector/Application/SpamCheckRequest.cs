namespace SpamDetector.Application;

/// <summary>
/// Application-layer input to the classification use case. Request-scoped only.
/// <paramref name="ApiKey"/> is a secret that must never be logged, persisted, serialized,
/// or copied into an <c>ApiResult</c> or exception.
/// </summary>
public sealed record SpamCheckRequest(string EmailContent, string ApiKey, string Model);
