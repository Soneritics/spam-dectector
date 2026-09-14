using Microsoft.Extensions.Logging;
using SpamDetector.Models;

namespace SpamDetector.Application;

/// <summary>
/// Spam classification use case. Invokes the classifier boundary and maps outcomes to a controlled
/// <see cref="ApiResult{T}"/>. Provider failures surface as HTTP 502 and unexpected errors as 500;
/// no key, header, stack trace, or raw payload is ever exposed.
/// </summary>
public sealed class SpamCheckService
{
    private readonly ISpamClassifier _classifier;
    private readonly ILogger<SpamCheckService> _logger;

    public SpamCheckService(ISpamClassifier classifier, ILogger<SpamCheckService> logger)
    {
        _classifier = classifier;
        _logger = logger;
    }

    public async Task<ApiResult<SpamResult>> HandleAsync(SpamCheckRequest request, CancellationToken cancellationToken)
    {
        try
        {
            SpamResult result = await _classifier.ClassifyAsync(request, cancellationToken).ConfigureAwait(false);

            return new ApiResult<SpamResult>
            {
                Result = result,
                HttpCode = 200,
                IsError = false,
                ErrorMessage = null
            };
        }
        catch (SpamClassificationException ex)
        {
            _logger.LogWarning("Classification failed with category {Category}", ex.Failure);

            return new ApiResult<SpamResult>
            {
                Result = null,
                HttpCode = ex.HttpCode,
                IsError = true,
                ErrorMessage = ex.SafeMessage
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Genuine client cancellation — let the host observe the cancellation.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during classification");

            return new ApiResult<SpamResult>
            {
                Result = null,
                HttpCode = 500,
                IsError = true,
                ErrorMessage = "An unexpected error occurred."
            };
        }
    }
}
