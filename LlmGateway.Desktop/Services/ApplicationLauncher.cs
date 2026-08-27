using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using LlmGateway.Desktop.Models;

namespace LlmGateway.Desktop.Services;

public sealed class ApplicationLauncher
{
    public ApplicationDetection DetectChatGpt()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ApplicationDetection(false, "ChatGPT", "当前平台暂不支持检测 ChatGPT 桌面应用", null);
        }

        var storeApp = DetectWindowsStoreApp("ChatGPT");
        if (storeApp is not null)
        {
            return storeApp;
        }

        var registered = FindRegisteredExecutable("ChatGPT", "chatgpt.exe");
        if (registered is not null)
        {
            return new ApplicationDetection(true, "ChatGPT", $"{registered}（传统注册表）", registered);
        }

        var command = FindOnPath(["chatgpt.exe"]);
        return command is null
            ? new ApplicationDetection(false, "ChatGPT", "未在 Store/MSIX、注册表或 PATH 中找到 ChatGPT", null)
            : new ApplicationDetection(true, "ChatGPT", command, command);
    }

    public void LaunchChatGpt(string workingDirectory)
    {
        Launch(DetectChatGpt(), workingDirectory);
    }

    [SupportedOSPlatform("windows")]
    public int LaunchChatGpt(string workingDirectory, string arguments)
    {
        var detection = DetectChatGpt();
        if (!detection.Found)
        {
            throw new InvalidOperationException(detection.Description);
        }

        if (!string.IsNullOrWhiteSpace(detection.AppUserModelId))
        {
            return LaunchStoreApp(detection.AppUserModelId, arguments);
        }

        if (string.IsNullOrWhiteSpace(detection.Command))
        {
            throw new InvalidOperationException($"{detection.Name} 缺少可启动命令。");
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = detection.Command,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        });
        return process?.Id ?? throw new InvalidOperationException("Codex Desktop 启动失败。");
    }

    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("请输入有效的网页地址。", nameof(url));
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });
    }

    public async Task CloseManagedClientsAsync(CancellationToken cancellationToken = default)
    {
        var processes = new[] { "ChatGPT", "chatgpt", "codex" }
            .SelectMany(Process.GetProcessesByName)
            .GroupBy(process => process.Id)
            .Select(group => group.First())
            .ToArray();

        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    process.WaitForExit();
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

    public void OpenDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("explorer.exe", $"\"{directory}\"")
            : OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("open", directory)
                : new ProcessStartInfo("xdg-open", directory);
        startInfo.UseShellExecute = false;
        Process.Start(startInfo);
    }

    private static void Launch(ApplicationDetection detection, string workingDirectory)
    {
        if (!detection.Found)
        {
            throw new InvalidOperationException(detection.Description);
        }

        if (!string.IsNullOrWhiteSpace(detection.AppUserModelId))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"shell:AppsFolder\\{detection.AppUserModelId}",
                UseShellExecute = true
            });
            return;
        }

        if (string.IsNullOrWhiteSpace(detection.Command))
        {
            throw new InvalidOperationException($"{detection.Name} 缺少可启动命令。");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = detection.Command,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        });
    }

    [SupportedOSPlatform("windows")]
    private static int LaunchStoreApp(string appUserModelId, string arguments)
    {
        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        var result = manager.ActivateApplication(appUserModelId, arguments, ActivateOptions.NoErrorUi, out var processId);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        return unchecked((int)processId);
    }

    [Flags]
    private enum ActivateOptions
    {
        NoErrorUi = 0x00000002
    }

    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            ActivateOptions options,
            out uint processId);
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager
    {
    }

    [SupportedOSPlatform("windows")]
    private static ApplicationDetection? DetectWindowsStoreApp(string target)
    {
        const string script = "$ErrorActionPreference='SilentlyContinue'; Get-StartApps | ForEach-Object { [Console]::Out.WriteLine($_.Name + \"`t\" + $_.AppID) }";
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
                var candidates = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Split('\t', 2))
                    .Where(parts => parts.Length == 2 && IsTarget(target, $"{parts[0]} {parts[1]}"))
                    .OrderByDescending(parts => string.Equals(parts[0], target, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (candidates.Length > 0)
                {
                    var name = candidates[0][0];
                    var appUserModelId = candidates[0][1];
                    return new ApplicationDetection(
                        true,
                        target,
                        $"Microsoft Store/MSIX 应用 · {name} · {appUserModelId}",
                        null,
                        appUserModelId);
                }
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }
        }

        return DetectStoreAppFromRegistry(target);
    }

    [SupportedOSPlatform("windows")]
    private static ApplicationDetection? DetectStoreAppFromRegistry(string target)
    {
        const string packagesPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
        using var packages = Registry.CurrentUser.OpenSubKey(packagesPath);
        if (packages is null)
        {
            return null;
        }

        foreach (var packageName in packages.GetSubKeyNames().Where(name => IsTarget(target, name)))
        {
            using var package = packages.OpenSubKey(packageName);
            var familyName = package?.GetValue("PackageFamilyName") as string;
            using var applications = package?.OpenSubKey("Applications");
            if (string.IsNullOrWhiteSpace(familyName) || applications is null)
            {
                continue;
            }

            foreach (var applicationId in applications.GetSubKeyNames())
            {
                using var application = applications.OpenSubKey(applicationId);
                var explicitId = application?.GetValue("AppUserModelID") as string
                    ?? application?.GetValue("ApplicationUserModelId") as string;
                var appUserModelId = string.IsNullOrWhiteSpace(explicitId)
                    ? $"{familyName}!{applicationId}"
                    : explicitId;
                if (IsTarget(target, $"{packageName} {applicationId} {appUserModelId}"))
                {
                    return new ApplicationDetection(
                        true,
                        target,
                        $"Microsoft Store/MSIX 应用 · {appUserModelId}（AppModel 注册表）",
                        null,
                        appUserModelId);
                }
            }
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string? FindRegisteredExecutable(string target, string executableName)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var appPath = baseKey.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{executableName}");
                    var directPath = NormalizeExecutablePath(appPath?.GetValue(null) as string);
                    if (directPath is not null)
                    {
                        return directPath;
                    }

                    using var uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null)
                    {
                        continue;
                    }

                    foreach (var subKeyName in uninstall.GetSubKeyNames())
                    {
                        using var item = uninstall.OpenSubKey(subKeyName);
                        var displayName = item?.GetValue("DisplayName") as string;
                        if (!IsTarget(target, displayName ?? string.Empty))
                        {
                            continue;
                        }

                        var iconPath = NormalizeExecutablePath(item?.GetValue("DisplayIcon") as string);
                        if (iconPath is not null)
                        {
                            return iconPath;
                        }

                        var installLocation = item?.GetValue("InstallLocation") as string;
                        var candidate = string.IsNullOrWhiteSpace(installLocation)
                            ? null
                            : Path.Combine(installLocation, executableName);
                        if (candidate is not null && File.Exists(candidate))
                        {
                            return Path.GetFullPath(candidate);
                        }
                    }
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
                {
                }
            }
        }

        return null;
    }

    private static string? NormalizeExecutablePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = Environment.ExpandEnvironmentVariables(value.Trim());
        if (trimmed.StartsWith('"'))
        {
            var endQuote = trimmed.IndexOf('"', 1);
            trimmed = endQuote > 1 ? trimmed[1..endQuote] : trimmed.Trim('"');
        }
        else
        {
            var comma = trimmed.LastIndexOf(',');
            if (comma > 0)
            {
                trimmed = trimmed[..comma];
            }
        }

        return File.Exists(trimmed) ? Path.GetFullPath(trimmed) : null;
    }

    private static bool IsTarget(string target, string value)
    {
        if (!value.Contains(target, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.Equals(target, "ChatGPT", StringComparison.OrdinalIgnoreCase)
            || !value.Contains("classic", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindOnPath(IEnumerable<string> names)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory.Trim('"'), name);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }
        return null;
    }
}
