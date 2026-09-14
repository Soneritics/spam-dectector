using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpamDetector.Application;
using SpamDetector.Models;

namespace SpamDetector.Functions;

/// <summary>
/// HTTP-triggered endpoint <c>POST /spam-check/email</c>. Reads the raw email body (never parsed as
/// JSON), validates the BYOK header and body, resolves the model, and returns an
/// <see cref="ApiResult{T}"/> whose HTTP status equals <see cref="ApiResult{T}.HttpCode"/>.
/// </summary>
public sealed class SpamCheckFunction(
    SpamCheckService service,
    IOptions<SpamDetectorOptions> options,
    ILogger<SpamCheckFunction> logger)
{
    private const string ApiKeyHeader = "x-openai-api-key";
    private const string ModelHeader = "x-openai-model";

    private readonly SpamDetectorOptions _options = options.Value;

    [Function("SpamCheck")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "spam-check/email")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        string? apiKey = request.Headers.TryGetValue(ApiKeyHeader, out var apiKeyValues)
            ? apiKeyValues.ToString()
            : null;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogInformation("Rejected request: missing {Header} header", ApiKeyHeader);
            return ErrorResult(400, $"Missing required header: {ApiKeyHeader}.");
        }

        long limit = _options.MaxRequestBodyBytes;

        // Fast reject via Content-Length when present; the bounded read below is authoritative.
        if (request.ContentLength is long declaredLength && declaredLength > limit)
        {
            logger.LogInformation("Rejected request: body exceeds the maximum allowed size");
            return ErrorResult(413, "Request body exceeds the maximum allowed size.");
        }

        (string? body, bool tooLarge) = await ReadBoundedBodyAsync(request.Body, limit, cancellationToken)
            .ConfigureAwait(false);

        if (tooLarge)
        {
            logger.LogInformation("Rejected request: body exceeds the maximum allowed size");
            return ErrorResult(413, "Request body exceeds the maximum allowed size.");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            logger.LogInformation("Rejected request: empty body");
            return ErrorResult(400, "Request body is empty.");
        }

        string? requestedModel = request.Headers.TryGetValue(ModelHeader, out var modelValues)
            ? modelValues.ToString()
            : null;
        string model = ModelResolver.Resolve(requestedModel, _options.DefaultModel);

        var spamRequest = new SpamCheckRequest(body, apiKey, model);
        ApiResult<SpamResult> result = await service.HandleAsync(spamRequest, cancellationToken).ConfigureAwait(false);

        return new ObjectResult(result) { StatusCode = result.HttpCode };
    }

    /// <summary>
    /// Reads the body as raw UTF-8 text without ever buffering more than <paramref name="limit"/>
    /// bytes. Returns <c>tooLarge</c> = true if the stream exceeds the limit.
    /// </summary>
    private static async Task<(string? body, bool tooLarge)> ReadBoundedBodyAsync(
        Stream stream, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] rented = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > limit)
                {
                    return (null, true);
                }

                buffer.Write(rented, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        return (Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length), false);
    }

    private static ObjectResult ErrorResult(int httpCode, string message)
    {
        var result = new ApiResult<SpamResult>
        {
            Result = null,
            HttpCode = httpCode,
            IsError = true,
            ErrorMessage = message
        };

        return new ObjectResult(result) { StatusCode = httpCode };
    }
}
