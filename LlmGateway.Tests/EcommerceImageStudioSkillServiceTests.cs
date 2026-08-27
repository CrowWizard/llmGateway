using LlmGateway.Desktop.Services;
using Xunit;

namespace LlmGateway.Tests;

public sealed class EcommerceImageStudioSkillServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void AppPaths_UsesProvidedCodexHome()
    {
        var codexHome = Path.Combine(_temporaryDirectory, "codex-home");

        var paths = new AppPaths(
            Path.Combine(_temporaryDirectory, "home"),
            Path.Combine(_temporaryDirectory, "app"),
            codexHome);

        Assert.Equal(codexHome, paths.CodexDirectory);
        Assert.Equal(Path.Combine(codexHome, "skills"), paths.CodexSkillsDirectory);
        Assert.Equal(Path.Combine(codexHome, "config.toml"), paths.CodexConfigPath);
    }

    [Fact]
    public async Task InstallAsync_InstallsAsCodexSkill()
    {
        var paths = new AppPaths(
            Path.Combine(_temporaryDirectory, "home"),
            Path.Combine(_temporaryDirectory, "app"));
        Assert.Equal(Path.Combine(paths.ApplicationDirectory, "ecommerce-image-studio"), paths.EcommerceImageStudioDirectory);
        Directory.CreateDirectory(paths.EcommerceImageStudioDirectory);
        File.WriteAllText(Path.Combine(paths.EcommerceImageStudioDirectory, "SKILL.md"), "ecommerce skill");

        var installedDirectory = await new EcommerceImageStudioSkillService(paths).InstallAsync();

        Assert.Equal(Path.Combine(paths.CodexSkillsDirectory, "ecommerce-image-studio"), installedDirectory);
        Assert.Equal("ecommerce skill", File.ReadAllText(Path.Combine(installedDirectory, "SKILL.md")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, true);
        }
    }
}