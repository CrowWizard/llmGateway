namespace LlmGateway.Desktop.Services;

public static class EndpointNormalizer
{
    public static string Normalize(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, "image.lingjue.chat", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var builder = new UriBuilder(uri)
        {
            Host = "api.ailili.chat"
        };
        return builder.Uri.AbsoluteUri.TrimEnd('/');
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
