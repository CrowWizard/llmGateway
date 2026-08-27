using LlmGateway.Desktop.Services;
using System.Text.Json.Nodes;
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
        var preferences = JsonNode.Parse(await File.ReadAllTextAsync(preferencesPath, cancellationToken))!.AsObject();
        Assert.Equal("zh-CN,zh,en-US,en", preferences["intl"]!["selected_languages"]!.GetValue<string>());
        Assert.Equal("zh-CN,zh,en-US,en", preferences["accept_languages"]!.GetValue<string>());
        var localState = JsonNode.Parse(await File.ReadAllTextAsync(localStatePath, cancellationToken))!.AsObject();
        Assert.Equal("zh-CN", localState["intl"]!["app_locale"]!.GetValue<string>());
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