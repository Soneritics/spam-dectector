using OpenAI.Responses;

namespace SpamDetector.Infrastructure.OpenAI;

/// <summary>
/// Creates a per-request <see cref="ResponsesClient"/> from a caller-supplied API key. The factory
/// stores no credential and holds no static/singleton client bound to a fixed key (BYOK isolation).
/// </summary>
public interface IResponsesClientFactory
{
    ResponsesClient Create(string apiKey);
}

/// <inheritdoc />
public sealed class ResponsesClientFactory : IResponsesClientFactory
{
    public ResponsesClient Create(string apiKey) => new(apiKey);
}
