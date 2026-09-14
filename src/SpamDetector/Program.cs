using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using SpamDetector.Application;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

builder.Services
    .AddSingleton<IOpenApiConfigurationOptions>(_ =>
    {
        var options = new OpenApiConfigurationOptions()
        {
            Info = new OpenApiInfo
            {
                Version = "1.0.0",
                Title = "Spam Detector",
                Description = "Spam Detector OpenAPI documentation"
            },
            
            Servers = DefaultOpenApiConfigurationOptions.GetHostNames(),
            OpenApiVersion = OpenApiVersionType.V3,
            IncludeRequestingHostName = true,
            ForceHttp = false,
            ForceHttps = false
        };

        return options;
    })
    .AddOptions<SpamDetectorOptions>()
    .Bind(builder.Configuration.GetSection(SpamDetectorOptions.SectionName))
    .ValidateOnStart();

// DI registration of classification services is completed in the User Story 1 wiring
// (ISpamClassifier, IResponsesClientFactory, SpamCheckService). No singleton OpenAI client
// bound to a fixed credential is ever registered (BYOK isolation).
builder.Services.AddSpamDetectorServices();

builder.Build().Run();
