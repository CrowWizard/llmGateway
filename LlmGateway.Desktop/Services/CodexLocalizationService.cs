using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LlmGateway.Desktop.Services;

public sealed class CodexLocalizationService(string? applicationDataDirectory = null, string? codexDirectory = null)
{
    private const string ChineseLanguages = "zh-CN,zh,en-US,en";

    private readonly string _applicationDataDirectory = applicationDataDirectory
        ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private readonly string _codexDirectory = codexDirectory
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    public string PreferencesPath => Path.Combine(_applicationDataDirectory, "Codex", "web", "Codex", "Default", "Preferences");
    public string LocalStatePath => Path.Combine(_applicationDataDirectory, "Codex", "web", "Codex", "Local State");
    public string ConfigPath => Path.Combine(_codexDirectory, "config.toml");

    public async Task<LocalizationResult> EnableChineseAsync(CancellationToken cancellationToken = default)
    {
        await WriteJsonAsync(PreferencesPath, root =>
        {
            var intl = GetOrCreateObject(root, "intl");
            intl["selected_languages"] = ChineseLanguages;
            root["accept_languages"] = ChineseLanguages;
        }, cancellationToken);

        await WriteJsonAsync(LocalStatePath, root =>
        {
            var intl = GetOrCreateObject(root, "intl");
            intl["app_locale"] = "zh-CN";
        }, cancellationToken);

        var config = AtomicFile.ReadUtf8(ConfigPath);
        await AtomicFile.WriteUtf8Async(ConfigPath, SetDeveloperInstructions(config), cancellationToken);
        return new LocalizationResult(PreferencesPath, LocalStatePath, ConfigPath, "界面与默认回复语言已设置为简体中文，重启 Codex 后生效。");
    }

    private static async Task WriteJsonAsync(string path, Action<JsonObject> update, CancellationToken cancellationToken)
    {
        var input = AtomicFile.ReadUtf8(path);
        JsonObject root;
        try
        {
            root = string.IsNullOrWhiteSpace(input)
                ? []
                : JsonNode.Parse(input)?.AsObject() ?? throw new InvalidDataException($"{path} 的根节点不是 JSON 对象。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"无法解析 Codex 配置文件：{path}", exception);
        }

        update(root);
        var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        await AtomicFile.WriteUtf8Async(path, output, cancellationToken);
    }

    private static JsonObject GetOrCreateObject(JsonObject root, string name)
    {
        if (root[name] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        root[name] = created;
        return created;
    }

    private static string SetDeveloperInstructions(string input)
    {
        const string replacement = "developer_instructions = \"请始终使用简体中文进行交流和输出。\"";
        var expression = new Regex("^\\s*developer_instructions\\s*=.*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        return expression.IsMatch(input)
            ? expression.Replace(input, replacement)
            : input.TrimEnd() + Environment.NewLine + replacement + Environment.NewLine;
    }
}

public sealed record LocalizationResult(string PreferencesPath, string LocalStatePath, string ConfigPath, string Message);