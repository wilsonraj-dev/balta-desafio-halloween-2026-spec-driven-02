using LinkShortener.Api.Application;
using Xunit;

namespace LinkShortener.Tests.Application;

public sealed class LinkCodeGeneratorTests
{
    [Fact]
    public void GenerateReturnsExactlySevenCharacters()
    {
        var generator = new LinkCodeGenerator();

        for (var sample = 0; sample < 1_000; sample++)
        {
            Assert.Equal(7, generator.Generate().Length);
        }
    }

    [Fact]
    public void GenerateUsesOnlyUppercaseLettersAndDigitsWithoutAmbiguousCharacters()
    {
        const string allowedCharacters = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        var generator = new LinkCodeGenerator();

        for (var sample = 0; sample < 1_000; sample++)
        {
            var code = generator.Generate();

            Assert.All(code, character => Assert.Contains(character, allowedCharacters));
        }
    }
}
