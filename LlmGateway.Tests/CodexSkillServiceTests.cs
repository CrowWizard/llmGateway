using LlmGateway.Desktop.Services;
using Xunit;

namespace LlmGateway.Tests;

public sealed class CodexSkillServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InstallImageGenAutoAsync_MovesSystemImageGenToTemporaryDirectory()
    {
        var userHome = Path.Combine(_temporaryDirectory, "home");
        var applicationDirectory = Path.Combine(_temporaryDirectory, "app");
        var paths = new AppPaths(userHome, applicationDirectory);
        var systemImageGenDirectory = Path.Combine(paths.CodexSkillsDirectory, ".system", "imagegen");
        Directory.CreateDirectory(paths.ImageGenAutoDirectory);
        Directory.CreateDirectory(systemImageGenDirectory);
        File.WriteAllText(Path.Combine(paths.ImageGenAutoDirectory, "SKILL.md"), "compatible skill");
        File.WriteAllText(Path.Combine(systemImageGenDirectory, "SKILL.md"), "system skill");

        var installedDirectory = await new CodexSkillService(paths).InstallImageGenAutoAsync();

        Assert.Equal(Path.Combine(paths.CodexSkillsDirectory, "imagegenauto"), installedDirectory);
        Assert.False(Directory.Exists(systemImageGenDirectory));
        Assert.Equal("compatible skill", File.ReadAllText(Path.Combine(installedDirectory, "SKILL.md")));
        var movedDirectory = Assert.Single(Directory.EnumerateDirectories(paths.CodexTemporaryDirectory, "imagegen-*"));
        Assert.Equal("system skill", File.ReadAllText(Path.Combine(movedDirectory, "SKILL.md")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, true);
        }
    }
}