using System.Net.Http.Headers;

namespace LlmGateway.Desktop.Services;

internal static class GatewayTrafficLogging
{
    public const string RequestBodyKey = "GatewayTrafficLogging.RequestBody";
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie", "X-Api-Key", "Api-Key"
    };

    public static async Task LogAsync(HttpContext context, RequestDelegate next, Action<string> log)
    {
        context.Request.EnableBuffering();
        using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
        {
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            context.Request.Body.Position = 0;
            context.Items[RequestBodyKey] = string.IsNullOrEmpty(body) ? "<空>" : body;
        }

        log($"收到客户端 {context.Request.Method} {context.Request.Path}{context.Request.QueryString}");
        log($"客户端头 {FormatHeaders(context.Request.Headers)}");
        await next(context);
        log($"返回客户端 HTTP {context.Response.StatusCode}");
    }

    public static string FormatHeaders(params IEnumerable<KeyValuePair<string, IEnumerable<string>>>?[] groups) => string.Join("; ", groups
        .Where(group => group is not null)
        .SelectMany(group => group!)
        .Select(header => $"{header.Key}: {(SensitiveHeaders.Contains(header.Key) ? "***" : string.Join(", ", header.Value))}"));

    public static string FormatHeaders(IHeaderDictionary headers) => string.Join("; ", headers
        .Select(header => $"{header.Key}: {(SensitiveHeaders.Contains(header.Key) ? "***" : header.Value.ToString())}"));

    public static string FormatHttpHeaders(HttpHeaders headers, HttpContentHeaders? contentHeaders) =>
        FormatHeaders(headers, contentHeaders);
}
