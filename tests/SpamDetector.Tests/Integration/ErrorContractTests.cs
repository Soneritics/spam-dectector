using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SpamDetector.Application;
using SpamDetector.Functions;
using SpamDetector.Models;
using Xunit;

namespace SpamDetector.Tests.Integration;

public sealed class ErrorContractTests
{
    [Fact]
    public async Task Missing_key_maps_http_status_to_400()
    {
        SpamCheckFunction function = TestFactory.CreateFunction(new FakeSpamClassifier(TestFactory.NonSpam()));
        HttpRequest request = TestFactory.CreateHttpRequest("content", apiKey: null);

        await AssertStatusMatchesEnvelope(function, request, 400);
    }

    [Fact]
    public async Task Empty_body_maps_http_status_to_400()
    {
        SpamCheckFunction function = TestFactory.CreateFunction(new FakeSpamClassifier(TestFactory.NonSpam()));
        HttpRequest request = TestFactory.CreateHttpRequest("", apiKey: "sk-test-key");

        await AssertStatusMatchesEnvelope(function, request, 400);
    }

    [Fact]
    public async Task Oversized_body_maps_http_status_to_413()
    {
        var options = TestFactory.DefaultOptions();
        options.MaxRequestBodyBytes = 32;
        SpamCheckFunction function = TestFactory.CreateFunction(new FakeSpamClassifier(TestFactory.NonSpam()), options);
        HttpRequest request = TestFactory.CreateHttpRequest(new string('x', 100), apiKey: "sk-test-key");

        await AssertStatusMatchesEnvelope(function, request, 413);
    }

    [Fact]
    public async Task Provider_failure_maps_http_status_to_502()
    {
        var classifier = FakeSpamClassifier.Throwing(
            new SpamClassificationException(SpamClassificationFailure.ProviderError));
        SpamCheckFunction function = TestFactory.CreateFunction(classifier);
        HttpRequest request = TestFactory.CreateHttpRequest("content", apiKey: "sk-test-key");

        await AssertStatusMatchesEnvelope(function, request, 502);
    }

    private static async Task AssertStatusMatchesEnvelope(SpamCheckFunction function, HttpRequest request, int expected)
    {
        var result = (ObjectResult)await function.Run(request, CancellationToken.None);
        var body = Assert.IsType<ApiResult<SpamResult>>(result.Value);

        Assert.Equal(expected, result.StatusCode);
        Assert.Equal(expected, body.HttpCode);
        Assert.Equal(body.HttpCode, result.StatusCode);
        Assert.True(body.IsError);
    }
}
