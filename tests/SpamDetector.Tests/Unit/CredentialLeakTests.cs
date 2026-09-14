using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SpamDetector.Application;
using SpamDetector.Functions;
using SpamDetector.Models;
using Xunit;

namespace SpamDetector.Tests.Unit;

public sealed class CredentialLeakTests
{
    private const string SecretKey = "sk-super-secret-KEY-should-never-leak-1234567890";

    public static IEnumerable<object[]> FailingClassifiers()
    {
        yield return new object[] { FakeSpamClassifier.Throwing(new SpamClassificationException(SpamClassificationFailure.Authentication)) };
        yield return new object[] { FakeSpamClassifier.Throwing(new SpamClassificationException(SpamClassificationFailure.ProviderError)) };
        yield return new object[] { FakeSpamClassifier.Throwing(new SpamClassificationException(SpamClassificationFailure.InvalidResponse)) };
        yield return new object[] { FakeSpamClassifier.Throwing(new InvalidOperationException($"leak {SecretKey}")) };
    }

    [Theory]
    [MemberData(nameof(FailingClassifiers))]
    public async Task Api_key_never_appears_in_error_output(FakeSpamClassifier classifier)
    {
        SpamCheckFunction function = TestFactory.CreateFunction(classifier);
        HttpRequest request = TestFactory.CreateHttpRequest("some email body", apiKey: SecretKey);

        var result = (ObjectResult)await function.Run(request, CancellationToken.None);
        var body = Assert.IsType<ApiResult<SpamResult>>(result.Value);

        Assert.True(body.IsError);
        Assert.NotNull(body.ErrorMessage);
        Assert.DoesNotContain(SecretKey, body.ErrorMessage!);

        string serialized = JsonSerializer.Serialize(body);
        Assert.DoesNotContain(SecretKey, serialized);
    }

    [Fact]
    public void Classification_exception_message_never_contains_the_key()
    {
        // The exception is constructed only from a category and never carries the key.
        foreach (SpamClassificationFailure failure in Enum.GetValues<SpamClassificationFailure>())
        {
            var ex = new SpamClassificationException(failure);
            Assert.DoesNotContain(SecretKey, ex.Message);
            Assert.DoesNotContain(SecretKey, ex.SafeMessage);
        }
    }

    [Fact]
    public async Task Missing_key_error_does_not_echo_any_key_value()
    {
        var classifier = new FakeSpamClassifier(TestFactory.NonSpam());
        SpamCheckFunction function = TestFactory.CreateFunction(classifier);
        // Key omitted entirely; error should only name the header, never a value.
        HttpRequest request = TestFactory.CreateHttpRequest("content", apiKey: null);

        var result = (ObjectResult)await function.Run(request, CancellationToken.None);
        var body = Assert.IsType<ApiResult<SpamResult>>(result.Value);

        Assert.DoesNotContain(SecretKey, body.ErrorMessage!);
    }
}
