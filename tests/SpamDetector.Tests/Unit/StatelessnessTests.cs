using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using OpenAI.Responses;
using SpamDetector.Application;
using SpamDetector.Infrastructure.OpenAI;
using Xunit;

namespace SpamDetector.Tests.Unit;

public sealed class StatelessnessTests
{
    [Fact]
    public void ResponsesClientFactory_stores_no_credential_state()
    {
        // The factory must hold no instance fields (no stored/captured key).
        FieldInfo[] instanceFields = typeof(ResponsesClientFactory)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.Empty(instanceFields);
    }

    [Fact]
    public void ResponsesClientFactory_creates_a_new_client_per_call()
    {
        var factory = new ResponsesClientFactory();

        ResponsesClient first = factory.Create("sk-key-one");
        ResponsesClient second = factory.Create("sk-key-two");

        Assert.NotSame(first, second);
    }

    [Fact]
    public void No_service_holds_a_static_credential_field()
    {
        foreach (Type type in new[]
                 {
                     typeof(ResponsesClientFactory),
                     typeof(OpenAISpamClassifier),
                     typeof(SpamCheckService)
                 })
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.NotEqual(typeof(string), field.FieldType);
                Assert.NotEqual(typeof(ResponsesClient), field.FieldType);
            }
        }
    }

    [Fact]
    public void DI_registers_no_singleton_ResponsesClient_bound_to_a_fixed_key()
    {
        var services = new ServiceCollection();
        services.AddSpamDetectorServices();

        Assert.DoesNotContain(services, d =>
            d.ServiceType == typeof(ResponsesClient) ||
            d.ImplementationType == typeof(ResponsesClient) ||
            d.ImplementationInstance is ResponsesClient);

        ServiceDescriptor factory = Assert.Single(
            services, d => d.ServiceType == typeof(IResponsesClientFactory));
        Assert.Equal(typeof(ResponsesClientFactory), factory.ImplementationType);
        Assert.Null(factory.ImplementationInstance);
    }

    [Fact]
    public async Task Second_request_is_unaffected_by_the_first()
    {
        var captured = new List<SpamCheckRequest>();
        var classifier = new FakeSpamClassifier((req, _) =>
        {
            captured.Add(req);
            return Task.FromResult(TestFactory.NonSpam());
        });
        SpamCheckService service = TestFactory.CreateService(classifier);

        await service.HandleAsync(new SpamCheckRequest("first body", "sk-key-one", "model-a"), CancellationToken.None);
        await service.HandleAsync(new SpamCheckRequest("second body", "sk-key-two", "model-b"), CancellationToken.None);

        Assert.Equal(2, captured.Count);
        Assert.Equal("first body", captured[0].EmailContent);
        Assert.Equal("second body", captured[1].EmailContent);
        Assert.Equal("sk-key-two", captured[1].ApiKey);
    }
}
