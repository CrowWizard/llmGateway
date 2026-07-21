using System.Text.Json;
using System.Text.Json.Nodes;
using LlmGateway.Desktop.Models;

namespace LlmGateway.Desktop.Services;

public sealed class GatewaySettingsService(AppPaths paths)
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public string SettingsPath => paths.GatewaySettingsPath;

    public GatewaySettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new GatewaySettings();
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject;
            return root?["Gateway"]?.Deserialize<GatewaySettings>() ?? new GatewaySettings();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"网关配置不是有效 JSON：{exception.Message}", exception);
        }
    }

    public async Task SaveAsync(GatewaySettings settings, CancellationToken cancellationToken = default)
    {
        JsonObject root;
        try
        {
            root = File.Exists(SettingsPath)
                ? JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath, cancellationToken)) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"网关配置不是有效 JSON：{exception.Message}", exception);
        }

        root["Gateway"] = JsonSerializer.SerializeToNode(settings, WriteOptions);
        await AtomicFile.WriteUtf8Async(SettingsPath, root.ToJsonString(WriteOptions) + Environment.NewLine, cancellationToken);
    }
}
