using Xunit;

public sealed class GatewayEndpointTests
{
    [Theory]
    [InlineData("https://api.example.com", "v1/models", "https://api.example.com/v1/models")]
    [InlineData("https://api.example.com/v1", "v1/models", "https://api.example.com/v1/models")]
    [InlineData("https://api.example.com/v1/", "/v1/chat/completions", "https://api.example.com/v1/chat/completions")]
    public void BuildApiUriPreservesOrAddsV1Prefix(string baseUrl, string path, string expected)
    {
        var endpoint = new GatewayEndpoint { BaseUrl = baseUrl };

        Assert.Equal(expected, endpoint.BuildApiUri(path).ToString());
    }

    [Fact]
    public void IsValidRequiresAnEnabledHttpEndpointAndOriginalKey()
    {
        var endpoint = new GatewayEndpoint
        {
            Name = "primary",
            BaseUrl = "https://api.example.com",
            ApiKey = "upstream-key"
        };

        Assert.True(endpoint.IsValid);

        endpoint.Enabled = false;
        Assert.False(endpoint.IsValid);
    }

    [Fact]
    public void CreateGatewayApiKeyCreatesOpenAiStyleLocalKey()
    {
        var key = GatewayEndpoint.CreateGatewayApiKey();

        Assert.Matches("^sk-[0-9a-f]{48}$", key);
    }
}