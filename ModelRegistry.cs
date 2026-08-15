using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;

public sealed class ModelRegistry(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ModelRegistry> logger) : BackgroundService
{
    private readonly object _sync = new();
    private readonly List<GatewayEndpoint> _endpoints = LoadEndpoints(configuration).ToList();
    private readonly Dictionary<string, List<GatewayEndpoint>> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _modelsByEndpoint = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _lastSuccessfulEndpoint = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<GatewayEndpoint> Endpoints => _endpoints;

    public IReadOnlyList<GatewayEndpoint> GetCandidates(string? model)
    {
        lock (_sync)
        {
            var candidates = !string.IsNullOrWhiteSpace(model) && _models.TryGetValue(model, out var known)
                ? known
                : _endpoints.Where(endpoint => endpoint.IsValid).ToList();

            if (!string.IsNullOrWhiteSpace(model)
                && _lastSuccessfulEndpoint.TryGetValue(model, out var preferredName))
            {
                return candidates
                    .OrderByDescending(endpoint => string.Equals(endpoint.Name, preferredName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            return candidates.ToArray();
        }
    }

    public void MarkSuccessful(string? model, GatewayEndpoint endpoint)
    {
        if (!string.IsNullOrWhiteSpace(model))
        {
            _lastSuccessfulEndpoint[model] = endpoint.Name;
        }
    }

    public IReadOnlyList<object> GetModels()
    {
        lock (_sync)
        {
            return _models
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => (object)new { id = entry.Key, @object = "model", owned_by = entry.Value[0].Name })
                .ToArray();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        foreach (var endpoint in _endpoints.Where(endpoint => endpoint.IsValid))
        {
            try
            {
                var models = await FetchModelsAsync(endpoint, cancellationToken);
                lock (_sync)
                {
                    _modelsByEndpoint[endpoint.Name] = models;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogWarning(exception, "无法刷新 Endpoint {Endpoint} 的模型列表；将保留上次缓存。", endpoint.Name);
            }
        }

        lock (_sync)
        {
            _models.Clear();
            foreach (var endpoint in _endpoints.Where(endpoint => endpoint.IsValid))
            {
                if (!_modelsByEndpoint.TryGetValue(endpoint.Name, out var models))
                {
                    continue;
                }

                foreach (var model in models)
                {
                    if (!_models.TryGetValue(model, out var providers))
                    {
                        providers = [];
                        _models[model] = providers;
                    }

                    providers.Add(endpoint);
                }
            }
        }
    }

    private async Task<IReadOnlyList<string>> FetchModelsAsync(GatewayEndpoint endpoint, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.BuildApiUri("v1/models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
        using var response = await httpClientFactory.CreateClient("gateway-upstream").SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray()
                .Where(item => item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                .Select(item => item.GetProperty("id").GetString())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
    }

    public static IEnumerable<GatewayEndpoint> LoadEndpoints(IConfiguration configuration)
    {
        var configured = configuration.GetSection("Gateway:Endpoints").Get<List<GatewayEndpoint>>() ?? [];
        if (configured.Count > 0)
        {
            return configured;
        }

        var legacyBaseUrl = configuration["Gateway:UpstreamBaseUrl"];
        return string.IsNullOrWhiteSpace(legacyBaseUrl)
            ? []
            : [new GatewayEndpoint { Name = "default", BaseUrl = legacyBaseUrl, Enabled = false }];
    }
}