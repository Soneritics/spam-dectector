using Microsoft.Extensions.DependencyInjection;
using SpamDetector.Infrastructure.OpenAI;

namespace SpamDetector.Application;

/// <summary>
/// Registers the SpamDetector classification services. No singleton OpenAI client bound to a fixed
/// credential is ever registered — the client is always built per request from the caller's key
/// (BYOK isolation). All registered services are stateless.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSpamDetectorServices(this IServiceCollection services)
    {
        services.AddSingleton<IResponsesClientFactory, ResponsesClientFactory>();
        services.AddSingleton<ISpamClassifier, OpenAISpamClassifier>();
        services.AddSingleton<SpamCheckService>();
        return services;
    }
}
