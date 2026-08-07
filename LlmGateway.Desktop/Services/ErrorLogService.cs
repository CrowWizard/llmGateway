namespace LlmGateway.Desktop.Services;

public sealed class ErrorLogService
{
    private readonly string _logPath;
    private readonly object _sync = new();

    public ErrorLogService(AppPaths paths)
    {
        _logPath = Path.Combine(paths.CodexDirectory, "llm-gateway-errors.log");
    }

    public string LogPath => _logPath;

    public void Write(string operation, Exception exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var entry = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {operation}\n{exception}\n{new string('-', 80)}\n";
            lock (_sync)
            {
                File.AppendAllText(_logPath, entry);
            }
        }
        catch
        {
            // Logging must never replace the original operation failure.
        }
    }
}
