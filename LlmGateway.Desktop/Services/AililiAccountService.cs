using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmGateway.Desktop.Services;

public sealed record AililiAccount(string Username, string Password, string CodexKey, string ImageKey);

public sealed class AililiAccountService(AppPaths paths, HttpClient? httpClient = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    public AililiAccount? Load()
    {
        if (!File.Exists(paths.AililiCredentialsPath))
        {
            return null;
        }

        return JsonSerializer.Deserialize<AililiAccount>(File.ReadAllText(paths.AililiCredentialsPath), JsonOptions);
    }

    public async Task<AililiAccount> RegisterAsync(CancellationToken cancellationToken = default)
    {
        var account = await RegisterAccountAsync(cancellationToken);
        return await CreateTokensAsync(account, cancellationToken);
    }

    public async Task<AililiAccount> RegisterAccountAsync(CancellationToken cancellationToken = default)
    {
        var username = $"codex_{Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant()}";
        var password = CreatePassword();

        await SendAsync<object>(HttpMethod.Post, "/api/user/register", new
        {
            username,
            password,
            password2 = password,
            aff_code = ""
        }, null, cancellationToken);

        var account = new AililiAccount(username, password, string.Empty, string.Empty);
        await SaveAsync(account, cancellationToken);
        return account;
    }

    public async Task<AililiAccount> CreateTokensAsync(AililiAccount? account = null, CancellationToken cancellationToken = default)
    {
        account ??= Load() ?? throw new InvalidOperationException("请先注册 Ailili 账号。");

        var login = await SendAsync<LoginData>(HttpMethod.Post, "/api/user/login", new
        {
            username = account.Username,
            password = account.Password
        }, null, cancellationToken);

        if (string.IsNullOrWhiteSpace(login.AccessToken))
        {
            throw new InvalidOperationException("Ailili 登录未返回访问令牌。");
        }

        var tokens = await SendAsync<TokenPage>(HttpMethod.Get, "/api/token/?p=0&page_size=20", null, login.AccessToken, cancellationToken);
        if (tokens.Items.All(item => item.Name != "Codex"))
        {
            await CreateTokenAsync("Codex", "OpenAI-gpt", login.AccessToken, cancellationToken);
        }
        if (tokens.Items.All(item => item.Name != "生图"))
        {
            await CreateTokenAsync("生图", "gpt-image-2", login.AccessToken, cancellationToken);
        }

        tokens = await SendAsync<TokenPage>(HttpMethod.Get, "/api/token/?p=0&page_size=20", null, login.AccessToken, cancellationToken);
        var codexToken = tokens.Items.FirstOrDefault(item => item.Name == "Codex")
            ?? throw new InvalidOperationException("未找到已创建的 Codex 令牌。");
        var imageToken = tokens.Items.FirstOrDefault(item => item.Name == "生图")
            ?? throw new InvalidOperationException("未找到已创建的生图令牌。");

        var codexKey = await GetTokenKeyAsync(codexToken.Id, login.AccessToken, cancellationToken);
        var imageKey = await GetTokenKeyAsync(imageToken.Id, login.AccessToken, cancellationToken);
        account = account with { CodexKey = codexKey, ImageKey = imageKey };
        await SaveAsync(account, cancellationToken);
        return account;
    }

    private async Task CreateTokenAsync(string name, string group, string accessToken, CancellationToken cancellationToken)
    {
        await SendAsync<object>(HttpMethod.Post, "/api/token/", new
        {
            name,
            expired_time = -1,
            remain_quota = 0,
            unlimited_quota = true,
            model_limits_enabled = false,
            model_limits = "",
            group,
            cross_group_retry = false
        }, accessToken, cancellationToken);
    }

    private async Task<string> GetTokenKeyAsync(int id, string accessToken, CancellationToken cancellationToken)
    {
        var data = await SendAsync<TokenKeyData>(HttpMethod.Post, $"/api/token/{id}/key", new { }, accessToken, cancellationToken);
        return data.Key.StartsWith("sk-", StringComparison.Ordinal) ? data.Key : $"sk-{data.Key}";
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(new Uri("https://api.ailili.chat"), path));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Ailili 返回了空响应。");
        if (!response.IsSuccessStatusCode || !envelope.Success)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(envelope.Message)
                ? $"Ailili 请求失败（HTTP {(int)response.StatusCode}）。"
                : envelope.Message);
        }
        return envelope.Data!;
    }

    private async Task SaveAsync(AililiAccount account, CancellationToken cancellationToken)
    {
        await AtomicFile.WriteUtf8Async(paths.AililiCredentialsPath, JsonSerializer.Serialize(account, JsonOptions), cancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(paths.AililiCredentialsPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static string CreatePassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
        return string.Create(18, alphabet, static (buffer, chars) =>
        {
            for (var index = 0; index < buffer.Length; index++)
            {
                buffer[index] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
            }
        });
    }

    private sealed record ApiEnvelope<T>(bool Success, string Message, T? Data);
    private sealed record LoginData([property: JsonPropertyName("access_token")] string AccessToken);
    private sealed record TokenPage([property: JsonPropertyName("items")] TokenItem[] Items);
    private sealed record TokenItem(int Id, string Name);
    private sealed record TokenKeyData(string Key);
}