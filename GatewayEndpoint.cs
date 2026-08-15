using System.Security.Cryptography;

public sealed class GatewayEndpoint
{
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    public string NormalizedBaseUrl => BaseUrl.TrimEnd('/');

    public Uri BuildApiUri(string path, string? query = null)
    {
        var baseUri = new Uri(NormalizedBaseUrl + "/", UriKind.Absolute);
        var apiPath = path.TrimStart('/');
        var baseIncludesV1 = baseUri.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase);
        if (baseIncludesV1 && apiPath.StartsWith("v1/", StringComparison.OrdinalIgnoreCase))
        {
            apiPath = apiPath[3..];
        }
        else if (!baseIncludesV1 && !apiPath.StartsWith("v1/", StringComparison.OrdinalIgnoreCase))
        {
            apiPath = $"v1/{apiPath}";
        }

        return new UriBuilder(new Uri(baseUri, apiPath)) { Query = query?.TrimStart('?') }.Uri;
    }

    public bool IsValid => Enabled
        && !string.IsNullOrWhiteSpace(Name)
        && Uri.TryCreate(NormalizedBaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        && !string.IsNullOrWhiteSpace(ApiKey);

    public static string CreateGatewayApiKey() => $"sk-{Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()}";
}