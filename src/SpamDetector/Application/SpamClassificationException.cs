namespace SpamDetector.Application;

/// <summary>
/// Categories of classification failure that map to a controlled upstream error (HTTP 502).
/// </summary>
public enum SpamClassificationFailure
{
    Authentication,
    RateLimit,
    Timeout,
    ProviderError,
    ProviderRejected,
    InvalidResponse
}

/// <summary>
/// Raised by the classifier when the upstream provider call fails or returns unusable output.
/// Carries only a safe, non-sensitive category message — never a key, header, raw payload, or
/// raw provider exception.
/// </summary>
public sealed class SpamClassificationException : Exception
{
    public SpamClassificationFailure Failure { get; }

    /// <summary>All classification failures surface to the caller as HTTP 502.</summary>
    public int HttpCode => 502;

    /// <summary>Non-sensitive error category string safe to return to the caller.</summary>
    public string SafeMessage { get; }

    public SpamClassificationException(SpamClassificationFailure failure)
        : base(ToSafeMessage(failure))
    {
        Failure = failure;
        SafeMessage = ToSafeMessage(failure);
    }

    private static string ToSafeMessage(SpamClassificationFailure failure) => failure switch
    {
        SpamClassificationFailure.Authentication =>
            "Upstream classification provider authentication failed.",
        SpamClassificationFailure.RateLimit =>
            "Upstream classification provider rate limit exceeded.",
        SpamClassificationFailure.Timeout =>
            "Upstream classification request timed out.",
        SpamClassificationFailure.ProviderRejected =>
            "Upstream classification provider rejected the request.",
        SpamClassificationFailure.InvalidResponse =>
            "Upstream classification returned an invalid response.",
        _ => "Upstream classification provider error."
    };
}
