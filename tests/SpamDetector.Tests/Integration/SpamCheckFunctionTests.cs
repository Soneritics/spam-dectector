using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SpamDetector.Application;
using SpamDetector.Functions;
using SpamDetector.Models;
using Xunit;

namespace SpamDetector.Tests.Integration;

public sealed class SpamCheckFunctionTests
{
    [Fact]
    public async Task Valid_request_returns_200_with_well_formed_envelope()
    {
        var classifier = new FakeSpamClassifier(TestFactory.NonSpam());
        SpamCheckFunction function = TestFactory.CreateFunction(classifier);
        HttpRequest request = TestFactory.CreateHttpRequest(
            "Hi Jane, lunch Thursday?", apiKey: "sk-test-key");

        IActionResult actionResult = await function.Run(request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(actionResult);
        var body = Assert.IsType<ApiResult<SpamResult>>(objectResult.Value);
        Assert.Equal(body.HttpCode, objectResult.StatusCode);
        Assert.Equal(200, body.HttpCode);
        Assert.False(body.IsError);
        Assert.NotNull(body.Result);
        Assert.Equal(1, classifier.CallCount);
    }

    [Fact]
    public async Task Default_model_used_when_model_header_absent()
    {
        var classifier = new FakeSpamClassifier(TestFactory.NonSpam());
        SpamCheckFunction function = TestFactory.CreateFunction(classifier);
        HttpRequest request = TestFactory.CreateHttpRequest("content", apiKey: "sk-test-key");

        await function.Run(request, CancellationToken.None);

        Assert.Equal("gpt-5.6-luna", classifier.LastRequest!.Model);
    }

    [Fact]
    public async Task Explicit_model_header_is_passed_through()
    {
        var classifier = new FakeSpamClassifier(TestFactory.NonSpam());
        SpamCheckFunction function = TestFactory.CreateFunction(classifier);
        HttpRequest request = TestFactory.CreateHttpRequest("content", apiKey: "sk-test-key", model: "gpt-4o");

        await function.Run(request, CancellationToken.None);

        Assert.Equal("gpt-4o", classifier.LastRequest!.Model);
    }
}
