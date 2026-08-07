namespace LlmGateway.Desktop.Services;

public sealed class ErrorLogService
{
    private readonly string _logDirectory;
    private readonly object _sync = new();

    public ErrorLogService(AppPaths paths)
    {
        _logDirectory = Path.Combine(paths.CodexDirectory, "logs");
    }

    public string LogDirectory => _logDirectory;

    public void WriteInformation(string operation, string message) => WriteEntry(operation, message);

    public void Write(string operation, Exception exception)
        => WriteEntry(operation, exception.ToString());

    private void WriteEntry(string operation, string detail)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            var logPath = Path.Combine(_logDirectory, $"{GetFileName(operation)}.log");

            var entry = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {operation}\n{detail}\n{new string('-', 80)}\n";
            lock (_sync)
            {
                File.AppendAllText(logPath, entry);
            }
        }
        catch
        {
            // Logging must never replace the original operation failure.
        }
    }

    private static string GetFileName(string operation)
    {
        var fileName = string.Concat(operation.Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-'));
        return $"{fileName.Trim('-')}-errors";
    }
}
