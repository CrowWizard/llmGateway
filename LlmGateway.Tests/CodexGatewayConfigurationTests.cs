using LlmGateway.Desktop.Models;
using LlmGateway.Desktop.Services;
using Xunit;

public sealed class CodexGatewayConfigurationTests
{
    [Fact]
    public void TransformUsesOpenAiBaseUrlAndRemovesModelProviders()
    {
        const string existing = """
            model = "old-model"
            model_provider = "old"

            [model_providers.old]
            name = "old"
            base_url = "https://old.example/v1"
            env_key = "OLD_KEY"

            [other]
            keep = true
            """;
        var settings = new CodexSettings
        {
            Model = "gpt-test",
            BaseUrl = "http://127.0.0.1:23008/v1"
        };

        var transformed = CodexConfigService.Transform(existing, settings);

        Assert.Contains("model = \"gpt-test\"", transformed);
        Assert.Contains("openai_base_url = \"http://127.0.0.1:23008/v1\"", transformed);
        Assert.Contains("[other]", transformed);
        Assert.DoesNotContain("model_provider", transformed);
        Assert.DoesNotContain("model_providers.", transformed);
    }

    [Fact]
    public async Task EnsureWritesGatewayKey()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"llm-gateway-test-{Guid.NewGuid():N}");
        var paths = new AppPaths(directory, directory);
        var service = new CodexAuthService(paths);

        try
        {
            var result = await service.EnsureAsync("sk-gateway-test", TestContext.Current.CancellationToken);
            var auth = await File.ReadAllTextAsync(paths.CodexAuthPath, TestContext.Current.CancellationToken);

            Assert.True(result.Created);
            Assert.Contains("\"OPENAI_API_KEY\": \"sk-gateway-test\"", auth);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}