using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;

namespace LlmGateway.Desktop.Services;

public sealed class NodeRuntimeService
{
    private const string NodeVersion = "22.22.3";
    private const string WindowsInstallerUrl = "https://mirrors.aliyun.com/nodejs-release/v22.22.3/node-v22.22.3-x64.msi";
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    public async Task<string> EnsureNodeAsync()
    {
        var existing = await FindNodeAsync();
        if (existing is not null)
        {
            return $"已检测到 Node.js：{existing}";
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Node.js 自动安装目前仅支持 Windows。");
        }

        var installerPath = Path.Combine(Path.GetTempPath(), $"node-v{NodeVersion}-x64.msi");
        try
        {
            await using (var responseStream = await _httpClient.GetStreamAsync(WindowsInstallerUrl))
            await using (var installerStream = File.Create(installerPath))
            {
                await responseStream.CopyToAsync(installerStream);
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                ArgumentList = { "/i", installerPath, "/qn", "/norestart", "ADDLOCAL=ALL" },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }) ?? throw new InvalidOperationException("无法启动 Node.js 安装程序。" );

            await process.WaitForExitAsync();
            if (process.ExitCode is not 0 and not 3010)
            {
                var error = (await process.StandardError.ReadToEndAsync()).Trim();
                throw new InvalidOperationException($"Node.js 安装失败（退出码 {process.ExitCode}）{(string.IsNullOrWhiteSpace(error) ? string.Empty : $"：{error}")}。");
            }

            var nodeDirectory = FindInstalledNodeDirectory();
            if (nodeDirectory is not null)
            {
                AddToUserPath(nodeDirectory);
            }

            var installed = await FindNodeAsync();
            var restartMessage = process.ExitCode == 3010 ? "安装程序要求重启系统以完成更新；" : string.Empty;
            return installed is null
                ? $"{restartMessage}Node.js 安装程序已完成，请重新启动应用以刷新 PATH。"
                : $"{restartMessage}Node.js 安装完成：{installed}";
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

    private static async Task<string?> FindNodeAsync()
    {
        foreach (var command in new[] { "node.exe", "node" })
        {
            var version = await TryGetVersionAsync(command);
            if (version is not null)
            {
                return $"{command} {version}";
            }
        }

        var nodeDirectory = FindInstalledNodeDirectory();
        return nodeDirectory is null ? null : await TryGetVersionAsync(Path.Combine(nodeDirectory, "node.exe"));
    }

    private static string? FindInstalledNodeDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs")
        };
        return candidates.FirstOrDefault(directory => File.Exists(Path.Combine(directory, "node.exe")));
    }

    private static void AddToUserPath(string directory)
    {
        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? string.Empty;
        var entries = userPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!entries.Contains(directory, StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("PATH", string.IsNullOrWhiteSpace(userPath) ? directory : $"{directory}{Path.PathSeparator}{userPath}", EnvironmentVariableTarget.User);
        }

        var processPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (!processPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(directory, StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("PATH", $"{directory}{Path.PathSeparator}{processPath}");
        }
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
            return process.ExitCode == 0 && result.TrimStart().StartsWith('v') ? result.Trim() : null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}