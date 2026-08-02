namespace LlmGateway.Desktop.Services;

public sealed class CodexSkillService(AppPaths paths)
{
    public string InstallImagePromptGuide()
    {
        var sourceDirectory = paths.ImagePromptGuideDirectory;
        var sourceSkillFile = Path.Combine(sourceDirectory, "SKILL.md");
        if (!File.Exists(sourceSkillFile))
        {
            throw new InvalidOperationException("未找到内置电商生图 Skill。请重新安装应用。");
        }

        var targetDirectory = Path.Combine(paths.CodexSkillsDirectory, "image-prompt-guide");
        var installedFileCount = 0;
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var targetFile = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile, true);
            installedFileCount++;
        }

        return targetDirectory;
    }
}