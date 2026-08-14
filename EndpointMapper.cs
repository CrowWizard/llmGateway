public static class EndpointMapper
{
    public static string MapPath(string requestPath, IReadOnlyDictionary<string, string> mappings)
    {
        var path = requestPath.StartsWith('/') ? requestPath : "/" + requestPath;
        var match = mappings
            .Where(mapping => MatchesPath(path, mapping.Key))
            .OrderByDescending(mapping => Normalize(mapping.Key).Length)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(match.Key) || string.IsNullOrWhiteSpace(match.Value))
        {
            return path;
        }

        var source = Normalize(match.Key);
        var destination = Normalize(match.Value);
        return destination + path[source.Length..];
    }

    public static bool MatchesPath(string path, string source)
    {
        var normalized = Normalize(source);
        return path.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) => "/" + path.Trim('/');
}