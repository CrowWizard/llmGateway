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
        await AtomicFile.WriteUtf8Async(paths.LinuxEnvironmentPath, $"{name}={value}{Environment.NewLine}", cancellationToken);
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
