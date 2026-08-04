using LlmGateway.Desktop.Services;
using Xunit;

namespace LlmGateway.Tests;

public sealed class CodexLocalizationServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void EnableChinese_UpdatesSwitchAndCreatesBackup()
    {
        var appAsarPath = CreateAppAsar("header-enable_i18n,!1)-footer");

        var result = new CodexLocalizationService().EnableChinese(appAsarPath);

        Assert.True(result.Changed);
        Assert.Equal(appAsarPath + ".bak", result.BackupPath);
        Assert.Equal("header-enable_i18n,!0)-footer", File.ReadAllText(appAsarPath));
        Assert.Equal("header-enable_i18n,!1)-footer", File.ReadAllText(appAsarPath + ".bak"));
    }

    [Fact]
    public void EnableChinese_DoesNotOverwriteExistingBackupOrModifyEnabledFile()
    {
        var appAsarPath = CreateAppAsar("header-enable_i18n,!0)-footer");
        File.WriteAllText(appAsarPath + ".bak", "original backup");

        var result = new CodexLocalizationService().EnableChinese(appAsarPath);

        Assert.False(result.Changed);
        Assert.Null(result.BackupPath);
        Assert.Equal("original backup", File.ReadAllText(appAsarPath + ".bak"));
    }

    private string CreateAppAsar(string content)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var path = Path.Combine(_temporaryDirectory, "app.asar");
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, true);
        }
    }
}