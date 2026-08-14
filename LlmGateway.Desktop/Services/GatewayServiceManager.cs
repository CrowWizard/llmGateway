using System.Diagnostics;

namespace LlmGateway.Desktop.Services;

public sealed class GatewayServiceManager(AppPaths paths)
{
    public async Task<string> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        var result = await RunScAsync($"query \"{paths.GatewayServiceName}\"", cancellationToken);
        return result.ExitCode == 0 && result.Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)
            ? "Windows 服务运行中"
            : result.ExitCode == 0
                ? "Windows 服务已停止"
                : "Windows 服务未安装";
    }

    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        if (!File.Exists(paths.GatewayExecutablePath))
        {
            throw new FileNotFoundException("未在界面程序同目录找到 LlmGateway.exe。请将 CLI 发布文件与桌面程序放在同一目录。", paths.GatewayExecutablePath);
        }

        var binaryPath = $"\\\"{paths.GatewayExecutablePath}\\\" --service";
        await EnsureSuccessAsync($"create \"{paths.GatewayServiceName}\" binPath= \"{binaryPath}\" start= auto DisplayName= \"LlmGateway\"", cancellationToken);
        await EnsureSuccessAsync($"failure \"{paths.GatewayServiceName}\" reset= 86400 actions= restart/5000/restart/5000/restart/5000", cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        EnsureSuccessAsync($"start \"{paths.GatewayServiceName}\"", cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        EnsureSuccessAsync($"stop \"{paths.GatewayServiceName}\"", cancellationToken, allowAlreadyStopped: true);

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);
        await StartAsync(cancellationToken);
    }

    public async Task UninstallAsync(CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        await StopAsync(cancellationToken);
        await EnsureSuccessAsync($"delete \"{paths.GatewayServiceName}\"", cancellationToken);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows 服务管理只能在 Windows 上使用。");
        }
    }

    private async Task EnsureSuccessAsync(string arguments, CancellationToken cancellationToken, bool allowAlreadyStopped = false)
    {
        EnsureWindows();
        var result = await RunScAsync(arguments, cancellationToken);
        if (result.ExitCode == 0 || (allowAlreadyStopped && result.Output.Contains("1062", StringComparison.Ordinal)))
        {
            return;
        }

        throw new InvalidOperationException($"服务操作失败。请以管理员身份运行界面程序。{Environment.NewLine}{result.Output}");
    }

    private static async Task<(int ExitCode, string Output)> RunScAsync(string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("sc.exe", arguments)
        {
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 sc.exe。");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        output += await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, output.Trim());
    }
}