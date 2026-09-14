using System.Globalization;
using System.Text.Json;
using Xunit;

namespace SpamDetector.Tests.Unit;

/// <summary>
/// Guards the invariant (FR-020, SC-009): the Functions host <c>functionTimeout</c> must be strictly
/// greater than the app's OpenAI timeout so a provider timeout surfaces as a controlled 502 before
/// the host aborts the invocation.
/// </summary>
public sealed class HostTimeoutConfigTests
{
    [Fact]
    public void Host_function_timeout_is_greater_than_openai_timeout()
    {
        string baseDir = AppContext.BaseDirectory;

        using JsonDocument host = JsonDocument.Parse(File.ReadAllText(Path.Combine(baseDir, "host.json")));
        string functionTimeoutRaw = host.RootElement.GetProperty("functionTimeout").GetString()!;
        TimeSpan functionTimeout = TimeSpan.ParseExact(functionTimeoutRaw, "c", CultureInfo.InvariantCulture);

        using JsonDocument app = JsonDocument.Parse(File.ReadAllText(Path.Combine(baseDir, "appsettings.json")));
        int openAiTimeoutSeconds = app.RootElement
            .GetProperty("SpamDetector")
            .GetProperty("OpenAiTimeoutSeconds")
            .GetInt32();

        Assert.Equal(30, openAiTimeoutSeconds);
        Assert.True(
            functionTimeout > TimeSpan.FromSeconds(openAiTimeoutSeconds),
            $"functionTimeout ({functionTimeout}) must be strictly greater than the OpenAI timeout ({openAiTimeoutSeconds}s).");
    }
}
