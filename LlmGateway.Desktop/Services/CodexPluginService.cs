using System.Text.Json;
using System.Text.Json.Nodes;

namespace LlmGateway.Desktop.Services;

public sealed class CodexPluginService(AppPaths paths)
{
    public string InstallEcommerceImageStudio()
    {
        var sourceDirectory = ResolveEcommerceImageStudioDirectory();
        if (sourceDirectory is null)
        {
            var manifestPath = Path.Combine(paths.EcommerceImageStudioDirectory, ".codex-plugin", "plugin.json");
            throw new InvalidOperationException($"未找到内置电商生图插件清单。请确认安装包包含 {manifestPath}。当前应用目录：{paths.ApplicationDirectory}");
        }

        var targetDirectory = Path.Combine(paths.CodexPluginsDirectory, "ecommerce-image-studio");
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var targetFile = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile, true);
        }

        InstallAgentsMarketplace();
        return targetDirectory;
    }

    private void InstallAgentsMarketplace()
    {
        var marketplaceSourcePath = paths.EcommerceImageStudioMarketplacePath;
        if (!File.Exists(marketplaceSourcePath))
        {
            throw new InvalidOperationException($"未找到内置插件市场文件：{marketplaceSourcePath}");
        }

        if (!File.Exists(paths.AgentsMarketplacePath))
        {
            Directory.CreateDirectory(paths.AgentsPluginsDirectory);
            File.Copy(marketplaceSourcePath, paths.AgentsMarketplacePath, true);
            return;
        }

        AtomicFile.BackupIfExists(paths.AgentsMarketplacePath);
        var marketplace = ParseMarketplace(AtomicFile.ReadUtf8(paths.AgentsMarketplacePath), paths.AgentsMarketplacePath);
        var plugins = marketplace["plugins"] as JsonArray;
        if (plugins is null)
        {
            plugins = [];
            marketplace["plugins"] = plugins;
        }

        if (!plugins.OfType<JsonObject>().Any(plugin =>
                string.Equals(plugin["name"]?.GetValue<string>(), "ecommerce-image-studio", StringComparison.Ordinal)))
        {
            plugins.Add(CreateEcommerceImageStudioMarketplaceEntry());
        }

        AtomicFile.WriteUtf8Async(
                paths.AgentsMarketplacePath,
                marketplace.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine)
            .GetAwaiter()
            .GetResult();
    }

    private static JsonObject ParseMarketplace(string content, string path)
    {
        try
        {
            return JsonNode.Parse(content)?.AsObject()
                ?? throw new InvalidOperationException("根节点必须是 JSON 对象。");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"插件市场文件不是有效 JSON：{path}", exception);
        }
    }

    private static JsonObject CreateEcommerceImageStudioMarketplaceEntry() => new()
    {
        ["name"] = "ecommerce-image-studio",
        ["source"] = new JsonObject
        {
            ["source"] = "local",
            ["path"] = "./.codex/plugins/ecommerce-image-studio"
        },
        ["policy"] = new JsonObject
        {
            ["installation"] = "AVAILABLE",
            ["authentication"] = "ON_INSTALL"
        },
        ["category"] = "Productivity"
    };

    private string? ResolveEcommerceImageStudioDirectory()
    {
        if (HasManifest(paths.EcommerceImageStudioDirectory))
        {
            return paths.EcommerceImageStudioDirectory;
        }

        return null;
    }

    private static bool HasManifest(string directory) =>
        File.Exists(Path.Combine(directory, ".codex-plugin", "plugin.json"));
}