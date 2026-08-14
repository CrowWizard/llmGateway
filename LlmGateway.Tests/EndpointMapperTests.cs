using Xunit;

public sealed class EndpointMapperTests
{
    [Theory]
    [InlineData("/v1/images/generations", "/v1/images/generations")]
    [InlineData("/v1beta/models/gemini-2.5-pro:generateContent", "/v1beta/models/gemini-2.5-pro:generateContent")]
    [InlineData("/v1/gemini-openai/chat/completions", "/v1beta/openai/chat/completions")]
    public void MapsConfiguredPrefixAndPreservesPathSuffix(string source, string expected)
    {
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["/v1/images/generations"] = "/v1/images/generations",
            ["/v1beta"] = "/v1beta",
            ["/v1/gemini-openai"] = "/v1beta/openai"
        };

        Assert.Equal(expected, EndpointMapper.MapPath(source, mappings));
    }

    [Fact]
    public void LeavesUnmappedPathUntouched()
    {
        Assert.Equal("/v1/audio/transcriptions", EndpointMapper.MapPath("/v1/audio/transcriptions", new Dictionary<string, string>()));
    }
}