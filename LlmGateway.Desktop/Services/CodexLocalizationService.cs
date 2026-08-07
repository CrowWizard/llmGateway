using System.Diagnostics;

namespace LlmGateway.Desktop.Services;

public sealed class CodexLocalizationService
{
    private static readonly byte[] I18nMarker = "enable_i18n"u8.ToArray();
    private static readonly byte[] DisabledExpression = ",!1)"u8.ToArray();
    private static readonly byte[] EnabledExpression = ",!0)"u8.ToArray();

    public string FindAppAsarPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("界面汉化仅支持 Windows 上的 Codex 桌面应用。");
        }

        var runningPath = FindFromRunningProcess();
        if (runningPath is not null)
        {
            return runningPath;
        }

        var storePath = FindFromStorePackage();
        if (storePath is not null)
        {
            return storePath;
        }

        var installedPath = FindFromKnownLocations();
        return installedPath ?? throw new FileNotFoundException(
            "未找到 app.asar。请先启动 Codex/ChatGPT，或确认解压版与 Microsoft Store 版本已正确安装。");
    }

    public LocalizationResult EnableChinese(string appAsarPath)
    {
        if (!File.Exists(appAsarPath))
        {
            throw new FileNotFoundException("未找到 app.asar。", appAsarPath);
        }

        if (IsStorePackagePath(appAsarPath))
        {
            throw new UnauthorizedAccessException(
                "检测到 Microsoft Store 版 Codex 的 WindowsApps 安装目录。该目录受系统保护，无法安全原地修改；请安装当前用户可写的 Codex 桌面版后重试。");
        }

        var data = File.ReadAllBytes(appAsarPath);
        var markerIndex = data.AsSpan().IndexOf(I18nMarker);
        if (markerIndex < 0)
        {
            throw new InvalidDataException("当前 app.asar 中未找到 enable_i18n，可能是不受支持的 Codex 版本。");
        }

        var searchLength = Math.Min(20, data.Length - markerIndex);
        var expression = data.AsSpan(markerIndex, searchLength);
        var disabledIndex = expression.IndexOf(DisabledExpression);
        if (disabledIndex < 0)
        {
            if (expression.IndexOf(EnabledExpression) >= 0)
            {
                return new LocalizationResult(appAsarPath, false, null, "界面汉化已启用，无需重复修改。");
            }

            throw new InvalidDataException("未找到预期的 !1 开关，已取消修改以避免破坏 app.asar。");
        }

        var backupPath = appAsarPath + ".bak";
        if (!File.Exists(backupPath))
        {
            try
            {
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(appAsarPath, backupPath);
                File.SetAttributes(backupPath, FileAttributes.Normal);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new UnauthorizedAccessException(
                    "无法创建 app.asar 备份，请以管理员身份运行，或将 Codex 安装到当前用户可写目录后重试。", exception);
            }
        }

        data[markerIndex + disabledIndex + 2] = (byte)'0';
        var temporaryPath = appAsarPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.SetAttributes(appAsarPath, FileAttributes.Normal);
            File.WriteAllBytes(temporaryPath, data);
            File.Move(temporaryPath, appAsarPath, true);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new UnauthorizedAccessException(
                "无法修改 app.asar。请以管理员身份运行应用，并确认 Codex/ChatGPT 已完全退出。", exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        var verification = File.ReadAllBytes(appAsarPath);
        var verificationMarker = verification.AsSpan().IndexOf(I18nMarker);
        var verified = verificationMarker >= 0 &&
            verification.AsSpan(verificationMarker, Math.Min(20, verification.Length - verificationMarker)).IndexOf(EnabledExpression) >= 0;
        if (!verified)
        {
            throw new InvalidDataException("写入后的 app.asar 校验失败，请使用 .bak 备份还原。");
        }

        return new LocalizationResult(appAsarPath, true, backupPath, "界面汉化已启用，重启 Codex/ChatGPT 后生效。");
    }

    private static string? FindFromRunningProcess()
    {
        foreach (var processName in new[] { "Codex", "codex", "ChatGPT", "chatgpt" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    var executablePath = process.MainModule?.FileName;
                    var appAsarPath = executablePath is null ? null : FindAppAsarNear(executablePath);
                    if (appAsarPath is not null)
                    {
                        return appAsarPath;
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return null;
    }

    private static string? FindFromKnownLocations()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var packageRoot = Path.Combine(localAppData, "Packages");
        if (Directory.Exists(packageRoot))
        {
            foreach (var package in Directory.EnumerateDirectories(packageRoot, "OpenAI.Codex_*"))
            {
                var appAsarPath = Path.Combine(package, "app", "resources", "app.asar");
                if (File.Exists(appAsarPath))
                {
                    return appAsarPath;
                }
            }
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Codex", "resources", "app.asar"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Codex", "resources", "app.asar"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatGPT", "resources", "app.asar")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindFromStorePackage()
    {
        const string script = "$ErrorActionPreference='SilentlyContinue'; Get-AppxPackage | Where-Object { $_.Name -match 'ChatGPT|Codex|OpenAI' -or $_.PackageFullName -match 'ChatGPT|Codex|OpenAI' } | ForEach-Object { [Console]::Out.WriteLine($_.InstallLocation) }";
        foreach (var shell in new[] { "powershell.exe", "pwsh.exe" })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = $"-NoLogo -NoProfile -NonInteractive -Command \"{script.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                if (process is null)
                {
                    continue;
                }

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);
                foreach (var installLocation in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    foreach (var relativePath in new[] { Path.Combine("resources", "app.asar"), Path.Combine("app", "resources", "app.asar") })
                    {
                        var candidate = Path.Combine(installLocation.Trim(), relativePath);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }
        }

        return null;
    }

    private static string? FindAppAsarNear(string executablePath)
    {
        var executableDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(executableDirectory))
        {
            return null;
        }

        var directories = new[]
        {
            executableDirectory,
            Directory.GetParent(executableDirectory)?.FullName,
            Directory.GetParent(executableDirectory)?.Parent?.FullName
        };
        foreach (var directory in directories.Where(directory => !string.IsNullOrWhiteSpace(directory)))
        {
            var appAsarPath = Path.Combine(directory!, "resources", "app.asar");
            if (File.Exists(appAsarPath))
            {
                return appAsarPath;
            }
        }

        return null;
    }

    private static bool IsStorePackagePath(string appAsarPath)
    {
        var windowsAppsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "WindowsApps");
        return appAsarPath.StartsWith(windowsAppsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record LocalizationResult(string AppAsarPath, bool Changed, string? BackupPath, string Message);