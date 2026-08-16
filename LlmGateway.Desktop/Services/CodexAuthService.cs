using System.Text.Json;
using System.Text.Json.Nodes;
using LlmGateway.Desktop.Models;

namespace LlmGateway.Desktop.Services;

public sealed class CodexAuthService(AppPaths paths)
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public string AuthPath => paths.CodexAuthPath;

    public async Task<AuthResult> EnsureAsync(string gatewayApiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gatewayApiKey))
        {
            throw new InvalidOperationException("Gateway API Key 不能为空。");
        }

        var created = !File.Exists(AuthPath);
        var repaired = false;
        var placeholderCreated = false;
        string? backupPath = null;
        JsonObject auth;

        if (created)
        {
            auth = new JsonObject();
        }
        else
        {
            try
            {
                auth = JsonNode.Parse(await File.ReadAllTextAsync(AuthPath, cancellationToken)) as JsonObject
                    ?? throw new JsonException("根节点必须是对象。");
            }
            catch (JsonException)
            {
                backupPath = AtomicFile.BackupIfExists(AuthPath);
                repaired = true;
                auth = new JsonObject();
            }
        }

        var changed = created || repaired;
        if (auth["auth_mode"] is not JsonValue authMode || !authMode.TryGetValue<string>(out _))
        {
            auth["auth_mode"] = "apikey";
            changed = true;
        }

        if (auth["OPENAI_API_KEY"] is not JsonValue apiKey
            || !apiKey.TryGetValue<string>(out var value)
            || value != gatewayApiKey)
        {
            auth["OPENAI_API_KEY"] = gatewayApiKey;
            changed = true;
        }

        if (changed)
        {
            if (!created && !repaired && string.IsNullOrEmpty(backupPath))
            {
                backupPath = AtomicFile.BackupIfExists(AuthPath);
            }
            await AtomicFile.WriteUtf8Async(AuthPath, auth.ToJsonString(WriteOptions) + Environment.NewLine, cancellationToken);
        }

        return new AuthResult(created, repaired, placeholderCreated, backupPath);
    }

}
