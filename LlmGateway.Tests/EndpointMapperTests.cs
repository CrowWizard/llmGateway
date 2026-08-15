using Xunit;

public sealed class EndpointMapperTests
{
    [Theory]
    [InlineData("/v1/images/generations", "/v1/images/generations")]
    public void MapsConfiguredPrefixAndPreservesPathSuffix(string source, string expected)
    {
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["/v1/images/generations"] = "/v1/images/generations"
        };

        Assert.Equal(expected, EndpointMapper.MapPath(source, mappings));
    }

    [Fact]
    public void LeavesUnmappedPathUntouched()
    {
        Assert.Equal("/v1/audio/transcriptions", EndpointMapper.MapPath("/v1/audio/transcriptions", new Dictionary<string, string>()));
    }
}