using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpamDetector.Application;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

// Strongly-typed options: max request body size, OpenAI timeout, and default model.
builder.Services
    .AddOptions<SpamDetectorOptions>()
    .Bind(builder.Configuration.GetSection(SpamDetectorOptions.SectionName))
    .ValidateOnStart();

// ASP.NET Core OpenAPI document generation (served through the ASP.NET Core integration pipeline).
builder.Services.AddOpenApi();

// DI registration of classification services is completed in the User Story 1 wiring
// (ISpamClassifier, IResponsesClientFactory, SpamCheckService). No singleton OpenAI client
// bound to a fixed credential is ever registered (BYOK isolation).
builder.Services.AddSpamDetectorServices();

builder.Build().Run();
