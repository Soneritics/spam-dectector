using Microsoft.Extensions.Logging.Abstractions;
using SpamDetector.Application;
using SpamDetector.Infrastructure.OpenAI;
using SpamDetector.Models;
using Xunit;

namespace SpamDetector.Tests.Integration;

/// <summary>
/// Live OpenAI classification test. Trait-gated and skipped unless <c>OPENAI_API_KEY</c> is set, so
/// it is excluded from the default run via <c>dotnet test --filter "Category!=LiveOpenAI"</c>.
/// </summary>
[Trait("Category", "LiveOpenAI")]
public sealed class LiveOpenAITests
{
    private static string? ApiKey => Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    private static string Model =>
        Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5.6-luna";

    [SkippableFact]
    public async Task Classifies_a_legitimate_email_as_not_spam()
    {
        Skip.If(string.IsNullOrWhiteSpace(ApiKey), "OPENAI_API_KEY not set; skipping live OpenAI test.");

        var classifier = new OpenAISpamClassifier(
            new ResponsesClientFactory(),
            TestFactory.Options(),
            NullLogger<OpenAISpamClassifier>.Instance);

        SpamResult result = await classifier.ClassifyAsync(
            new SpamCheckRequest("Hi Jane, are we still on for lunch Thursday? - Bob", ApiKey!, Model),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.InRange(result.Confidence, 0.0, 1.0);
    }
}
