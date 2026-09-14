using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SpamDetector.Application;
using SpamDetector.Functions;
using SpamDetector.Models;
using Xunit;

namespace SpamDetector.Tests.Unit;

public sealed class ValidationTests
{
    [Fact]
    public async Task Missing_api_key_returns_400_and_does_not_call_provider()
    {
        var classifier = new FakeSpamClassifier(TestFactory.NonSpam());
        SpamCheckFunction function = TestFactory.CreateFunction(classifier);
        HttpRequest request = TestFactory.CreateHttpRequest("some content", apiKey: null);

        var result = (ObjectResult)await function.Run(request, CancellationToken.None);
        var body = Assert.IsType<ApiResult<SpamResult>>(result.Value);

        Assert.Equal(400, result.StatusCode);
        Assert.Equal(400, body.HttpCode);
        Assert.True(body.IsError);
        Assert.Equal(0, classifier.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task Empty_or_whitespace_body_returns_400_and_does_not_call_provider(string body)
    {
        var classifier = new FakeSpamClassifier(TestFactory.NonSpam());
        SpamCheckFunction function = TestFactory.CreateFunction(classifier);
        HttpRequest request = TestFactory.CreateHttpRequest(body, apiKey: "sk-test-key");

        var result = (ObjectResult)await function.Run(request, CancellationToken.None);
        var envelope = Assert.IsType<ApiResult<SpamResult>>(result.Value);

        Assert.Equal(400, result.StatusCode);
        Assert.True(envelope.IsError);
        Assert.Equal(0, classifier.CallCount);
    }

    [Fact]
    public async Task Oversized_body_returns_413_and_does_not_call_provider()
    {
        var options = TestFactory.DefaultOptions();
        options.MaxRequestBodyBytes = 1_048_576;
        var classifier = new FakeSpamClassifier(TestFactory.NonSpam());
        SpamCheckFunction function = TestFactory.CreateFunction(classifier, options);

        string oversized = new('a', (int)options.MaxRequestBodyBytes + 1);
        HttpRequest request = TestFactory.CreateHttpRequest(oversized, apiKey: "sk-test-key");

        var result = (ObjectResult)await function.Run(request, CancellationToken.None);
        var body = Assert.IsType<ApiResult<SpamResult>>(result.Value);

        Assert.Equal(413, result.StatusCode);
        Assert.Equal(413, body.HttpCode);
        Assert.True(body.IsError);
        Assert.Equal(0, classifier.CallCount);
    }

    [Fact]
    public async Task Oversized_body_without_content_length_is_rejected_by_bounded_read()
    {
        var options = TestFactory.DefaultOptions();
        options.MaxRequestBodyBytes = 64;
        var classifier = new FakeSpamClassifier(TestFactory.NonSpam());
        SpamCheckFunction function = TestFactory.CreateFunction(classifier, options);

        string oversized = new('b', 200);
        HttpRequest request = TestFactory.CreateHttpRequest(oversized, apiKey: "sk-test-key", setContentLength: false);

        var result = (ObjectResult)await function.Run(request, CancellationToken.None);

        Assert.Equal(413, result.StatusCode);
        Assert.Equal(0, classifier.CallCount);
    }

    [Fact]
    public async Task Body_at_the_limit_is_accepted()
    {
        var options = TestFactory.DefaultOptions();
        options.MaxRequestBodyBytes = 64;
        var classifier = new FakeSpamClassifier(TestFactory.NonSpam());
        SpamCheckFunction function = TestFactory.CreateFunction(classifier, options);

        string atLimit = new('c', 64);
        HttpRequest request = TestFactory.CreateHttpRequest(atLimit, apiKey: "sk-test-key");

        var result = (ObjectResult)await function.Run(request, CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(1, classifier.CallCount);
    }
}
