using LlmGateway.Desktop.Models;

namespace LlmGateway.Desktop.Services;

public sealed class CodexBackupService(AppPaths paths)
{
    public BackupItem Create()
    {
        var hasConfig = File.Exists(paths.CodexConfigPath);
        var hasAuth = File.Exists(paths.CodexAuthPath);
        if (!hasConfig && !hasAuth)
        {
            throw new InvalidOperationException("没有可备份的 config.toml 或 auth.json。");
        }

        Directory.CreateDirectory(paths.BackupDirectory);
        var baseId = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        var id = baseId;
        var directory = Path.Combine(paths.BackupDirectory, id);
        for (var suffix = 0; Directory.Exists(directory) && suffix < 1000; suffix++)
        {
            id = $"{baseId}-{suffix + 1}";
            directory = Path.Combine(paths.BackupDirectory, id);
        }

        if (Directory.Exists(directory))
        {
            throw new IOException("无法创建唯一的备份目录。");
        }

        Directory.CreateDirectory(directory);
        try
        {
            if (hasConfig)
            {
                CopyVerified(paths.CodexConfigPath, Path.Combine(directory, "config.toml"));
            }
            if (hasAuth)
            {
                CopyVerified(paths.CodexAuthPath, Path.Combine(directory, "auth.json"));
            }
        }
        catch
        {
            Directory.Delete(directory, true);
            throw;
        }

        return CreateItem(directory);
    }

    public IReadOnlyList<BackupItem> List()
    {
        if (!Directory.Exists(paths.BackupDirectory))
        {
            return [];
        }

        return Directory.EnumerateDirectories(paths.BackupDirectory)
            .Select(CreateItem)
            .Where(item => item.HasConfig || item.HasAuth)
            .OrderByDescending(item => item.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task RestoreAsync(BackupItem backup, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(paths.BackupDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var selected = Path.GetFullPath(backup.DirectoryPath).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(selected), root, PathComparison))
        {
            throw new InvalidOperationException("所选备份目录无效。");
        }

        var backupConfig = Path.Combine(selected, "config.toml");
        var backupAuth = Path.Combine(selected, "auth.json");
        if (!File.Exists(backupConfig) && !File.Exists(backupAuth))
        {
            throw new InvalidOperationException("所选备份不包含可还原文件。");
        }

        var oldConfig = await SnapshotAsync(paths.CodexConfigPath, cancellationToken);
        var oldAuth = await SnapshotAsync(paths.CodexAuthPath, cancellationToken);
        var newConfig = await SnapshotAsync(backupConfig, cancellationToken);
        var newAuth = await SnapshotAsync(backupAuth, cancellationToken);

        try
        {
            await ApplyAsync(paths.CodexConfigPath, newConfig, cancellationToken);
            await ApplyAsync(paths.CodexAuthPath, newAuth, cancellationToken);
        }
        catch (Exception restoreException)
        {
            var rollbackErrors = new List<string>();
            try
            {
                await ApplyAsync(paths.CodexConfigPath, oldConfig, CancellationToken.None);
            }
            catch (Exception exception)
            {
                rollbackErrors.Add($"config.toml: {exception.Message}");
            }
            try
            {
                await ApplyAsync(paths.CodexAuthPath, oldAuth, CancellationToken.None);
            }
            catch (Exception exception)
            {
                rollbackErrors.Add($"auth.json: {exception.Message}");
            }

            if (rollbackErrors.Count > 0)
            {
                throw new InvalidOperationException($"还原失败且回滚不完整，请人工检查配置目录。原始错误：{restoreException.Message}；{string.Join("；", rollbackErrors)}", restoreException);
            }
            throw new InvalidOperationException($"还原失败，已恢复还原前状态：{restoreException.Message}", restoreException);
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static BackupItem CreateItem(string directory)
    {
        var id = Path.GetFileName(directory);
        var displayName = id.Length >= 23
            ? $"{id[..10]}  {id.Substring(11, 2)}:{id.Substring(14, 2)}:{id.Substring(17, 2)}.{id.Substring(20, 3)}{id[23..]}"
            : id;
        return new BackupItem(
            id,
            displayName,
            directory,
            File.Exists(Path.Combine(directory, "config.toml")),
            File.Exists(Path.Combine(directory, "auth.json")));
    }

    private static void CopyVerified(string source, string destination)
    {
        var expectedLength = new FileInfo(source).Length;
        File.Copy(source, destination, false);
        if (!File.Exists(destination) || new FileInfo(destination).Length != expectedLength)
        {
            throw new IOException($"备份文件复制不完整：{source}");
        }
    }

    private static async Task<string?> SnapshotAsync(string path, CancellationToken cancellationToken) =>
        File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;

    private static async Task ApplyAsync(string path, string? content, CancellationToken cancellationToken)
    {
        if (content is null)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            return;
        }

        await AtomicFile.WriteUtf8Async(path, content, cancellationToken);
    }
}
