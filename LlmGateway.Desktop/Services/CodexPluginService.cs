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

        var targetDirectory = Path.Combine(paths.CodexPluginsDirectory, "ecommerce-image-studio", Path.GetFileName(sourceDirectory));
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var targetFile = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile, true);
        }

        return targetDirectory;
    }

    private string? ResolveEcommerceImageStudioDirectory()
    {
        if (HasManifest(paths.EcommerceImageStudioDirectory))
        {
            return paths.EcommerceImageStudioDirectory;
        }

        var pluginRoot = Path.Combine(paths.ApplicationDirectory, "ecommerce-image-studio");
        if (!Directory.Exists(pluginRoot))
        {
            return null;
        }

        return Directory.EnumerateDirectories(pluginRoot)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .FirstOrDefault(HasManifest);
    }

    private static bool HasManifest(string directory) =>
        File.Exists(Path.Combine(directory, ".codex-plugin", "plugin.json"));
}