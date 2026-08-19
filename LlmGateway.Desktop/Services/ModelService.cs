using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LlmGateway.Desktop.Services;

public sealed class ModelService
{
    private const int MaxResponseBytes = 4 * 1024 * 1024;
    private const int ResponsePreviewLength = 500;
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<IReadOnlyList<string>> FetchAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.ContainsAny('\r', '\n', '\0'))
        {
            throw new InvalidOperationException("请提供格式有效的令牌。");
        }
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || (!baseUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !baseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Base URL 必须是有效的 HTTP 或 HTTPS 地址。");
        }
        if (!string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new InvalidOperationException("Base URL 不能包含查询参数或片段。");
        }

        var path = baseUri.AbsolutePath.TrimEnd('/');
        path = path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? $"{path}/models" : $"{path}/v1/models";
        var modelsUri = new UriBuilder(baseUri) { Path = path, Query = string.Empty, Fragment = string.Empty }.Uri;

        using var request = new HttpRequestMessage(HttpMethod.Get, modelsUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
        {
            throw new InvalidOperationException($"模型服务返回 HTTP {(int)response.StatusCode}，响应超过 4 MB 限制。请求地址：{modelsUri}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var limited = new LimitedReadStream(stream, MaxResponseBytes);
        using var body = new MemoryStream();
        try
        {
            await limited.CopyToAsync(body, cancellationToken);
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("超过 4 MB", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"模型服务返回 HTTP {(int)response.StatusCode}，响应超过 4 MB 限制。请求地址：{modelsUri}", exception);
        }

        var responseText = Encoding.UTF8.GetString(body.GetBuffer(), 0, checked((int)body.Length));
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"模型服务请求失败。请求地址：{modelsUri}；HTTP {(int)response.StatusCode} {response.ReasonPhrase}；Content-Type：{response.Content.Headers.ContentType?.ToString() ?? "未返回"}；响应：{CreatePreview(responseText)}");
        }

        body.Position = 0;
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"模型响应不是有效 JSON。请求地址：{modelsUri}；HTTP {(int)response.StatusCode} {response.ReasonPhrase}；Content-Type：{response.Content.Headers.ContentType?.ToString() ?? "未返回"}；响应前缀：{CreatePreview(responseText)}；解析错误：{exception.Message}", exception);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("模型响应缺少 data 数组。");
            }

            var models = data.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                .Select(item => item.GetProperty("id").GetString())
                .Where(id => !string.IsNullOrWhiteSpace(id) && id.All(character => !char.IsControl(character)))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return models.Length > 0 ? models : throw new InvalidOperationException("响应中没有可用模型 ID。");
        }
    }

    private static string CreatePreview(string responseText)
    {
        var preview = responseText.Trim();
        if (preview.Length > ResponsePreviewLength)
        {
            preview = preview[..ResponsePreviewLength] + "…";
        }

        return preview.Replace('\r', ' ').Replace('\n', ' ');
    }

    private sealed class LimitedReadStream(Stream inner, long limit) : Stream
    {
        private long _totalRead;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _totalRead; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            EnsureWithinLimit(read);
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            EnsureWithinLimit(read);
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }
            base.Dispose(disposing);
        }
        private void EnsureWithinLimit(int read)
        {
            _totalRead += read;
            if (_totalRead > limit)
            {
                throw new InvalidOperationException("模型响应超过 4 MB 限制。");
            }
        }
    }
}
