namespace LlmGateway.Desktop.Services;

public sealed class CodexSkillService(AppPaths paths)
{
    public string InstallImageGenAuto()
    {
        var sourceDirectory = paths.ImageGenAutoDirectory;
        var sourceSkillFile = Path.Combine(sourceDirectory, "SKILL.md");
        if (!File.Exists(sourceSkillFile))
        {
            throw new InvalidOperationException("未找到内置兼容版生图 Skill。请重新安装应用。");
        }

        var targetDirectory = Path.Combine(paths.CodexSkillsDirectory, "imagegenauto");
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