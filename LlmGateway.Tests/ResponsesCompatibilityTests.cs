using System.Text.Json.Nodes;
using Xunit;

public sealed class ResponsesCompatibilityTests
{
    [Fact]
    public void ConvertsInstructionsAndStringInputToMessages()
    {
        var source = Parse("""
        {
          "model": "test-model",
          "instructions": "Follow the system policy",
          "input": "Hello"
        }
        """);

        Assert.True(ResponsesCompatibility.TryConvertRequest(source, out var target, out var error), error?.Message);

        var messages = target!["messages"]!.AsArray();
        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal("Follow the system policy", messages[0]!["content"]!.GetValue<string>());
        Assert.Equal("user", messages[1]!["role"]!.GetValue<string>());
        Assert.Equal("Hello", messages[1]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void ConvertsDeveloperInputMessageToSystem()
    {
        var source = Parse("""
        {
          "model": "test-model",
          "input": [
            { "type": "message", "role": "developer", "content": "Follow project rules" },
            { "type": "message", "role": "user", "content": "Hello" }
          ]
        }
        """);

        Assert.True(ResponsesCompatibility.TryConvertRequest(source, out var target, out var error), error?.Message);

        var messages = target!["messages"]!.AsArray();
        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal("Follow project rules", messages[0]!["content"]!.GetValue<string>());
        Assert.Equal("user", messages[1]!["role"]!.GetValue<string>());
    }

    [Fact]
    public void ConvertsBuiltInToolsAndToolChoice()
    {
        var source = Parse("""
        {
          "model": "test-model",
          "input": "Find current release information",
          "tools": [
            { "type": "web_search_preview", "search_context_size": "high" },
            { "type": "file_search", "vector_store_ids": ["vs_secret"] },
            { "type": "computer_use_preview" },
            { "type": "function", "name": "get_weather", "parameters": { "type": "object" } }
          ],
          "tool_choice": { "type": "web_search_preview" }
        }
        """);

        Assert.True(ResponsesCompatibility.TryConvertRequest(source, out var target, out var error), error?.Message);

        var functions = target!["tools"]!.AsArray()
            .Select(tool => tool!["function"]!.AsObject())
            .ToArray();
        Assert.Equal(new[] { "web_search", "file_search", "computer_action", "get_weather" },
            functions.Select(function => function["name"]!.GetValue<string>()));
        Assert.Equal("web_search", target["tool_choice"]!["function"]!["name"]!.GetValue<string>());
        Assert.DoesNotContain("vs_secret", target.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertsMcpWithoutLeakingConnectionDetails()
    {
        var source = Parse("""
        {
          "model": "test-model",
          "input": "Use documentation tools",
          "tools": [{
            "type": "mcp",
            "server_label": "Docs Server!",
            "server_url": "https://secret.example/mcp",
            "headers": { "Authorization": "Bearer secret" }
          }],
          "tool_choice": { "type": "mcp", "server_label": "Docs Server!" }
        }
        """);

        Assert.True(ResponsesCompatibility.TryConvertRequest(source, out var target, out var error), error?.Message);

        var json = target!.ToJsonString();
        Assert.Contains("mcp_call_Docs_Server", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret.example", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDuplicateDowngradedFunctionNames()
    {
        var source = Parse("""
        {
          "model": "test-model",
          "input": "Search",
          "tools": [
            { "type": "web_search" },
            { "type": "function", "name": "web_search" }
          ]
        }
        """);

        Assert.False(ResponsesCompatibility.TryConvertRequest(source, out var target, out var error));
        Assert.Null(target);
        Assert.Contains("duplicate function name", error!.Value.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KeepsKnownStringChoiceForDowngradedTools()
    {
        var source = Parse("""
        {
          "model": "test-model",
          "input": "Search",
          "tools": [{ "type": "web_search" }],
          "tool_choice": "required"
        }
        """);

        Assert.True(ResponsesCompatibility.TryConvertRequest(source, out var target, out var error), error?.Message);
        Assert.Equal("required", target!["tool_choice"]!.GetValue<string>());
    }

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();
}
