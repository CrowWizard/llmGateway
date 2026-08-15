using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseContentRoot(AppContext.BaseDirectory);
if (args.Contains("--service", StringComparer.OrdinalIgnoreCase))
{
    builder.Host.UseWindowsService();
}

// 读取 Gateway 配置。
var gateway = builder.Configuration.GetSection("Gateway");
var gatewayLogLevel = GatewayLogLevels.Parse(gateway["LogLevel"]);
builder.Logging.SetMinimumLevel(gatewayLogLevel.ToMicrosoftLogLevel());
builder.Logging.AddFilter((_, level) => level >= gatewayLogLevel.ToMicrosoftLogLevel());
var logTraffic = gateway.GetValue("LogTraffic", true) && gatewayLogLevel == GatewayLogLevel.Debug;
GatewayTrafficFileLog.Configure(logTraffic, gateway["TrafficLogDirectory"]);
UnsupportedResponsesRequestLog.Configure(gateway["TrafficLogDirectory"]);
var responsesMode = gateway["ResponsesMode"] ?? "Auto";
var gatewayApiKey = gateway["ApiKey"]?.Trim();
if (string.IsNullOrWhiteSpace(gatewayApiKey))
{
    gatewayApiKey = GatewayEndpoint.CreateGatewayApiKey();
    PersistGatewayApiKey(gatewayApiKey);
}
var localBindIp = gateway["LocalBindIp"] ?? "127.0.0.1";
var listenPort = gateway.GetValue("ListenPort", 3001);
var configuredListenPort = listenPort;
while (!IsTcpPortAvailable(localBindIp, listenPort))
{
    if (listenPort >= 65535)
    {
        throw new InvalidOperationException("找不到可用的本地网关端口。");
    }

    listenPort++;
}

if (listenPort != configuredListenPort)
{
    PersistListenPort(listenPort);
}

var listenUrl = $"http://{(localBindIp.Contains(':') ? $"[{localBindIp}]" : localBindIp)}:{listenPort}";
builder.WebHost.UseUrls(listenUrl);
builder.Services.AddHttpClient("gateway-upstream", client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddSingleton<ModelRegistry>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ModelRegistry>());
builder.Services.AddSingleton<EndpointForwarder>();

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


var app = builder.Build();
app.UseWebSockets();

// 健康检查 / 说明页
app.MapGet("/", () => Results.Json(new
{
    name = "LlmGateway",
    status = "ok",
    listen = listenUrl,
    endpoints = app.Services.GetRequiredService<ModelRegistry>().Endpoints.Select(endpoint => endpoint.Name),
    usage = new
    {
        tip = $"把客户端 base_url 指到本机监听地址，例如 {listenUrl}/v1",
        config = "修改同目录 appsettings.json 中 Gateway 配置后重启服务"
    }
}));

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/v1")
        && !HasGatewayApiKey(context.Request.Headers.Authorization, gatewayApiKey))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = new { message = "Gateway API Key 无效。", type = "authentication_error" } });
        return;
    }

    await next(context);
});

if (logTraffic)
{
    app.Use(TrafficLogging.LogAsync);
}

app.MapGet("/v1/models", (ModelRegistry registry) => Results.Json(new { @object = "list", data = registry.GetModels() }));
app.MapPost("/v1/responses", async (HttpContext context, IHttpClientFactory httpClientFactory, ModelRegistry registry) =>
{
    using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
    var model = document.RootElement.TryGetProperty("model", out var modelNode) ? modelNode.GetString() : null;
    context.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(document.RootElement));
    var endpoint = registry.GetCandidates(model).FirstOrDefault();
    if (endpoint is null)
    {
        return Results.Json(new { error = new { message = "没有可用的上游 Endpoint。", type = "no_upstream" } }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    await ResponsesCompatibility.HandleAsync(context, httpClientFactory, endpoint, extraHeaders, logTraffic, responsesMode);
    if (context.Response.StatusCode is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices)
    {
        registry.MarkSuccessful(model, endpoint);
    }
    return Results.Empty;
});
app.MapGet("/v1/responses", async (HttpContext context, IHttpClientFactory httpClientFactory, ModelRegistry registry) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        await ResponsesWebSocketCompatibility.HandleAsync(context, httpClientFactory, registry, extraHeaders, logTraffic, responsesMode);
        return;
    }

    const string responseBody = "{\"error\":{\"message\":\"仅支持 POST /v1/responses。\",\"type\":\"invalid_request_error\"}}";
    await UnsupportedResponsesRequestLog.WriteAsync(context, StatusCodes.Status405MethodNotAllowed, responseBody);
    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
    context.Response.ContentType = "application/json; charset=utf-8";
    await context.Response.WriteAsync(responseBody, context.RequestAborted);
});
app.MapMethods("/v1/responses", ["PUT", "PATCH", "DELETE"], async context =>
{
    const string responseBody = "{\"error\":{\"message\":\"仅支持 POST /v1/responses。\",\"type\":\"invalid_request_error\"}}";
    await UnsupportedResponsesRequestLog.WriteAsync(context, StatusCodes.Status405MethodNotAllowed, responseBody);
    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
    context.Response.ContentType = "application/json; charset=utf-8";
    await context.Response.WriteAsync(responseBody, context.RequestAborted);
});
app.MapMethods("/v1/{**path}", ["GET", "POST", "PUT", "PATCH", "DELETE"], async (HttpContext context, EndpointForwarder forwarder) =>
{
    await forwarder.ForwardAsync(context, extraHeaders, endpointMappings, logTraffic, context.RequestAborted);
});

Console.WriteLine("========================================");
Console.WriteLine("  LlmGateway - 本机 LLM 反向代理");
Console.WriteLine($"  模式: {(args.Contains("--service", StringComparer.OrdinalIgnoreCase) ? "Windows 服务" : "控制台")}");
Console.WriteLine($"  监听: {listenUrl}");
Console.WriteLine($"  Endpoint: {string.Join(", ", app.Services.GetRequiredService<ModelRegistry>().Endpoints.Select(endpoint => endpoint.Name))}");
Console.WriteLine($"  日志级别: {gatewayLogLevel}");
Console.WriteLine($"  流量详情: {(logTraffic ? "开启" : "仅 Debug 级别记录")}");
Console.WriteLine("  改配置: 同目录 appsettings.json -> 重启");
Console.WriteLine("========================================");

app.Run();

static bool IsTcpPortAvailable(string bindIp, int port)
{
    var address = IPAddress.TryParse(bindIp, out var parsedAddress)
        ? parsedAddress
        : IPAddress.Any;
    try
    {
        using var listener = new TcpListener(address, port);
        listener.Start();
        return true;
    }
    catch (SocketException)
    {
        return false;
    }
}

static void PersistListenPort(int port)
{
    var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    if (!File.Exists(path))
    {
        return;
    }

    var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
    if (root?["Gateway"] is not JsonObject gateway)
    {
        return;
    }

    gateway["ListenPort"] = port;
    File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
}

static void PersistGatewayApiKey(string apiKey)
{
    var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    if (!File.Exists(path))
    {
        return;
    }

    var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
    if (root?["Gateway"] is not JsonObject gateway)
    {
        return;
    }

    gateway["ApiKey"] = apiKey;
    File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
}

static bool HasGatewayApiKey(string? authorization, string expectedKey) =>
    AuthenticationHeaderValue.TryParse(authorization, out var value)
    && string.Equals(value.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
    && string.Equals(value.Parameter, expectedKey, StringComparison.Ordinal);

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
            .Select(header => $"{header.Key}: {FormatHeaderValue(header.Key, header.Value)}"));
    }

    public static string FormatHeaders(IHeaderDictionary headers)
    {
        return string.Join("; ", headers.Select(header => $"{header.Key}: {FormatHeaderValue(header.Key, header.Value)}"));
    }

    private static string FormatHeaderValue(string name, IEnumerable<string> values) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
        || name.Equals("x-goog-api-key", StringComparison.OrdinalIgnoreCase)
        || name.Equals("x-api-key", StringComparison.OrdinalIgnoreCase)
            ? MaskSecret(string.Join(", ", values))
            : string.Join(", ", values);

    private static string MaskSecret(string value)
    {
        const int visibleLength = 6;
        const string bearerPrefix = "Bearer ";
        if (value.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return bearerPrefix + MaskSecret(value[bearerPrefix.Length..]);
        }

        return value.Length <= visibleLength * 2
            ? "[redacted]"
            : $"{value[..visibleLength]}...{value[^visibleLength..]}";
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

static class GatewayTrafficFileLog
{
    public static void Configure(bool enabled, string? configuredDirectory)
    {
        if (!enabled)
        {
            return;
        }

        var directory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex",
                "logs")
            : configuredDirectory;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "gateway-traffic.log");
        Console.SetOut(new TeeTextWriter(Console.Out, path));
    }
}

static class UnsupportedResponsesRequestLog
{
    private static readonly object Sync = new();
    private static string? _path;

    public static void Configure(string? configuredDirectory)
    {
        var directory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex",
                "logs")
            : configuredDirectory;
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "unsupported-responses-requests.log");
    }

    public static async Task WriteAsync(HttpContext context, int statusCode, string responseBody)
    {
        if (string.IsNullOrWhiteSpace(_path))
        {
            return;
        }

        context.Request.EnableBuffering();
        string requestBody;
        using (var reader = new StreamReader(
                   context.Request.Body,
                   Encoding.UTF8,
                   detectEncodingFromByteOrderMarks: true,
                   leaveOpen: true))
        {
            requestBody = await reader.ReadToEndAsync(context.RequestAborted);
            context.Request.Body.Position = 0;
        }

        var requestUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
        var entry = new StringBuilder()
            .AppendLine("========================================")
            .AppendLine($"时间: {DateTimeOffset.Now:O}")
            .AppendLine($"请求 ID: {context.TraceIdentifier}")
            .AppendLine($"请求方法: {context.Request.Method}")
            .AppendLine($"请求 URL: {requestUrl}")
            .AppendLine($"请求协议: {context.Request.Protocol}")
            .AppendLine($"请求头: {TrafficLogging.FormatHeaders(context.Request.Headers)}")
            .AppendLine($"请求体: {(string.IsNullOrEmpty(requestBody) ? "<空>" : requestBody)}")
            .AppendLine($"返回状态: HTTP {statusCode}")
            .AppendLine("返回头: Content-Type: application/json; charset=utf-8")
            .AppendLine($"返回体: {responseBody}");

        lock (Sync)
        {
            File.AppendAllText(_path, entry.ToString(), Encoding.UTF8);
        }
    }
}

sealed class TeeTextWriter(TextWriter console, string path) : TextWriter
{
    private readonly object _sync = new();

    public override Encoding Encoding => console.Encoding;

    public override void Write(char value)
    {
        lock (_sync)
        {
            console.Write(value);
            File.AppendAllText(path, value.ToString(), Encoding.UTF8);
        }
    }

    public override void Write(string? value)
    {
        lock (_sync)
        {
            console.Write(value);
            File.AppendAllText(path, value, Encoding.UTF8);
        }
    }

    public override void WriteLine(string? value)
    {
        lock (_sync)
        {
            console.WriteLine(value);
            File.AppendAllText(path, (value ?? string.Empty) + Environment.NewLine, Encoding.UTF8);
        }
    }
}
