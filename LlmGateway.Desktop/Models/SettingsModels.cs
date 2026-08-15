using System.Text.Json.Serialization;
using LlmGateway.Desktop.Infrastructure;

namespace LlmGateway.Desktop.Models;

public sealed class ModelGroupSettings : ObservableObject
{
    private string _name = string.Empty;
    private string _baseUrl = "https://api.ailili.chat/v1";
    private string _apiKey = string.Empty;
    private string _model = string.Empty;
    private bool _isPrimary;

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string BaseUrl { get => _baseUrl; set => SetProperty(ref _baseUrl, value); }
    public string ApiKey { get => _apiKey; set => SetProperty(ref _apiKey, value); }
    public string Model { get => _model; set => SetProperty(ref _model, value); }
    public bool IsPrimary { get => _isPrimary; set => SetProperty(ref _isPrimary, value); }

    public ModelGroupSettings Clone() => new()
    {
        Name = Name,
        BaseUrl = BaseUrl,
        ApiKey = ApiKey,
        Model = Model,
        IsPrimary = IsPrimary
    };
}

public sealed class GatewayEndpointSettings
{
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public sealed class GatewaySettings
{
    public string LocalBindIp { get; set; } = "127.0.0.1";
    public int ListenPort { get; set; } = 23001;
    [JsonPropertyName("ApiKey")]
    public string GatewayApiKey { get; set; } = string.Empty;
    public string UpstreamBaseUrl { get; set; } = "https://api.ailili.chat";
    public bool CompatibilityMode { get; set; }
    public string ResponsesMode { get; set; } = "Auto";
    public string GeminiImageApiKey { get; set; } = string.Empty;
    public string DirectCodexBaseUrl { get; set; } = "https://api.ailili.chat/v1";
    public Dictionary<string, string> ExtraRequestHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8"
    };
    public Dictionary<string, string> EndpointMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/v1/images/generations"] = "/v1/images/generations",
        ["/v1/images/edits"] = "/v1/images/edits"
    };
    public List<ModelGroupSettings> TextModelGroups { get; set; } = [];
    public List<ModelGroupSettings> ImageModelGroups { get; set; } = [];
    public List<GatewayEndpointSettings> Endpoints { get; set; } = [];
    public bool LogTraffic { get; set; }

    public GatewaySettings Clone() => new()
    {
        LocalBindIp = LocalBindIp,
        ListenPort = ListenPort,
        GatewayApiKey = GatewayApiKey,
        UpstreamBaseUrl = UpstreamBaseUrl,
        CompatibilityMode = CompatibilityMode,
        ResponsesMode = ResponsesMode,
        GeminiImageApiKey = GeminiImageApiKey,
        DirectCodexBaseUrl = DirectCodexBaseUrl,
        ExtraRequestHeaders = new Dictionary<string, string>(ExtraRequestHeaders, StringComparer.OrdinalIgnoreCase),
        EndpointMappings = new Dictionary<string, string>(EndpointMappings, StringComparer.OrdinalIgnoreCase),
        TextModelGroups = TextModelGroups.Select(group => group.Clone()).ToList(),
        ImageModelGroups = ImageModelGroups.Select(group => group.Clone()).ToList(),
        Endpoints = Endpoints.Select(endpoint => new GatewayEndpointSettings
        {
            Name = endpoint.Name,
            BaseUrl = endpoint.BaseUrl,
            ApiKey = endpoint.ApiKey,
            Enabled = endpoint.Enabled
        }).ToList(),
        LogTraffic = LogTraffic
    };
}

public sealed class CodexSettings
{
    public string Model { get; set; } = string.Empty;
    public string Provider { get; set; } = "dzdy";
    public string BaseUrl { get; set; } = "https://api.ailili.chat/v1";
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
