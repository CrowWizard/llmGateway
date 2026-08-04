using LlmGateway.Desktop.Services;
using Xunit;

namespace LlmGateway.Tests;

public sealed class CodexContentInstallerTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void Install_MovesExistingContentToTimestampedBackup()
    {
        var sourceDirectory = Path.Combine(_temporaryDirectory, "source");
        var targetDirectory = Path.Combine(_temporaryDirectory, ".codex", "skills", "imagegenauto");
        var backupRoot = Path.Combine(_temporaryDirectory, ".codex", "temp");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "new.txt"), "new content");
        File.WriteAllText(Path.Combine(targetDirectory, "old.txt"), "old content");

        var installedDirectory = CodexContentInstaller.Install(sourceDirectory, targetDirectory, backupRoot);

        Assert.Equal(targetDirectory, installedDirectory);
        Assert.Equal("new content", File.ReadAllText(Path.Combine(targetDirectory, "new.txt")));
        Assert.False(File.Exists(Path.Combine(targetDirectory, "old.txt")));
        var backupDirectory = Assert.Single(Directory.EnumerateDirectories(backupRoot, "imagegenauto-*"));
        Assert.Equal("old content", File.ReadAllText(Path.Combine(backupDirectory, "old.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, true);
        }
    }
}