using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmGateway.Desktop.Models;

namespace LlmGateway.Desktop.Services;

public sealed class CodexAuthService(AppPaths paths)
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public string AuthPath => paths.CodexAuthPath;

    public async Task<AuthResult> EnsureAsync(CancellationToken cancellationToken = default)
    {
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
            || string.IsNullOrWhiteSpace(value))
        {
            auth["OPENAI_API_KEY"] = CreatePlaceholder();
            placeholderCreated = true;
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

    private static string CreatePlaceholder()
    {
        Span<byte> randomBytes = stackalloc byte[48];
        RandomNumberGenerator.Fill(randomBytes);
        return "sk-" + string.Create(48, randomBytes.ToArray(), static (characters, bytes) =>
        {
            for (var index = 0; index < characters.Length; index++)
            {
                characters[index] = Alphabet[bytes[index] % Alphabet.Length];
            }
        });
    }
}
