using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Responses;
using SpamDetector.Application;
using SpamDetector.Infrastructure.OpenAI;
using SpamDetector.Models;
using Xunit;

namespace SpamDetector.Tests.Unit;

/// <summary>
/// Exercises <see cref="OpenAISpamClassifier"/> against a stubbed OpenAI Responses client so the
/// full success, timeout, provider-error, and invalid-response paths are covered without any live
/// network call.
/// </summary>
public sealed class OpenAISpamClassifierTests
{
    private static OpenAISpamClassifier CreateClassifier(IResponsesClientFactory factory, SpamDetectorOptions? options = null) =>
        new(factory, TestFactory.Options(options), NullLogger<OpenAISpamClassifier>.Instance);

    private static SpamCheckRequest Request(string body = "email body", string apiKey = "sk-test-key", string model = "gpt-5.6-luna") =>
        new(body, apiKey, model);

    // Builds a real ResponseResult whose GetOutputText() returns the given text, using the SDK's
    // JSON persistence contract (the same wire shape the Responses API returns).
    private static ResponseResult ResultWithOutputText(string outputText)
    {
        string escaped = JsonSerializer.Serialize(outputText);
        string wire =
            "{\"id\":\"resp_1\",\"object\":\"response\",\"created_at\":0,\"model\":\"m\",\"status\":\"completed\"," +
            "\"output\":[{\"type\":\"message\",\"id\":\"msg_1\",\"role\":\"assistant\",\"status\":\"completed\"," +
            "\"content\":[{\"type\":\"output_text\",\"text\":" + escaped + ",\"annotations\":[]}]}]}";

        return ModelReaderWriter.Read<ResponseResult>(BinaryData.FromString(wire))!;
    }

    private static ClientResult<ResponseResult> Ok(ResponseResult result) =>
        ClientResult.FromValue(result, new StubPipelineResponse(200));

    [Fact]
    public async Task Successful_response_is_deserialized_into_a_spam_result()
    {
        string json = JsonSerializer.Serialize(new SpamResult
        {
            Spam = true,
            Confidence = 0.91,
            PromptInjectionDetected = true,
            Reason = "Phishing."
        });

        var factory = new StubClientFactory(new StubResponsesClient((_, _) =>
            Task.FromResult(Ok(ResultWithOutputText(json)))));

        SpamResult result = await CreateClassifier(factory).ClassifyAsync(Request(), CancellationToken.None);

        Assert.True(result.Spam);
        Assert.Equal(0.91, result.Confidence);
        Assert.True(result.PromptInjectionDetected);
        Assert.Equal("Phishing.", result.Reason);
    }

    [Fact]
    public async Task Client_is_built_from_the_callers_api_key()
    {
        var factory = new StubClientFactory(new StubResponsesClient((_, _) =>
            Task.FromResult(Ok(ResultWithOutputText(JsonSerializer.Serialize(TestFactory.NonSpam()))))));

        await CreateClassifier(factory).ClassifyAsync(Request(apiKey: "sk-caller-key"), CancellationToken.None);

        Assert.Equal("sk-caller-key", factory.LastApiKey);
    }

    [Fact]
    public async Task Empty_output_text_maps_to_invalid_response()
    {
        var factory = new StubClientFactory(new StubResponsesClient((_, _) =>
            Task.FromResult(Ok(new ResponseResult()))));

        var ex = await Assert.ThrowsAsync<SpamClassificationException>(() =>
            CreateClassifier(factory).ClassifyAsync(Request(), CancellationToken.None));

        Assert.Equal(SpamClassificationFailure.InvalidResponse, ex.Failure);
    }

    [Fact]
    public async Task Undeserializable_output_maps_to_invalid_response()
    {
        var factory = new StubClientFactory(new StubResponsesClient((_, _) =>
            Task.FromResult(Ok(ResultWithOutputText("this is not json {{{")))));

        var ex = await Assert.ThrowsAsync<SpamClassificationException>(() =>
            CreateClassifier(factory).ClassifyAsync(Request(), CancellationToken.None));

        Assert.Equal(SpamClassificationFailure.InvalidResponse, ex.Failure);
    }

    [Fact]
    public async Task Null_json_output_maps_to_invalid_response()
    {
        var factory = new StubClientFactory(new StubResponsesClient((_, _) =>
            Task.FromResult(Ok(ResultWithOutputText("null")))));

        var ex = await Assert.ThrowsAsync<SpamClassificationException>(() =>
            CreateClassifier(factory).ClassifyAsync(Request(), CancellationToken.None));

        Assert.Equal(SpamClassificationFailure.InvalidResponse, ex.Failure);
    }

    [Theory]
    [InlineData(401, SpamClassificationFailure.Authentication)]
    [InlineData(403, SpamClassificationFailure.Authentication)]
    [InlineData(429, SpamClassificationFailure.RateLimit)]
    [InlineData(400, SpamClassificationFailure.ProviderRejected)]
    [InlineData(404, SpamClassificationFailure.ProviderRejected)]
    [InlineData(422, SpamClassificationFailure.ProviderRejected)]
    [InlineData(500, SpamClassificationFailure.ProviderError)]
    [InlineData(503, SpamClassificationFailure.ProviderError)]
    [InlineData(418, SpamClassificationFailure.ProviderError)]
    public async Task Provider_status_codes_map_to_the_expected_failure_category(int status, SpamClassificationFailure expected)
    {
        var factory = new StubClientFactory(new StubResponsesClient((_, _) =>
            throw new ClientResultException(new StubPipelineResponse(status))));

        var ex = await Assert.ThrowsAsync<SpamClassificationException>(() =>
            CreateClassifier(factory).ClassifyAsync(Request(), CancellationToken.None));

        Assert.Equal(expected, ex.Failure);
        Assert.Equal(502, ex.HttpCode);
    }

    [Fact]
    public async Task Provider_timeout_maps_to_timeout_failure()
    {
        var options = TestFactory.DefaultOptions();
        options.OpenAiTimeoutSeconds = 0; // Fire the internal timeout immediately.

        var factory = new StubClientFactory(new StubResponsesClient(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Ok(new ResponseResult());
        }));

        var ex = await Assert.ThrowsAsync<SpamClassificationException>(() =>
            CreateClassifier(factory, options).ClassifyAsync(Request(), CancellationToken.None));

        Assert.Equal(SpamClassificationFailure.Timeout, ex.Failure);
    }

    [Fact]
    public async Task Genuine_caller_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var factory = new StubClientFactory(new StubResponsesClient(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Ok(new ResponseResult());
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateClassifier(factory).ClassifyAsync(Request(), cts.Token));
    }

    private sealed class StubClientFactory(ResponsesClient client) : IResponsesClientFactory
    {
        public string? LastApiKey { get; private set; }

        public ResponsesClient Create(string apiKey)
        {
            LastApiKey = apiKey;
            return client;
        }
    }

    private sealed class StubResponsesClient(
        Func<CreateResponseOptions, CancellationToken, Task<ClientResult<ResponseResult>>> behavior)
        : ResponsesClient(new ApiKeyCredential("sk-stub-key"))
    {
        public override Task<ClientResult<ResponseResult>> CreateResponseAsync(
            CreateResponseOptions options, CancellationToken cancellationToken) => behavior(options, cancellationToken);
    }

    private sealed class StubPipelineResponse(int status) : PipelineResponse
    {
        private Stream _contentStream = new MemoryStream();

        public override int Status { get; } = status;
        public override string ReasonPhrase => string.Empty;
        protected override PipelineResponseHeaders HeadersCore { get; } = new StubHeaders();
        public override Stream? ContentStream { get => _contentStream; set => _contentStream = value ?? new MemoryStream(); }
        public override BinaryData Content => BinaryData.FromString(string.Empty);
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => new(Content);
        public override void Dispose() => _contentStream.Dispose();

        private sealed class StubHeaders : PipelineResponseHeaders
        {
            public override bool TryGetValue(string name, out string? value)
            {
                value = null;
                return false;
            }

            public override bool TryGetValues(string name, out IEnumerable<string>? values)
            {
                values = null;
                return false;
            }

            public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
                Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
        }
    }
}
