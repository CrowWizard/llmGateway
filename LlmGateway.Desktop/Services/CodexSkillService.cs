namespace LlmGateway.Desktop.Services;

public sealed class CodexSkillService(AppPaths paths)
{
    public Task<string> InstallImageGenAutoAsync()
    {
        var sourceDirectory = paths.ImageGenAutoDirectory;
        var sourceSkillFile = Path.Combine(sourceDirectory, "SKILL.md");
        if (!File.Exists(sourceSkillFile))
        {
            throw new InvalidOperationException("未找到内置兼容版生图 Skill。请重新安装应用。");
        }

        var systemImageGenDirectory = Path.Combine(paths.CodexSkillsDirectory, ".system", "imagegen");
        CodexContentInstaller.MoveToTemporary(systemImageGenDirectory, paths.CodexTemporaryDirectory);

        var targetDirectory = Path.Combine(paths.CodexSkillsDirectory, "imagegenauto");
        return CodexContentInstaller.InstallAsync(sourceDirectory, targetDirectory, paths.CodexTemporaryDirectory);
    }
}