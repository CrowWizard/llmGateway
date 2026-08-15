using System.Text.Json.Nodes;
using Xunit;

public sealed class ResponsesWebSocketCompatibilityTests
{
    [Fact]
    public void ParsesResponseCreateEventAndRemovesTransportType()
    {
        const string payload = """{ "type": "response.create", "model": "test-model", "generate": false, "input": [] }""";

        var parsed = ResponsesWebSocketCompatibility.TryParseCreateEvent(payload, out var request, out var error);

        Assert.True(parsed, error);
        Assert.Equal("test-model", request!["model"]!.GetValue<string>());
        Assert.False(request.ContainsKey("type"));
        Assert.False(request["generate"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("{\"type\":\"unknown\"}")]
    [InlineData("not json")]
    public void RejectsUnsupportedWebSocketEvents(string payload)
    {
        Assert.False(ResponsesWebSocketCompatibility.TryParseCreateEvent(payload, out var request, out var error));
        Assert.Null(request);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void RetainsGenerateFalseForPrewarmRecognition()
    {
        const string payload = """{ "type": "response.create", "model": "test-model", "generate": false, "input": [] }""";

        Assert.True(ResponsesWebSocketCompatibility.TryParseCreateEvent(payload, out var request, out _));

        Assert.False(request!["generate"]!.GetValue<bool>());
    }
}