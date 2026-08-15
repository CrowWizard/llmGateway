using LlmGateway.Desktop.Models;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;

namespace LlmGateway.Desktop.Services;

public sealed class GatewayHostService
{
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private WebApplication? _application;

    public bool IsRunning => _application is not null;
    public event Action<string>? LogReceived;

    public async Task StartAsync(GatewaySettings settings, CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_application is not null)
            {
                return;
            }

            Validate(settings);
            var effective = settings.Clone();
            var upstreamBaseUrl = effective.UpstreamBaseUrl.TrimEnd('/') + "/";
            var listenUrl = $"http://{(effective.LocalBindIp.Contains(':') ? $"[{effective.LocalBindIp}]" : effective.LocalBindIp)}:{effective.ListenPort}";
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls(listenUrl);
            builder.Logging.ClearProviders();
            builder.Services.AddHttpClient("responses-compatibility", client => client.Timeout = TimeSpan.FromMinutes(10));

            var routes = new[]
            {
                new RouteConfig { RouteId = "llm-catch-all", ClusterId = "upstream", Match = new RouteMatch { Path = "{**catch-all}" } }
            };
            var clusters = new[]
            {
                new ClusterConfig
                {
                    ClusterId = "upstream",
                    HttpRequest = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromMinutes(10) },
                    Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["primary"] = new DestinationConfig { Address = upstreamBaseUrl }
                    }
                }
            };

            builder.Services.AddReverseProxy().LoadFromMemory(routes, clusters).AddTransforms(context =>
            {
                context.AddRequestTransform(transformContext =>
                {
                    ApplyHeaders(transformContext.ProxyRequest, effective);
                    transformContext.ProxyRequest.Headers.Remove("X-Forwarded-Host");
                    if (effective.LogTraffic)
                    {
                        Log($"发送上游 {transformContext.ProxyRequest.Method} {transformContext.ProxyRequest.RequestUri}");
                        Log($"上游请求头 {GatewayTrafficLogging.FormatHttpHeaders(transformContext.ProxyRequest.Headers, transformContext.ProxyRequest.Content?.Headers)}");
                    }
                    return ValueTask.CompletedTask;
                });
                context.AddResponseTransform(transformContext =>
                {
                    if (effective.LogTraffic)
                    {
                        Log($"接收上游 HTTP {(int?)transformContext.ProxyResponse?.StatusCode ?? 0}");
                    }
                    return ValueTask.CompletedTask;
                });
            });

            var app = builder.Build();
            app.MapGet("/", () => Results.Json(new
            {
                name = "LlmGateway",
                status = "ok",
                listen = listenUrl,
                upstream = upstreamBaseUrl,
                responsesCompatibility = true
            }));
            app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
            app.MapPost("/v1/responses", async (HttpContext context, IHttpClientFactory httpClientFactory) =>
            {
                await ResponsesCompatibility.HandleAsync(
                    context,
                    httpClientFactory,
                    new GatewayEndpoint
                    {
                        Name = "default",
                        BaseUrl = upstreamBaseUrl,
                        ApiKey = effective.Endpoints.FirstOrDefault(endpoint => endpoint.Enabled)?.ApiKey ?? string.Empty
                    },
                    effective.ExtraRequestHeaders,
                    effective.LogTraffic);
            });
            app.MapReverseProxy(proxyPipeline =>
            {
                if (effective.LogTraffic)
                {
                    proxyPipeline.Use((context, next) => GatewayTrafficLogging.LogAsync(context, next, Log));
                }
            });

            try
            {
                await app.StartAsync(cancellationToken);
                _application = app;
                Log($"网关已启动：{listenUrl} → {upstreamBaseUrl}");
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_application is null)
            {
                return;
            }

            var application = _application;
            _application = null;
            await application.StopAsync(cancellationToken);
            await application.DisposeAsync();
            Log("网关已停止。");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private static void Validate(GatewaySettings settings)
    {
        if (!Uri.TryCreate(settings.UpstreamBaseUrl, UriKind.Absolute, out var upstream) || upstream.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException("上游地址必须是有效的 HTTP 或 HTTPS 地址。");
        }
        if (settings.ListenPort is < 1 or > 65535)
        {
            throw new InvalidOperationException("监听端口必须在 1 到 65535 之间。");
        }
        if (string.IsNullOrWhiteSpace(settings.LocalBindIp))
        {
            throw new InvalidOperationException("监听 IP 不能为空。");
        }
    }

    private static void ApplyHeaders(HttpRequestMessage request, GatewaySettings settings)
    {
        foreach (var (name, value) in settings.ExtraRequestHeaders)
        {
            if (name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private void Log(string message) => LogReceived?.Invoke($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");
}
