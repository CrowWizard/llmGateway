using LlmGateway.Desktop.Services;
using Xunit;

namespace LlmGateway.Tests;

public sealed class CodexLocalizationServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task EnableChineseAsync_UpdatesUserLanguagePreferencesAndInstructions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var applicationData = Path.Combine(_temporaryDirectory, "Roaming");
        var codexDirectory = Path.Combine(_temporaryDirectory, ".codex");
        var preferencesPath = Path.Combine(applicationData, "Codex", "web", "Codex", "Default", "Preferences");
        var localStatePath = Path.Combine(applicationData, "Codex", "web", "Codex", "Local State");
        Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
        await File.WriteAllTextAsync(preferencesPath, "{\"existing\":true,\"intl\":{\"other\":\"value\"}}", cancellationToken);
        Directory.CreateDirectory(codexDirectory);
        await File.WriteAllTextAsync(Path.Combine(codexDirectory, "config.toml"), "model = \"test\"\ndeveloper_instructions = \"English\"\n", cancellationToken);

        var result = await new CodexLocalizationService(applicationData, codexDirectory).EnableChineseAsync(cancellationToken);

        Assert.Equal(preferencesPath, result.PreferencesPath);
        Assert.Equal(localStatePath, result.LocalStatePath);
        Assert.Contains("\"selected_languages\": \"zh-CN,zh,en-US,en\"", await File.ReadAllTextAsync(preferencesPath, cancellationToken));
        Assert.Contains("\"accept_languages\": \"zh-CN,zh,en-US,en\"", await File.ReadAllTextAsync(preferencesPath, cancellationToken));
        Assert.Contains("\"app_locale\": \"zh-CN\"", await File.ReadAllTextAsync(localStatePath, cancellationToken));
        var config = await File.ReadAllTextAsync(Path.Combine(codexDirectory, "config.toml"), cancellationToken);
        Assert.Contains("developer_instructions = \"请始终使用简体中文进行交流和输出。\"", config);
        Assert.DoesNotContain("developer_instructions = \"English\"", config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, true);
        }
    }
}