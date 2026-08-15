using Microsoft.Extensions.Logging;

public enum GatewayLogLevel
{
    Error,
    Information,
    Debug
}

public static class GatewayLogLevels
{
    public static GatewayLogLevel Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "debug" => GatewayLogLevel.Debug,
        "information" or "info" => GatewayLogLevel.Information,
        _ => GatewayLogLevel.Error
    };

    public static LogLevel ToMicrosoftLogLevel(this GatewayLogLevel value) => value switch
    {
        GatewayLogLevel.Debug => LogLevel.Debug,
        GatewayLogLevel.Information => LogLevel.Information,
        _ => LogLevel.Error
    };
}