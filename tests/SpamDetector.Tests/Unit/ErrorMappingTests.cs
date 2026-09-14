using SpamDetector.Application;
using SpamDetector.Models;
using Xunit;

namespace SpamDetector.Tests.Unit;

public sealed class ErrorMappingTests
{
    [Theory]
    [InlineData(SpamClassificationFailure.Authentication)]
    [InlineData(SpamClassificationFailure.RateLimit)]
    [InlineData(SpamClassificationFailure.Timeout)]
    [InlineData(SpamClassificationFailure.ProviderError)]
    [InlineData(SpamClassificationFailure.ProviderRejected)]
    [InlineData(SpamClassificationFailure.InvalidResponse)]
    public async Task Provider_failures_map_to_502_with_safe_category(SpamClassificationFailure failure)
    {
        var classifier = FakeSpamClassifier.Throwing(new SpamClassificationException(failure));
        SpamCheckService service = TestFactory.CreateService(classifier);

        ApiResult<SpamResult> result = await service.HandleAsync(
            new SpamCheckRequest("body", "sk-test", "gpt-5.6-luna"), CancellationToken.None);

        Assert.Equal(502, result.HttpCode);
        Assert.True(result.IsError);
        Assert.Null(result.Result);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task Unexpected_exception_maps_to_500()
    {
        var classifier = FakeSpamClassifier.Throwing(new InvalidOperationException("boom internal detail"));
        SpamCheckService service = TestFactory.CreateService(classifier);

        ApiResult<SpamResult> result = await service.HandleAsync(
            new SpamCheckRequest("body", "sk-test", "gpt-5.6-luna"), CancellationToken.None);

        Assert.Equal(500, result.HttpCode);
        Assert.True(result.IsError);
        Assert.Equal("An unexpected error occurred.", result.ErrorMessage);
        Assert.DoesNotContain("boom internal detail", result.ErrorMessage);
    }

    [Fact]
    public async Task Genuine_client_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var classifier = FakeSpamClassifier.Throwing(new OperationCanceledException(cts.Token));
        SpamCheckService service = TestFactory.CreateService(classifier);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.HandleAsync(new SpamCheckRequest("body", "sk-test", "gpt-5.6-luna"), cts.Token));
    }
}
