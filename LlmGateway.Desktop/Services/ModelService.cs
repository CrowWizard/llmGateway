using System.Net.Http.Headers;
using System.Text.Json;

namespace LlmGateway.Desktop.Services;

public sealed class ModelService
{
    private const int MaxResponseBytes = 4 * 1024 * 1024;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<IReadOnlyList<string>> FetchAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.ContainsAny('\r', '\n', '\0'))
        {
            throw new InvalidOperationException("请提供格式有效的令牌。");
        }
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("获取模型仅允许使用 HTTPS Base URL。");
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
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("LlmGateway-Desktop/1.0");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"模型服务返回 HTTP {(int)response.StatusCode}，请检查 Base URL 和令牌。");
        }
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
        {
            throw new InvalidOperationException("模型响应超过 4 MB 限制。");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var limited = new LimitedReadStream(stream, MaxResponseBytes);
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(limited, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"模型响应不是有效 JSON：{exception.Message}", exception);
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
