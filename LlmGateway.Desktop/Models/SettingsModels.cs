namespace LlmGateway.Desktop.Models;

public sealed class GatewaySettings
{
    public string LocalBindIp { get; set; } = "127.0.0.1";
    public int ListenPort { get; set; } = 23001;
    public string UpstreamBaseUrl { get; set; } = "https://image.lingjue.chat/";
    public bool CompatibilityMode { get; set; }
    public string DirectCodexBaseUrl { get; set; } = "https://image.lingjue.chat/v1";
    public Dictionary<string, string> ExtraRequestHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8"
    };
    public bool LogTraffic { get; set; }

    public GatewaySettings Clone() => new()
    {
        LocalBindIp = LocalBindIp,
        ListenPort = ListenPort,
        UpstreamBaseUrl = UpstreamBaseUrl,
        CompatibilityMode = CompatibilityMode,
        DirectCodexBaseUrl = DirectCodexBaseUrl,
        ExtraRequestHeaders = new Dictionary<string, string>(ExtraRequestHeaders, StringComparer.OrdinalIgnoreCase),
        LogTraffic = LogTraffic
    };
}

public sealed class CodexSettings
{
    public string Model { get; set; } = "kaka-5.5";
    public string Provider { get; set; } = "dzdy";
    public string BaseUrl { get; set; } = "https://image.lingjue.chat/v1";
    public string EnvironmentKey { get; set; } = "DZDY_API_KEY";
}

public sealed record BackupItem(
    string Id,
    string DisplayName,
    string DirectoryPath,
    bool HasConfig,
    bool HasAuth)
{
    public string Files => HasConfig && HasAuth ? "config.toml + auth.json" : HasConfig ? "config.toml" : "auth.json";
    public string Label => $"{DisplayName}  ·  {Files}";
}

public sealed record AuthResult(bool Created, bool Repaired, bool PlaceholderCreated, string? BackupPath);

public sealed record ApplicationDetection(
    bool Found,
    string Name,
    string Description,
    string? Command,
    string? AppUserModelId = null);
