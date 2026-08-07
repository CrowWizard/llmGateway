namespace LlmGateway.Desktop.Services;

public sealed class EcommerceImageStudioSkillService(AppPaths paths)
{
    public async Task<string> InstallAsync()
    {
        var sourceSkillFile = Path.Combine(paths.EcommerceImageStudioDirectory, "SKILL.md");
        if (!File.Exists(sourceSkillFile))
        {
            throw new InvalidOperationException($"未找到内置电商生图 Skill：{sourceSkillFile}");
        }

        var targetDirectory = Path.Combine(paths.CodexSkillsDirectory, "ecommerce-image-studio");
        await CodexContentInstaller.InstallAsync(paths.EcommerceImageStudioDirectory, targetDirectory, paths.CodexTemporaryDirectory);
        return targetDirectory;
    }
}