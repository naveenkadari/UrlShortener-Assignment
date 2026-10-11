using UrlShortener.Services;

namespace UrlShortener.Tests.Unit;

public class CodeGeneratorTests
{
    [Fact]
    public void Generate_ReturnsSevenBase62Characters() =>
        Assert.Matches("^[0-9A-Za-z]{7}$", CodeGenerator.Generate());

    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    public void Generate_RespectsRequestedLength(int length) =>
        Assert.Equal(length, CodeGenerator.Generate(length).Length);

    [Fact]
    public void Generate_ProducesNoDuplicatesIn10kSamples() =>
        Assert.Equal(10_000,
            Enumerable.Range(0, 10_000).Select(_ => CodeGenerator.Generate()).Distinct().Count());
}