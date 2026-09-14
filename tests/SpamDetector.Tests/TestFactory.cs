using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpamDetector.Application;
using SpamDetector.Functions;
using SpamDetector.Models;

namespace SpamDetector.Tests;

/// <summary>
/// Test doubles and factory helpers shared across the unit and integration suites. No test contacts
/// live OpenAI; the <see cref="FakeSpamClassifier"/> replaces the classifier boundary.
/// </summary>
public sealed class FakeSpamClassifier : ISpamClassifier
{
    private readonly Func<SpamCheckRequest, CancellationToken, Task<SpamResult>> _behavior;

    public int CallCount { get; private set; }
    public SpamCheckRequest? LastRequest { get; private set; }
    public CancellationToken LastCancellationToken { get; private set; }

    public FakeSpamClassifier(SpamResult result)
        : this((_, _) => Task.FromResult(result))
    {
    }

    public FakeSpamClassifier(Func<SpamCheckRequest, CancellationToken, Task<SpamResult>> behavior)
    {
        _behavior = behavior;
    }

    public static FakeSpamClassifier Throwing(Exception exception) =>
        new((_, _) => throw exception);

    public Task<SpamResult> ClassifyAsync(SpamCheckRequest request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        LastCancellationToken = cancellationToken;
        return _behavior(request, cancellationToken);
    }
}

public static class TestFactory
{
    public static SpamDetectorOptions DefaultOptions() => new()
    {
        MaxRequestBodyBytes = 1_048_576,
        OpenAiTimeoutSeconds = 30,
        DefaultModel = "gpt-5.6-luna"
    };

    public static IOptions<SpamDetectorOptions> Options(SpamDetectorOptions? options = null) =>
        Microsoft.Extensions.Options.Options.Create(options ?? DefaultOptions());

    public static SpamCheckService CreateService(ISpamClassifier classifier) =>
        new(classifier, NullLogger<SpamCheckService>.Instance);

    public static SpamCheckFunction CreateFunction(ISpamClassifier classifier, SpamDetectorOptions? options = null) =>
        new(CreateService(classifier), Options(options), NullLogger<SpamCheckFunction>.Instance);

    public static HttpRequest CreateHttpRequest(
        string? body,
        string? apiKey = "sk-test-key",
        string? model = null,
        bool setContentLength = true)
    {
        var context = new DefaultHttpContext();
        HttpRequest request = context.Request;
        request.Method = "POST";
        request.ContentType = "text/plain";

        if (apiKey is not null)
        {
            request.Headers["x-openai-api-key"] = apiKey;
        }

        if (model is not null)
        {
            request.Headers["x-openai-model"] = model;
        }

        byte[] bytes = body is null
            ? Array.Empty<byte>()
            : System.Text.Encoding.UTF8.GetBytes(body);
        request.Body = new MemoryStream(bytes);
        if (setContentLength)
        {
            request.ContentLength = bytes.Length;
        }

        return request;
    }

    public static SpamResult NonSpam() => new()
    {
        Spam = false,
        Confidence = 0.02,
        PromptInjectionDetected = false,
        Reason = "Ordinary personal correspondence."
    };

    public static SpamResult Spam() => new()
    {
        Spam = true,
        Confidence = 0.97,
        PromptInjectionDetected = false,
        Reason = "Phishing indicators present."
    };
}
