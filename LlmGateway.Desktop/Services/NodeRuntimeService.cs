using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace LlmGateway.Desktop.Services;

public sealed class NodeRuntimeService(AppPaths paths, HttpClient? httpClient = null, ErrorLogService? errorLogService = null)
{
    private const string NodeVersion = "22.22.3";
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    private readonly ErrorLogService _errorLogService = errorLogService ?? new ErrorLogService(paths);

    public async Task<string> EnsureNodeAsync()
    {
        var managedNode = GetManagedNodePath();
        var managedVersion = await TryGetVersionAsync(managedNode);
        if (managedVersion is not null)
        {
            if (!File.Exists(Path.Combine(paths.NodeRuntimeSkillDirectory, "node_modules", "sharp", "package.json")))
            {
                await InstallSharedPackagesAsync(managedNode, Path.GetDirectoryName(managedNode)!);
            }
            return $"托管 Node.js 已就绪：{managedNode} {managedVersion}";
        }

        var platform = GetPlatformPackage();
        if (platform is null)
        {
            return await SystemFallbackAsync("当前平台暂无托管 Node.js 包");
        }

        var archivePath = Path.Combine(paths.CodexTemporaryDirectory, platform.ArchiveName);
        var extractDirectory = Path.Combine(paths.CodexTemporaryDirectory, $"node-{Guid.NewGuid():N}");
        try
        {
            WriteLog($"开始安装 Node.js {NodeVersion}。内置 Skill：{paths.NodeRuntimeSourceDirectory}；目标目录：{paths.NodeRuntimeSkillDirectory}；临时目录：{paths.CodexTemporaryDirectory}");
            Directory.CreateDirectory(paths.CodexTemporaryDirectory);
            WriteLog("已创建临时目录，开始复制内置 noderuntime Skill。");
            await InstallRuntimeSkillAsync();
            WriteLog($"内置 Skill 已就绪，开始下载归档：{archivePath}");
            await DownloadArchiveAsync(platform, archivePath);

            WriteLog($"归档下载完成，开始解压到：{extractDirectory}");
            ZipFile.ExtractToDirectory(archivePath, extractDirectory);
            var extractedRoot = Directory.EnumerateDirectories(extractDirectory).Single();
            var targetDirectory = Path.GetDirectoryName(managedNode)!;
            WriteLog($"解压完成，移动运行时目录：{extractedRoot} -> {targetDirectory}");
            CodexContentInstaller.MoveToTemporary(targetDirectory, paths.CodexTemporaryDirectory);
            Directory.Move(extractedRoot, targetDirectory);

            var installedVersion = await TryGetVersionAsync(managedNode)
                ?? throw new InvalidOperationException("托管 Node.js 解压完成，但运行验证失败。");
            WriteLog($"Node.js {installedVersion} 验证成功，开始安装共享依赖。");
            await InstallSharedPackagesAsync(managedNode, targetDirectory);
            await WriteManifestAsync(platform, installedVersion);
            WriteLog("托管 Node.js 和共享依赖安装完成。");
            return $"托管 Node.js 安装完成：{managedNode} {installedVersion}";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or InvalidOperationException)
        {
            WriteLog($"安装失败：{exception}");
            return await SystemFallbackAsync($"托管 Node.js 安装失败：{exception.Message}");
        }
        finally
        {
            TryDeleteFile(archivePath);
            TryDeleteDirectory(extractDirectory);
        }
    }

    public string GetManagedNodePath()
    {
        return Path.Combine(paths.NodeRuntimeDirectory, GetRuntimeIdentifier(), "node.exe");
    }

    private async Task InstallRuntimeSkillAsync()
    {
        if (!File.Exists(Path.Combine(paths.NodeRuntimeSourceDirectory, "SKILL.md")))
        {
            throw new InvalidOperationException("未找到内置 noderuntime Skill。请重新安装应用。");
        }

        await CodexContentInstaller.InstallAsync(paths.NodeRuntimeSourceDirectory, paths.NodeRuntimeSkillDirectory, paths.CodexTemporaryDirectory);
    }

    private async Task DownloadArchiveAsync(PlatformPackage platform, string archivePath)
    {
        var failures = new List<string>();
        foreach (var url in platform.Urls)
        {
            try
            {
                WriteLog($"尝试下载 Node.js：{url}");
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("LlmGateway/1.0");
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                await using var responseStream = await response.Content.ReadAsStreamAsync();
                await using var archiveStream = File.Create(archivePath);
                await responseStream.CopyToAsync(archiveStream);
                WriteLog($"下载成功：{url}");
                return;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                TryDeleteFile(archivePath);
                WriteLog($"下载失败：{url}；{exception.Message}");
                failures.Add($"{url}：{exception.Message}");
            }
        }

        throw new HttpRequestException($"Node.js 压缩包下载失败，已尝试 {failures.Count} 个下载源：{string.Join("；", failures)}");
    }

    private async Task InstallSharedPackagesAsync(string nodePath, string targetDirectory)
    {
        var npmPath = OperatingSystem.IsWindows() ? Path.Combine(targetDirectory, "npm.cmd") : Path.Combine(targetDirectory, "bin", "npm");
        if (!File.Exists(npmPath))
        {
            throw new InvalidOperationException("托管 Node.js 中未找到 npm，无法安装共享图片处理库。");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = npmPath,
            WorkingDirectory = paths.NodeRuntimeSkillDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["PATH"] = $"{Path.GetDirectoryName(nodePath)}{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH")}";
        var failures = new List<string>();
        foreach (var registry in new[] { "https://registry.npmmirror.com", "https://registry.npmjs.org" })
        {
            WriteLog($"尝试通过 npm registry 安装共享依赖：{registry}");
            startInfo.ArgumentList.Clear();
            startInfo.ArgumentList.Add("install");
            startInfo.ArgumentList.Add("--omit=dev");
            startInfo.ArgumentList.Add("--registry");
            startInfo.ArgumentList.Add(registry);
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动托管 npm。");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = (await outputTask).Trim();
            var error = (await errorTask).Trim();
            if (process.ExitCode == 0)
            {
                WriteLog($"共享依赖安装成功：{registry}");
                return;
            }

            var detail = string.IsNullOrWhiteSpace(error) ? output : error;
            WriteLog($"共享依赖安装失败：{registry}；{TrimError(detail)}");
            failures.Add($"{registry}: {TrimError(detail)}");
        }

        throw new InvalidOperationException($"共享 Node 库安装失败。已尝试 npm 镜像：{string.Join("；", failures)}");
    }

    private static string TrimError(string value) => value.Length <= 500 ? value : value[^500..];

    private void WriteLog(string message) => _errorLogService.WriteInformation("处理 Node.js", message);

    private async Task WriteManifestAsync(PlatformPackage platform, string version)
    {
        var manifest = JsonSerializer.Serialize(new
        {
            version,
            rid = GetRuntimeIdentifier(),
            executable = Path.GetRelativePath(paths.NodeRuntimeSkillDirectory, GetManagedNodePath()),
            source = platform.Urls[0],
            fallback = "system-node"
        }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(paths.NodeRuntimeSkillDirectory, "runtime.json"), manifest + Environment.NewLine);
    }

    private static PlatformPackage? GetPlatformPackage()
    {
        const string archiveName = $"node-v{NodeVersion}-win-x64.zip";
        return OperatingSystem.IsWindows() && Environment.Is64BitOperatingSystem
            ? new PlatformPackage(archiveName,
            [
                $"https://mirrors.aliyun.com/nodejs-release/v{NodeVersion}/{archiveName}",
                $"https://cdn.npmmirror.com/binaries/node/v{NodeVersion}/{archiveName}",
                $"https://nodejs.org/dist/v{NodeVersion}/{archiveName}"
            ])
            : null;
    }

    private static string GetRuntimeIdentifier() => "win-x64";

    private static async Task<string> SystemFallbackAsync(string reason)
    {
        var systemNode = await FindSystemNodeAsync();
        return systemNode is not null
            ? $"{reason}，已降级使用系统 Node.js：{systemNode}"
            : throw new InvalidOperationException($"{reason}，且未检测到系统 Node.js。");
    }

    private static async Task<string?> FindSystemNodeAsync()
    {
        foreach (var command in OperatingSystem.IsWindows() ? new[] { "node.exe", "node" } : new[] { "node", "nodejs" })
        {
            var version = await TryGetVersionAsync(command);
            if (version is not null) return $"{command} {version}";
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
            if (process is null) return null;
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

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { }
    }

    private sealed record PlatformPackage(string ArchiveName, IReadOnlyList<string> Urls);
}