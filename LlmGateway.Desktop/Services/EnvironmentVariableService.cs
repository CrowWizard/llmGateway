using System.Diagnostics;
using System.Security;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LlmGateway.Desktop.Services;

public sealed class EnvironmentVariableService(AppPaths paths)
{
    private const int HwndBroadcast = 0xffff;
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    public string Read(string name)
    {
        var current = OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
            : Environment.GetEnvironmentVariable(name);
        current ??= Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrEmpty(current) || !File.Exists(paths.LinuxEnvironmentPath))
        {
            return current ?? string.Empty;
        }

        var prefix = $"{name}=";
        return File.ReadLines(paths.LinuxEnvironmentPath)
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..]
            ?? string.Empty;
    }

    public async Task SaveAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || value.ContainsAny('\r', '\n', '\0'))
        {
            throw new InvalidOperationException("环境变量名或令牌格式无效。");
        }

        if (OperatingSystem.IsWindows())
        {
            using var environmentKey = Registry.CurrentUser.CreateSubKey("Environment", true)
                ?? throw new InvalidOperationException("无法打开当前用户环境变量注册表项。");
            environmentKey.SetValue(name, value, RegistryValueKind.String);
            Environment.SetEnvironmentVariable(name, value);
            SendMessageTimeout(new IntPtr(HwndBroadcast), WmSettingChange, IntPtr.Zero, "Environment", SmtoAbortIfHung, 3000, out _);
            return;
        }

        Environment.SetEnvironmentVariable(name, value);
        var variables = ReadStoredVariables();
        variables[name] = value;
        var content = string.Join(Environment.NewLine, variables.Select(pair => $"{pair.Key}={pair.Value}")) + Environment.NewLine;
        await AtomicFile.WriteUtf8Async(paths.LinuxEnvironmentPath, content, cancellationToken);

        if (OperatingSystem.IsMacOS())
        {
            await AtomicFile.WriteUtf8Async(
                paths.MacOsEnvironmentLaunchAgentPath(name),
                CreateLaunchAgent(name, value),
                cancellationToken);
            File.SetUnixFileMode(paths.MacOsEnvironmentLaunchAgentPath(name), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            await SetMacOsEnvironmentVariableAsync(name, value, cancellationToken);
        }
    }

    private Dictionary<string, string> ReadStoredVariables()
    {
        if (!File.Exists(paths.LinuxEnvironmentPath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return File.ReadLines(paths.LinuxEnvironmentPath)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
    }

    private static string CreateLaunchAgent(string name, string value)
    {
        var escapedName = SecurityElement.Escape(name) ?? string.Empty;
        var escapedValue = SecurityElement.Escape(value) ?? string.Empty;
                return string.Join(Environment.NewLine,
                [
                        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
                        "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">",
                        "<plist version=\"1.0\">",
                        "<dict>",
                        "  <key>Label</key>",
                        $"  <string>com.llmgateway.environment.{escapedName}</string>",
                        "  <key>ProgramArguments</key>",
                        "  <array>",
                        "    <string>/bin/launchctl</string>",
                        "    <string>setenv</string>",
                        $"    <string>{escapedName}</string>",
                        $"    <string>{escapedValue}</string>",
                        "  </array>",
                        "  <key>RunAtLoad</key>",
                        "  <true/>",
                        "</dict>",
                        "</plist>",
                        string.Empty
                ]);
    }

    private static async Task SetMacOsEnvironmentVariableAsync(string name, string value, CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo("/bin/launchctl")
        {
            ArgumentList = { "setenv", name, value },
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("无法启动 launchctl。");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"launchctl setenv 失败，退出代码：{process.ExitCode}。");
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window,
        uint message,
        IntPtr wParam,
        string lParam,
        uint flags,
        uint timeout,
        out IntPtr result);
}

internal static class StringExtensions
{
    public static bool ContainsAny(this string value, params char[] characters) => value.IndexOfAny(characters) >= 0;
}
