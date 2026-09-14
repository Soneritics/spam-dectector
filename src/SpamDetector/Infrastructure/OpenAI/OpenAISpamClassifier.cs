using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Responses;
using SpamDetector.Application;
using SpamDetector.Models;

namespace SpamDetector.Infrastructure.OpenAI;

/// <summary>
/// <see cref="ISpamClassifier"/> implementation backed by the OpenAI Responses API. Builds a
/// per-request client from the caller's key, sends a trusted system instruction plus the untrusted
/// email as separate user input, forces strict Structured Outputs, and deserializes the result.
/// No tools are ever enabled. Applies a finite timeout and propagates cancellation.
/// </summary>
public sealed class OpenAISpamClassifier(
    IResponsesClientFactory clientFactory,
    IOptions<SpamDetectorOptions> options,
    ILogger<OpenAISpamClassifier> logger)
    : ISpamClassifier
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly SpamDetectorOptions _options = options.Value;

    public async Task<SpamResult> ClassifyAsync(SpamCheckRequest request, CancellationToken cancellationToken)
    {
        ResponsesClient client = clientFactory.Create(request.ApiKey);

        // Email content is ONLY ever supplied here, as untrusted user input.
        var inputItems = new List<ResponseItem>
        {
            ResponseItem.CreateUserMessageItem(SpamClassifierPrompt.Wrap(request.EmailContent))
        };

        var createOptions = new CreateResponseOptions(request.Model, inputItems)
        {
            // Trusted, static system instruction — never built from email content.
            Instructions = SpamClassifierPrompt.SystemInstruction,
            TextOptions = new ResponseTextOptions
            {
                TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                    SpamResultSchema.SchemaName,
                    SpamResultSchema.AsBinaryData(),
                    jsonSchemaFormatDescription: null,
                    jsonSchemaIsStrict: true)
            }
            // No Tools configured: email content cannot trigger any external action.
        };

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.OpenAiTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        ResponseResult response;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            response = await client.CreateResponseAsync(createOptions, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("OpenAI classification timed out after {TimeoutSeconds}s", _options.OpenAiTimeoutSeconds);
            throw new SpamClassificationException(SpamClassificationFailure.Timeout);
        }
        catch (System.ClientModel.ClientResultException ex)
        {
            SpamClassificationFailure failure = MapStatus(ex.Status);
            logger.LogWarning("OpenAI classification failed with provider status {Status} ({Category})", ex.Status, failure);
            throw new SpamClassificationException(failure);
        }
        finally
        {
            stopwatch.Stop();
        }

        logger.LogInformation("Classification completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

        string outputText = response.GetOutputText();
        if (string.IsNullOrWhiteSpace(outputText))
        {
            logger.LogWarning("OpenAI classification returned empty structured output");
            throw new SpamClassificationException(SpamClassificationFailure.InvalidResponse);
        }

        try
        {
            SpamResult? result = JsonSerializer.Deserialize<SpamResult>(outputText, SerializerOptions);
            if (result is null)
            {
                throw new SpamClassificationException(SpamClassificationFailure.InvalidResponse);
            }

            return result;
        }
        catch (JsonException)
        {
            logger.LogWarning("OpenAI classification returned undeserializable structured output");
            throw new SpamClassificationException(SpamClassificationFailure.InvalidResponse);
        }
    }

    private static SpamClassificationFailure MapStatus(int status) => status switch
    {
        401 or 403 => SpamClassificationFailure.Authentication,
        429 => SpamClassificationFailure.RateLimit,
        400 or 404 or 422 => SpamClassificationFailure.ProviderRejected,
        >= 500 => SpamClassificationFailure.ProviderError,
        _ => SpamClassificationFailure.ProviderError
    };
}
