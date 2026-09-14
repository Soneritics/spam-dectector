using SpamDetector.Application;
using Xunit;

namespace SpamDetector.Tests.Unit;

public sealed class ModelSelectionTests
{
    private const string Default = "gpt-5.6-luna";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Missing_empty_or_whitespace_model_resolves_to_default(string? requested)
    {
        Assert.Equal(Default, ModelResolver.Resolve(requested, Default));
    }

    [Theory]
    [InlineData("gpt-4o")]
    [InlineData("o4-mini")]
    [InlineData("some-custom-model")]
    public void Explicit_model_is_passed_through_unchanged(string requested)
    {
        Assert.Equal(requested, ModelResolver.Resolve(requested, Default));
    }
}
