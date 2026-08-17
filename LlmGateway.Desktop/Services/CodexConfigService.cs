using System.Text;
using System.Text.RegularExpressions;
using LlmGateway.Desktop.Models;

namespace LlmGateway.Desktop.Services;

public sealed partial class CodexConfigService(AppPaths paths)
{
    public string ConfigPath => paths.CodexConfigPath;

    public CodexSettings Load()
    {
        var result = new CodexSettings();
        var lines = ReadLines();
        var inSection = false;

        foreach (var line in lines)
        {
            if (TryGetSection(line, out _))
            {
                inSection = true;
                continue;
            }

            if (!inSection && IsKey(line, "model") && TryReadQuotedValue(line, out var model))
            {
                result.Model = model;
            }
            else if (!inSection && IsKey(line, "openai_base_url") && TryReadQuotedValue(line, out var baseUrl))
            {
                result.BaseUrl = baseUrl;
            }
        }

        return result;
    }

    public async Task SaveAsync(CodexSettings settings, CancellationToken cancellationToken = default)
    {
        Validate(settings);
        var transformed = Transform(AtomicFile.ReadUtf8(ConfigPath), settings);
        await AtomicFile.WriteUtf8Async(ConfigPath, transformed, cancellationToken);
    }

    internal static string Transform(string input, CodexSettings settings)
    {
        var kept = new List<string>();
        var inSection = false;
        var skipSection = false;

        foreach (var line in SplitLines(input))
        {
            if (TryGetSection(line, out var section))
            {
                inSection = true;
                skipSection = section.StartsWith("model_providers.", StringComparison.Ordinal);
                if (skipSection)
                {
                    continue;
                }
            }

            if (skipSection || (!inSection && (IsKey(line, "model") || IsKey(line, "model_provider") || IsKey(line, "openai_base_url"))))
            {
                continue;
            }

            kept.Add(line);
        }

        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[0]))
        {
            kept.RemoveAt(0);
        }
        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1]))
        {
            kept.RemoveAt(kept.Count - 1);
        }

        var output = new StringBuilder();
        output.AppendLine($"model = \"{Escape(settings.Model)}\"");
        output.AppendLine("model_provider = \"openai\"");
        output.AppendLine($"openai_base_url = \"{Escape(settings.BaseUrl)}\"");
        if (kept.Count > 0)
        {
            output.AppendLine();
            foreach (var line in kept)
            {
                output.AppendLine(line);
            }
        }

        return output.ToString();
    }

    private string[] ReadLines() => SplitLines(AtomicFile.ReadUtf8(ConfigPath));

    private static string[] SplitLines(string input) => input
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n')
        .Split('\n');

    private static bool TryGetSection(string line, out string section)
    {
        var match = SectionRegex().Match(line);
        section = match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        return match.Success;
    }

    private static bool IsKey(string line, string key) =>
        Regex.IsMatch(line, $"^\\s*{Regex.Escape(key)}\\s*=", RegexOptions.CultureInvariant);

    private static bool TryReadQuotedValue(string line, out string value)
    {
        var match = QuotedValueRegex().Match(line);
        if (!match.Success)
        {
            value = string.Empty;
            return false;
        }

        value = Regex.Unescape(match.Groups[1].Value);
        return true;
    }

    private static void Validate(CodexSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Model))
        {
            throw new InvalidOperationException("模型不能为空。");
        }
        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException("Base URL 必须是有效的 HTTP 或 HTTPS 地址。");
        }
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    [GeneratedRegex(@"^\s*\[\[?([^\]]+)\]\]?\s*(?:#.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SectionRegex();

    [GeneratedRegex("^\\s*[A-Za-z0-9_.-]+\\s*=\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedValueRegex();

}
