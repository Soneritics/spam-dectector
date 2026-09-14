using SpamDetector.Models;

namespace SpamDetector.Application;

/// <summary>
/// Single external boundary abstraction for spam classification. Exposes no OpenAI-specific types
/// so it can be replaced by a fake in tests.
/// </summary>
public interface ISpamClassifier
{
    Task<SpamResult> ClassifyAsync(SpamCheckRequest request, CancellationToken cancellationToken);
}
