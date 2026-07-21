using System.Text;

namespace LlmGateway.Desktop.Services;

internal static class AtomicFile
{
    public static string ReadUtf8(string path) => File.Exists(path)
        ? File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF')
        : string.Empty;

    public static async Task WriteUtf8Async(string path, string content, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("目标文件没有父目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{path}.tmp.{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}";

        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false), cancellationToken);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static string BackupIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        var backupPath = $"{path}.backup.{DateTime.Now:yyyyMMddHHmmssfff}";
        File.Copy(path, backupPath, true);
        return backupPath;
    }
}
