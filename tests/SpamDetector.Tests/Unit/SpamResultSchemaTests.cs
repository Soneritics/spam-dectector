using System.Text.Json;
using SpamDetector.Infrastructure.OpenAI;
using Xunit;

namespace SpamDetector.Tests.Unit;

public sealed class SpamResultSchemaTests
{
    [Fact]
    public void Schema_name_is_spam_result()
    {
        Assert.Equal("spam_result", SpamResultSchema.SchemaName);
    }

    [Fact]
    public void AsBinaryData_returns_the_schema_json()
    {
        BinaryData data = SpamResultSchema.AsBinaryData();

        Assert.Equal(SpamResultSchema.SchemaJson, data.ToString());
    }

    [Fact]
    public void Schema_is_strict_and_bounds_confidence_between_0_and_1()
    {
        using JsonDocument document = SpamResultSchema.AsJsonDocument();
        JsonElement root = document.RootElement;

        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());

        JsonElement confidence = root.GetProperty("properties").GetProperty("confidence");
        Assert.Equal(0, confidence.GetProperty("minimum").GetInt32());
        Assert.Equal(1, confidence.GetProperty("maximum").GetInt32());

        var required = root.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(required.SetEquals(new[] { "spam", "confidence", "promptInjectionDetected", "reason" }));
    }
}
