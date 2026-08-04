using System.ComponentModel;
using System.Diagnostics;

namespace LlmGateway.Desktop.Services;

public sealed class ChatGptInstallerService
{
    private const string ChatGptProductId = "9plm9xgg6vks";

    public async Task<string> InstallAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ChatGPT 自动安装仅支持 Windows。");
        }

        var wingetPath = await FindWingetAsync();
        if (wingetPath is null)
        {
            OpenChatGptStorePage();
            return "未检测到 winget，已打开 Microsoft Store 的 ChatGPT 页面。请在 Store 中完成安装。";
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = wingetPath,
            ArgumentList = { "install", "--id", ChatGptProductId, "--exact", "--accept-source-agreements", "--accept-package-agreements" },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("无法启动 winget。" );

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        if (process.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(error) ? output : error;
            throw new InvalidOperationException($"winget 安装失败（退出码 {process.ExitCode}）{(string.IsNullOrWhiteSpace(details) ? string.Empty : $"：{details}")}。");
        }

        return string.IsNullOrWhiteSpace(output)
            ? "ChatGPT 安装命令已完成。"
            : $"ChatGPT 安装完成：{output}";
    }

    private static async Task<string?> FindWingetAsync()
    {
        foreach (var path in WingetCandidates())
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (process is null)
                {
                    continue;
                }

                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    return path;
                }
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> WingetCandidates()
    {
        yield return "winget.exe";

        var windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");
        if (File.Exists(windowsApps))
        {
            yield return windowsApps;
        }
    }

    private static void OpenChatGptStorePage()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = $"ms-windows-store://pdp/?productid={ChatGptProductId}",
            UseShellExecute = true
        });
    }
}