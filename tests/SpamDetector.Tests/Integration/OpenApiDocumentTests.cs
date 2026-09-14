using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Visitors;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Models;
using NSubstitute;
using SpamDetector.Functions;
using Xunit;

namespace SpamDetector.Tests.Integration;

/// <summary>
/// Verifies that the OpenAPI document produced from the <see cref="SpamCheckFunction"/> attributes by
/// the <c>Microsoft.Azure.Functions.Worker.Extensions.OpenApi</c> extension (the Functions-native
/// replacement for the previous ASP.NET Core <c>AddOpenApi()</c>/<c>MapOpenApi()</c> pipeline) is
/// valid, describes the spam-check endpoint, and never leaks credentials.
/// </summary>
public sealed class OpenApiDocumentTests
{
    private static IHttpRequestDataObject CreateRequest()
    {
        var req = Substitute.For<IHttpRequestDataObject>();
        req.Scheme.Returns("https");
        req.Host.Returns(new HostString("localhost"));
        req.Query.Returns(new QueryCollection());
        return req;
    }

    private static async Task<string> RenderDocumentAsync(OpenApiVersionType version, OpenApiSpecVersion specVersion)
    {
        var helper = new DocumentHelper(new RouteConstraintFilter(), new OpenApiSchemaAcceptor());
        var document = new Document(helper);

        return await document
            .InitialiseDocument()
            .AddMetadata(new OpenApiInfo
            {
                Version = "1.0.0",
                Title = "Spam Detector",
                Description = "Spam Detector OpenAPI documentation"
            })
            .AddServer(CreateRequest(), routePrefix: string.Empty)
            .AddVisitors(VisitorCollection.CreateInstance())
            .Build(typeof(SpamCheckFunction).Assembly, version)
            .RenderAsync(specVersion, OpenApiFormat.Json);
    }

    [Fact]
    public async Task OpenApi_document_is_generated_as_valid_json()
    {
        string content = await RenderDocumentAsync(OpenApiVersionType.V3, OpenApiSpecVersion.OpenApi3_0);

        Assert.False(string.IsNullOrWhiteSpace(content));

        using JsonDocument document = JsonDocument.Parse(content);
        Assert.True(document.RootElement.TryGetProperty("openapi", out _));
    }

    [Fact]
    public async Task OpenApi_document_describes_the_spam_check_endpoint()
    {
        string content = await RenderDocumentAsync(OpenApiVersionType.V3, OpenApiSpecVersion.OpenApi3_0);

        using JsonDocument document = JsonDocument.Parse(content);
        JsonElement paths = document.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/spam-check/email", out JsonElement endpoint));
        Assert.True(endpoint.TryGetProperty("post", out JsonElement post));
        Assert.Equal("SpamCheck", post.GetProperty("operationId").GetString());

        // The BYOK header parameter is documented and required.
        JsonElement parameters = post.GetProperty("parameters");
        var apiKeyParameter = parameters.EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "x-openai-api-key");
        Assert.Equal("header", apiKeyParameter.GetProperty("in").GetString());
        Assert.True(apiKeyParameter.GetProperty("required").GetBoolean());

        // The raw email body is documented as text/plain.
        Assert.True(post.GetProperty("requestBody").GetProperty("content").TryGetProperty("text/plain", out _));
    }

    [Fact]
    public async Task OpenApi_document_never_leaks_credentials()
    {
        string content = await RenderDocumentAsync(OpenApiVersionType.V3, OpenApiSpecVersion.OpenApi3_0);

        Assert.DoesNotContain("sk-", content);
    }

    [Fact]
    public async Task OpenApi_document_can_be_rendered_as_v2()
    {
        string content = await RenderDocumentAsync(OpenApiVersionType.V2, OpenApiSpecVersion.OpenApi2_0);

        using JsonDocument document = JsonDocument.Parse(content);
        Assert.Equal("2.0", document.RootElement.GetProperty("swagger").GetString());
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/spam-check/email", out _));
    }
}
