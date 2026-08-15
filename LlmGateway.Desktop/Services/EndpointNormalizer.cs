namespace LlmGateway.Desktop.Services;

public static class EndpointNormalizer
{
    public static string Normalize(string value)
    {
        var trimmed = value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || !uri.AbsolutePath.Equals("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return new UriBuilder(uri) { Path = string.Empty }.Uri.ToString().TrimEnd('/');
    }

    public static string GetConfigurationName(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return "默认配置";
        }

        var labels = uri.Host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return labels.Length >= 3 ? labels[^2] : labels[0];
    }

    public static string GetEnvironmentKey(string value) =>
        $"{GetConfigurationName(value).ToUpperInvariant()}_API_KEY";
}
