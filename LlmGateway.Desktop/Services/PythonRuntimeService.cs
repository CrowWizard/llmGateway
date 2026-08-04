using System.Diagnostics;
using System.ComponentModel;
using System.Net.Http;

namespace LlmGateway.Desktop.Services;

public sealed class PythonRuntimeService
{
    private const string PythonVersion = "3.14.6";
    private const string WindowsInstallerUrl = "https://mirrors.aliyun.com/python-release/windows/python-3.14.6-amd64.exe";
    private const string MacOsInstallerUrl = "https://mirrors.aliyun.com/python-release/macos/python-3.14.6-macos11.pkg";
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    public async Task<string> EnsurePythonAsync()
    {
        var existing = await FindPythonAsync();
        if (existing is not null)
        {
            return $"已检测到 Python：{existing}";
        }

        if (OperatingSystem.IsMacOS())
        {
            return await DownloadAndOpenMacOsInstallerAsync();
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Python 自动安装目前仅支持 Windows 和 macOS。");
        }

        var installerPath = Path.Combine(Path.GetTempPath(), $"python-{PythonVersion}-amd64.exe");
        try
        {
            await using (var responseStream = await _httpClient.GetStreamAsync(WindowsInstallerUrl))
            await using (var installerStream = File.Create(installerPath))
            {
                await responseStream.CopyToAsync(installerStream);
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/quiet InstallAllUsers=0 PrependPath=1 Include_test=0 SimpleInstall=1",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }) ?? throw new InvalidOperationException("无法启动 Python 安装程序。");

            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var error = (await process.StandardError.ReadToEndAsync()).Trim();
                throw new InvalidOperationException($"Python 安装失败（退出码 {process.ExitCode}）{(string.IsNullOrWhiteSpace(error) ? string.Empty : $"：{error}")}。");
            }

            var installed = await FindPythonAsync();
            return installed is null
                ? "Python 安装程序已完成，请重新启动应用以刷新 PATH。"
                : $"Python 安装完成：{installed}";
        }
        finally
        {
            try
            {
                if (File.Exists(installerPath))
                {
                    File.Delete(installerPath);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private async Task<string> DownloadAndOpenMacOsInstallerAsync()
    {
        var installerPath = Path.Combine(Path.GetTempPath(), $"python-{PythonVersion}-macos11.pkg");
        await using (var responseStream = await _httpClient.GetStreamAsync(MacOsInstallerUrl))
        await using (var installerStream = File.Create(installerPath))
        {
            await responseStream.CopyToAsync(installerStream);
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            ArgumentList = { installerPath },
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("无法打开 macOS Python 安装包。");

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"无法打开 Python 安装包（退出码 {process.ExitCode}）。");
        }

        return "已打开 Python 3.14.6 安装器。请在系统安装器中完成授权和安装，然后重新启动应用。";
    }

    private static async Task<string?> FindPythonAsync()
    {
        foreach (var command in OperatingSystem.IsWindows() ? new[] { "python.exe", "python3.exe", "py.exe" } : new[] { "python3", "python" })
        {
            var version = await TryGetVersionAsync(command);
            if (version is not null)
            {
                return $"{command} {version}";
            }
        }

        return null;
    }

    private static async Task<string?> TryGetVersionAsync(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = command,
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var result = string.IsNullOrWhiteSpace(output) ? error : output;
            return process.ExitCode == 0 && result.TrimStart().StartsWith("Python ", StringComparison.OrdinalIgnoreCase)
                ? result.Trim()
                : null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}