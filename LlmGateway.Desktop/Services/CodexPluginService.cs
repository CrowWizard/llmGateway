namespace LlmGateway.Desktop.Services;

public sealed class CodexPluginService(AppPaths paths)
{
    public string InstallEcommerceImageStudio()
    {
        var sourceDirectory = paths.EcommerceImageStudioDirectory;
        var manifestPath = Path.Combine(sourceDirectory, ".codex-plugin", "plugin.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException("未找到内置电商生图插件。请重新安装应用。");
        }

        var targetDirectory = Path.Combine(paths.CodexPluginsDirectory, "ecommerce-image-studio", "0.1.0");
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var targetFile = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile, true);
        }

        return targetDirectory;
    }
}