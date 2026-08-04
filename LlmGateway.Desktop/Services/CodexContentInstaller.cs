namespace LlmGateway.Desktop.Services;

public static class CodexContentInstaller
{
    public static Task<string> InstallAsync(string sourceDirectory, string targetDirectory, string temporaryDirectory) =>
        Task.Run(() => Install(sourceDirectory, targetDirectory, temporaryDirectory));

    public static string Install(string sourceDirectory, string targetDirectory, string temporaryDirectory)
    {
        if (Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(temporaryDirectory);
            var backupDirectory = CreateBackupDirectory(temporaryDirectory, Path.GetFileName(targetDirectory));
            Directory.Move(targetDirectory, backupDirectory);
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var targetFile = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile, true);
        }

        return targetDirectory;
    }

    private static string CreateBackupDirectory(string temporaryDirectory, string contentName)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
        var backupDirectory = Path.Combine(temporaryDirectory, $"{contentName}-{timestamp}");
        var attempt = 1;
        while (Directory.Exists(backupDirectory))
        {
            backupDirectory = Path.Combine(temporaryDirectory, $"{contentName}-{timestamp}-{attempt++}");
        }

        return backupDirectory;
    }
}