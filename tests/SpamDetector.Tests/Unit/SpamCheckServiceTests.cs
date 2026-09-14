using SpamDetector.Application;
using SpamDetector.Models;
using Xunit;

namespace SpamDetector.Tests.Unit;

public sealed class SpamCheckServiceTests
{
    [Fact]
    public async Task Returns_success_envelope_for_non_spam_result()
    {
        var classifier = new FakeSpamClassifier(TestFactory.NonSpam());
        SpamCheckService service = TestFactory.CreateService(classifier);

        ApiResult<SpamResult> result = await service.HandleAsync(
            new SpamCheckRequest("hello", "sk-test", "gpt-5.6-luna"), CancellationToken.None);

        Assert.Equal(200, result.HttpCode);
        Assert.False(result.IsError);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Result);
        Assert.False(result.Result!.Spam);
    }

    [Fact]
    public async Task Returns_success_envelope_for_spam_result()
    {
        var classifier = new FakeSpamClassifier(TestFactory.Spam());
        SpamCheckService service = TestFactory.CreateService(classifier);

        ApiResult<SpamResult> result = await service.HandleAsync(
            new SpamCheckRequest("buy now", "sk-test", "gpt-5.6-luna"), CancellationToken.None);

        Assert.Equal(200, result.HttpCode);
        Assert.False(result.IsError);
        Assert.NotNull(result.Result);
        Assert.True(result.Result!.Spam);
    }

    [Fact]
    public async Task Propagates_cancellation_token_to_classifier()
    {
        var classifier = new FakeSpamClassifier(TestFactory.NonSpam());
        SpamCheckService service = TestFactory.CreateService(classifier);
        using var cts = new CancellationTokenSource();

        await service.HandleAsync(new SpamCheckRequest("hi", "sk-test", "gpt-5.6-luna"), cts.Token);

        Assert.Equal(cts.Token, classifier.LastCancellationToken);
    }
}
