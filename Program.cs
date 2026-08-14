using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseContentRoot(AppContext.BaseDirectory);
if (args.Contains("--service", StringComparer.OrdinalIgnoreCase))
{
    builder.Host.UseWindowsService();
}

// 读取 Gateway 配置。
var gateway = builder.Configuration.GetSection("Gateway");
var rawUpstream = gateway["UpstreamBaseUrl"];
var upstreamBaseUrl = string.IsNullOrWhiteSpace(rawUpstream)
    ? "https://api.openai.com/"
    : rawUpstream.TrimEnd('/') + "/";
var logTraffic = gateway.GetValue("LogTraffic", true);
var responsesMode = gateway["ResponsesMode"] ?? "Auto";
var geminiImageApiKey = gateway["GeminiImageApiKey"]?.Trim();
var localBindIp = gateway["LocalBindIp"] ?? "127.0.0.1";
var listenPort = gateway.GetValue("ListenPort", 3001);
var listenUrl = $"http://{(localBindIp.Contains(':') ? $"[{localBindIp}]" : localBindIp)}:{listenPort}";
builder.WebHost.UseUrls(listenUrl);
builder.Services.AddHttpClient("responses-compatibility", client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});

// 额外请求头
var extraHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var extraSection = gateway.GetSection("ExtraRequestHeaders");
foreach (var child in extraSection.GetChildren())
{
    if (!string.IsNullOrWhiteSpace(child.Value))
    {
        extraHeaders[child.Key] = child.Value!;
    }
}

var endpointMappings = gateway.GetSection("EndpointMappings")
    .GetChildren()
    .Where(child => !string.IsNullOrWhiteSpace(child.Value))
    .ToDictionary(child => child.Key, child => child.Value!, StringComparer.OrdinalIgnoreCase);

// 用代码动态构建 YARP 路由/集群：上游地址以 Gateway:UpstreamBaseUrl 为准
var routes = new[]
{
    new RouteConfig
    {
        RouteId = "llm-catch-all",
        ClusterId = "upstream",
        Match = new RouteMatch { Path = "{**catch-all}" }
    }
};

var clusters = new[]
{
    new ClusterConfig
    {
        ClusterId = "upstream",
        HttpRequest = new ForwarderRequestConfig
        {
            // LLM 流式响应可能很长
            ActivityTimeout = TimeSpan.FromMinutes(10)
        },
        Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["primary"] = new DestinationConfig { Address = upstreamBaseUrl }
        }
    }
};

builder.Services
    .AddReverseProxy()
    .LoadFromMemory(routes, clusters)
    .AddTransforms(context =>
    {
        context.AddRequestTransform(async transformContext =>
        {
            var headers = transformContext.ProxyRequest.Headers;

            foreach (var (key, value) in extraHeaders)
            {
                if (string.Equals(key, "User-Agent", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                headers.Remove(key);
                headers.TryAddWithoutValidation(key, value);
            }

            // 去掉可能干扰上游的转发头
            headers.Remove("X-Forwarded-Host");

            var mappedPath = EndpointMapper.MapPath(transformContext.HttpContext.Request.Path, endpointMappings);
            if (!string.Equals(mappedPath, transformContext.HttpContext.Request.Path, StringComparison.Ordinal))
            {
                var original = transformContext.ProxyRequest.RequestUri!;
                transformContext.ProxyRequest.RequestUri = new UriBuilder(original) { Path = mappedPath }.Uri;
            }

            if (!string.IsNullOrWhiteSpace(geminiImageApiKey)
                && EndpointMapper.MatchesPath(transformContext.HttpContext.Request.Path, "/v1beta"))
            {
                headers.Remove("Authorization");
                headers.Remove("x-goog-api-key");
                headers.TryAddWithoutValidation("x-goog-api-key", geminiImageApiKey);
            }

            if (logTraffic)
            {
                var request = transformContext.HttpContext.Request;
                var requestId = transformContext.HttpContext.TraceIdentifier;
                var body = transformContext.HttpContext.Items[TrafficLogging.RequestBodyKey] as string ?? "<空>";
                Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{requestId}] [发送上游] {request.Method} {upstreamBaseUrl.TrimEnd('/')}{request.Path}{request.QueryString}");
                Console.WriteLine($"[{requestId}] [发送上游头] {TrafficLogging.FormatHeaders(headers, transformContext.ProxyRequest.Content?.Headers)}");
                Console.WriteLine($"[{requestId}] [发送上游体] {body}");
            }

            await ValueTask.CompletedTask;
        });

        context.AddResponseTransform(transformContext =>
        {
            if (logTraffic)
            {
                var requestId = transformContext.HttpContext.TraceIdentifier;
                var response = transformContext.ProxyResponse;
                Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{requestId}] [接收上游] HTTP {(int?)response?.StatusCode ?? 0}");
                Console.WriteLine($"[{requestId}] [接收上游头] {TrafficLogging.FormatHeaders(response?.Headers, response?.Content.Headers)}");
            }

            return ValueTask.CompletedTask;
        });
    });

var app = builder.Build();

// 健康检查 / 说明页
app.MapGet("/", () => Results.Json(new
{
    name = "LlmGateway",
    status = "ok",
    listen = listenUrl,
    upstream = upstreamBaseUrl,
    usage = new
    {
        tip = $"把客户端 base_url 指到本机监听地址，例如 {listenUrl}/v1",
        config = "修改同目录 appsettings.json 中 Gateway 配置后重启服务"
    }
}));

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapPost("/v1/responses", async (HttpContext context, IHttpClientFactory httpClientFactory) =>
{
    await ResponsesCompatibility.HandleAsync(
        context,
        httpClientFactory,
        upstreamBaseUrl,
        extraHeaders,
        logTraffic,
        responsesMode);
});

app.MapReverseProxy(proxyPipeline =>
{
    if (logTraffic)
    {
        proxyPipeline.Use(TrafficLogging.LogAsync);
    }
});

Console.WriteLine("========================================");
Console.WriteLine("  LlmGateway - 本机 LLM 反向代理");
Console.WriteLine($"  模式: {(args.Contains("--service", StringComparer.OrdinalIgnoreCase) ? "Windows 服务" : "控制台")}");
Console.WriteLine($"  监听: {listenUrl}");
Console.WriteLine($"  上游: {upstreamBaseUrl}");
Console.WriteLine($"  流量日志: {(logTraffic ? "开启" : "关闭")}");
Console.WriteLine("  改配置: 同目录 appsettings.json -> 重启");
Console.WriteLine("========================================");

app.Run();

static class TrafficLogging
{
    public const string RequestBodyKey = "TrafficLogging.RequestBody";

    public static async Task LogAsync(HttpContext context, RequestDelegate next)
    {
        var requestId = context.TraceIdentifier;
        context.Request.EnableBuffering();

        string requestBody;
        using (var reader = new StreamReader(
                   context.Request.Body,
                   System.Text.Encoding.UTF8,
                   detectEncodingFromByteOrderMarks: true,
                   leaveOpen: true))
        {
            requestBody = await reader.ReadToEndAsync(context.RequestAborted);
            context.Request.Body.Position = 0;
        }

        context.Items[RequestBodyKey] = string.IsNullOrEmpty(requestBody) ? "<空>" : requestBody;
        Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{requestId}] [收到客户端] {context.Request.Method} {context.Request.Path}{context.Request.QueryString}");
        Console.WriteLine($"[{requestId}] [收到客户端头] {FormatHeaders(context.Request.Headers)}");
        Console.WriteLine($"[{requestId}] [收到客户端体] {(string)context.Items[RequestBodyKey]!}");

        var originalBody = context.Response.Body;
        await using var loggingBody = new LoggingResponseStream(originalBody, requestId);
        context.Response.Body = loggingBody;

        try
        {
            await next(context);
        }
        finally
        {
            await loggingBody.FlushAsync(context.RequestAborted);
            context.Response.Body = originalBody;
            Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{requestId}] [返回客户端] HTTP {context.Response.StatusCode}");
            Console.WriteLine($"[{requestId}] [返回客户端头] {FormatHeaders(context.Response.Headers)}");
        }
    }

    public static string FormatHeaders(params IEnumerable<KeyValuePair<string, IEnumerable<string>>>?[] headerGroups)
    {
        return string.Join("; ", headerGroups
            .Where(group => group is not null)
            .SelectMany(group => group!)
            .Select(header => $"{header.Key}: {string.Join(", ", header.Value)}"));
    }

    public static string FormatHeaders(IHeaderDictionary headers)
    {
        return string.Join("; ", headers.Select(header => $"{header.Key}: {header.Value}"));
    }

    private sealed class LoggingResponseStream(Stream inner, string requestId) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            Log(buffer.AsSpan(offset, count));
            inner.Write(buffer, offset, count);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Log(buffer.AsSpan(offset, count));
            await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Log(buffer.Span);
            await inner.WriteAsync(buffer, cancellationToken);
        }

        private void Log(ReadOnlySpan<byte> data)
        {
            var text = System.Text.Encoding.UTF8.GetString(data);
            Console.WriteLine($"[{requestId}] [接收上游体] {text}");
            Console.WriteLine($"[{requestId}] [返回客户端体] {text}");
        }
    }
}
