using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

public sealed class EndpointForwarder(IHttpClientFactory httpClientFactory, ModelRegistry modelRegistry)
{
    public async Task ForwardAsync(HttpContext context, IReadOnlyDictionary<string, string> extraHeaders, IReadOnlyDictionary<string, string> endpointMappings, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken);
        var model = GetModel(body);
        var candidates = modelRegistry.GetCandidates(model);
        if (candidates.Count == 0)
        {
            await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, "没有可用的上游 Endpoint。请先配置并等待模型列表刷新。", "no_upstream");
            return;
        }

        HttpResponseMessage? response = null;
        GatewayEndpoint? selected = null;
        foreach (var endpoint in candidates)
        {
            try
            {
                response = await SendAsync(context.Request, endpoint, body, extraHeaders, endpointMappings, cancellationToken);
            }
            catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }

            if (!IsRetryable(response.StatusCode))
            {
                selected = endpoint;
                break;
            }

            response.Dispose();
            response = null;
        }

        if (response is null || selected is null)
        {
            await WriteErrorAsync(context, StatusCodes.Status502BadGateway, "所有支持该模型的上游 Endpoint 均不可用。", "upstream_unavailable");
            return;
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                modelRegistry.MarkSuccessful(model, selected);
            }

            await CopyResponseAsync(context, response, cancellationToken);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequest request, GatewayEndpoint endpoint, byte[] body, IReadOnlyDictionary<string, string> extraHeaders, IReadOnlyDictionary<string, string> endpointMappings, CancellationToken cancellationToken)
    {
        var mappedPath = EndpointMapper.MapPath(request.Path, endpointMappings);
        var target = endpoint.BuildApiUri(mappedPath, request.QueryString.Value);
        var upstream = new HttpRequestMessage(new HttpMethod(request.Method), target);
        foreach (var header in request.Headers)
        {
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            upstream.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        if (body.Length > 0)
        {
            upstream.Content = new ByteArrayContent(body);
            if (!string.IsNullOrWhiteSpace(request.ContentType))
            {
                upstream.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
            }
        }

        foreach (var (name, value) in extraHeaders)
        {
            upstream.Headers.Remove(name);
            upstream.Headers.TryAddWithoutValidation(name, value);
        }

        upstream.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
        return await httpClientFactory.CreateClient("gateway-upstream").SendAsync(upstream, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static string? GetModel(byte[] body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String
                ? model.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsRetryable(HttpStatusCode statusCode) => statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static async Task CopyResponseAsync(HttpContext context, HttpResponseMessage upstream, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = (int)upstream.StatusCode;
        foreach (var header in upstream.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        foreach (var header in upstream.Content.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        context.Response.Headers.Remove("transfer-encoding");
        await using var stream = await upstream.Content.ReadAsStreamAsync(cancellationToken);
        await stream.CopyToAsync(context.Response.Body, cancellationToken);
    }

    private static Task WriteErrorAsync(HttpContext context, int statusCode, string message, string code)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new { error = new { message, type = code } });
    }
}