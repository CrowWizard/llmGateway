using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
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

    private readonly HttpClient _httpClient = httpClient ?? CreateHttpClient();
    private string? _accessToken;
    private string? _userId;

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
        await LoginAsync(account, cancellationToken);
        return account;
    }

    public async Task<AililiAccount> CreateTokensAsync(AililiAccount? account = null, CancellationToken cancellationToken = default)
    {
        account ??= Load() ?? throw new InvalidOperationException("请先注册 Ailili 账号。");

        if (_accessToken is null)
        {
            await LoginAsync(account, cancellationToken);
        }

        var accessToken = _accessToken;

        var tokens = await SendAsync<TokenPage>(HttpMethod.Get, "/api/token/?p=0&page_size=20", null, accessToken, cancellationToken);
        if (tokens.Items.All(item => item.Name != "Codex"))
        {
            await CreateTokenAsync("Codex", "OpenAI-gpt", accessToken, cancellationToken);
        }
        if (tokens.Items.All(item => item.Name != "生图"))
        {
            await CreateTokenAsync("生图", "gpt-image-2", accessToken, cancellationToken);
        }

        tokens = await SendAsync<TokenPage>(HttpMethod.Get, "/api/token/?p=0&page_size=20", null, accessToken, cancellationToken);
        var codexToken = tokens.Items.FirstOrDefault(item => item.Name == "Codex")
            ?? throw new InvalidOperationException("未找到已创建的 Codex 令牌。");
        var imageToken = tokens.Items.FirstOrDefault(item => item.Name == "生图")
            ?? throw new InvalidOperationException("未找到已创建的生图令牌。");

        var codexKey = await GetTokenKeyAsync(codexToken.Id, accessToken, cancellationToken);
        var imageKey = await GetTokenKeyAsync(imageToken.Id, accessToken, cancellationToken);
        account = account with { CodexKey = codexKey, ImageKey = imageKey };
        await SaveAsync(account, cancellationToken);
        return account;
    }

    private async Task LoginAsync(AililiAccount account, CancellationToken cancellationToken)
    {
        var login = await SendAsync<JsonElement>(HttpMethod.Post, "/api/user/login", new
        {
            username = account.Username,
            password = account.Password
        }, null, cancellationToken);

        var accessToken = GetAccessToken(login);
        var userId = GetUserId(login);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("Ailili 登录成功，但未返回用户 ID，无法创建分组 Key。");
        }

        _accessToken = accessToken;
        _userId = userId;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true
        };
        return new HttpClient(handler);
    }

    private static string? GetAccessToken(JsonElement login)
    {
        if (login.ValueKind == JsonValueKind.String)
        {
            return login.GetString();
        }

        if (login.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in new[] { "access_token", "accessToken", "token" })
        {
            if (login.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static string? GetUserId(JsonElement login)
    {
        if (login.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in new[] { "id", "user_id", "userId" })
        {
            if (!login.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var userId))
            {
                return userId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private async Task CreateTokenAsync(string name, string group, string? accessToken, CancellationToken cancellationToken)
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

    private async Task<string> GetTokenKeyAsync(int id, string? accessToken, CancellationToken cancellationToken)
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
        if (!string.IsNullOrWhiteSpace(_userId))
        {
            request.Headers.TryAddWithoutValidation("New-Api-User", _userId);
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
    private sealed record TokenPage([property: JsonPropertyName("items")] TokenItem[] Items);
    private sealed record TokenItem(int Id, string Name);
    private sealed record TokenKeyData(string Key);
}