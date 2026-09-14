using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using SpamDetector.Functions;
using Xunit;

namespace SpamDetector.Tests.Integration;

/// <summary>
/// Verifies that the OpenAPI document registered by <c>AddOpenApi()</c> is actually served at
/// runtime through <see cref="OpenApiFunction"/> (the Functions-native replacement for
/// <c>MapOpenApi()</c>), that unknown document names return 404, and that it never leaks credentials.
/// </summary>
public sealed class OpenApiDocumentTests
{
    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment
        {
            ApplicationName = "SpamDetector",
            EnvironmentName = "Development"
        });
        services.AddOpenApi();
        return services.BuildServiceProvider();
    }

    private static HttpRequest CreateRequest(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        return context.Request;
    }

    [Fact]
    public async Task OpenApi_document_is_served_as_valid_json()
    {
        using ServiceProvider provider = BuildServices();
        var function = new OpenApiFunction(NullLogger<OpenApiFunction>.Instance);

        IActionResult result = await function.Run(CreateRequest(provider), "v1", CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(200, content.StatusCode);
        Assert.Equal("application/json", content.ContentType);
        Assert.False(string.IsNullOrWhiteSpace(content.Content));

        using JsonDocument document = JsonDocument.Parse(content.Content!);
        Assert.True(document.RootElement.TryGetProperty("openapi", out _));
        System.Console.WriteLine("PATHS_DUMP:" + content.Content);

        Assert.DoesNotContain("sk-", content.Content!);
    }

    [Fact]
    public async Task Unknown_document_name_returns_404()
    {
        using ServiceProvider provider = BuildServices();
        var function = new OpenApiFunction(NullLogger<OpenApiFunction>.Instance);

        IActionResult result = await function.Run(CreateRequest(provider), "does-not-exist", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}
