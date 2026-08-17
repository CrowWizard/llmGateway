using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public sealed record OAuthAuthorizationRequest(
    string ClientId,
    string RedirectUri,
    string? State,
    string? CodeChallenge,
    string? CodeChallengeMethod,
    string? Scope);

public sealed record OAuthAuthorizationResult(string Code, string? State, string RedirectUri);

public sealed record OAuthTokenResult(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    string Scope,
    string? IdToken = null,
    string? AccountId = null);

public sealed record DeviceAuthorizationResult(
    string DeviceAuthId,
    string UserCode,
    string VerificationUri,
    int ExpiresIn,
    int Interval);

public sealed record DeviceAuthorizationCodeResult(
    string AuthorizationCode,
    string CodeChallenge,
    string CodeVerifier);

public sealed class OAuthCompatibilityStore
{
    private const int AuthorizationCodeLifetimeSeconds = 300;
    private const int AccessTokenLifetimeSeconds = 3600;
    private const int DeviceAuthorizationLifetimeSeconds = 900;
    private const int DeviceAuthorizationPollingIntervalSeconds = 5;
    private readonly ConcurrentDictionary<string, AuthorizationCode> _codes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RefreshGrant> _refreshTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AccessGrant> _accessTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DeviceAuthorization> _deviceAuthorizations = new(StringComparer.Ordinal);

    public OAuthAuthorizationResult Authorize(OAuthAuthorizationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId) || !Uri.TryCreate(request.RedirectUri, UriKind.Absolute, out _))
        {
            throw new OAuthProtocolException("invalid_request", "client_id 和 redirect_uri 是必需的。", 400);
        }

        if (!string.IsNullOrWhiteSpace(request.CodeChallenge)
            && !string.Equals(request.CodeChallengeMethod, "S256", StringComparison.OrdinalIgnoreCase))
        {
            throw new OAuthProtocolException("invalid_request", "仅支持 PKCE S256。", 400);
        }

        var code = CreateToken();
        _codes[code] = new AuthorizationCode(
            request.ClientId,
            request.RedirectUri,
            request.CodeChallenge,
            DateTimeOffset.UtcNow.AddSeconds(AuthorizationCodeLifetimeSeconds));
        return new OAuthAuthorizationResult(code, request.State, request.RedirectUri);
    }

    public OAuthTokenResult Exchange(string? code, string? clientId, string? redirectUri, string? codeVerifier, string? scope)
    {
        if (string.IsNullOrWhiteSpace(code) || !_codes.TryRemove(code, out var authorizationCode))
        {
            throw new OAuthProtocolException("invalid_grant", "授权码无效或已过期。", 400);
        }

        if (authorizationCode.ExpiresAt <= DateTimeOffset.UtcNow
            || !string.Equals(authorizationCode.ClientId, clientId, StringComparison.Ordinal)
            || !string.Equals(authorizationCode.RedirectUri, redirectUri, StringComparison.Ordinal)
            || !IsCodeVerifierValid(authorizationCode.CodeChallenge, codeVerifier))
        {
            throw new OAuthProtocolException("invalid_grant", "授权码验证失败。", 400);
        }

        return IssueTokens(scope, authorizationCode.ClientId);
    }

    public OAuthTokenResult Refresh(string? refreshToken, string? clientId, string? scope)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)
            || !_refreshTokens.TryRemove(refreshToken, out var grant)
            || grant.ExpiresAt <= DateTimeOffset.UtcNow
            || (!string.IsNullOrWhiteSpace(clientId) && !string.Equals(grant.ClientId, clientId, StringComparison.Ordinal)))
        {
            throw new OAuthProtocolException("invalid_grant", "refresh_token 无效或已过期。", 400);
        }

        return IssueTokens(scope ?? grant.Scope, grant.ClientId);
    }

    public DeviceAuthorizationResult StartDeviceAuthorization(string? clientId, string? scope, string verificationUri)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new OAuthProtocolException("invalid_request", "client_id 是必需的。", 400);
        }

        var deviceAuthId = CreateToken();
        var userCode = CreateUserCode();
        var codeVerifier = CreateToken();
        _deviceAuthorizations[deviceAuthId] = new DeviceAuthorization(
            clientId,
            userCode,
            string.IsNullOrWhiteSpace(scope) ? "openid profile email" : scope.Trim(),
            DateTimeOffset.UtcNow.AddSeconds(DeviceAuthorizationLifetimeSeconds),
            codeVerifier,
            CreateCodeChallenge(codeVerifier));
        return new DeviceAuthorizationResult(
            deviceAuthId,
            userCode,
            verificationUri,
            DeviceAuthorizationLifetimeSeconds,
            DeviceAuthorizationPollingIntervalSeconds);
    }

    public void ApproveDeviceAuthorization(string? userCode)
    {
        var normalizedUserCode = NormalizeUserCode(userCode);
        var authorization = _deviceAuthorizations.FirstOrDefault(pair =>
            string.Equals(pair.Value.UserCode, normalizedUserCode, StringComparison.Ordinal));
        if (string.IsNullOrEmpty(authorization.Key) || authorization.Value.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new OAuthProtocolException("invalid_user_code", "用户码无效或已过期。", 400);
        }

        _deviceAuthorizations.TryUpdate(authorization.Key, authorization.Value with { IsApproved = true }, authorization.Value);
    }

    public OAuthTokenResult CompleteDeviceAuthorization(string? deviceAuthId)
    {
        if (string.IsNullOrWhiteSpace(deviceAuthId)
            || !_deviceAuthorizations.TryGetValue(deviceAuthId, out var authorization))
        {
            throw new OAuthProtocolException("invalid_grant", "设备授权码无效。", 400);
        }

        if (authorization.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _deviceAuthorizations.TryRemove(deviceAuthId, out _);
            throw new OAuthProtocolException("expired_token", "设备授权已过期。", 400);
        }

        if (!authorization.IsApproved)
        {
            throw new OAuthProtocolException("authorization_pending", "等待用户完成授权。", 400);
        }

        if (!_deviceAuthorizations.TryRemove(deviceAuthId, out authorization))
        {
            throw new OAuthProtocolException("invalid_grant", "设备授权码已被使用。", 400);
        }

        return IssueTokens(authorization.Scope, authorization.ClientId);
    }

    public DeviceAuthorizationCodeResult CompleteDeviceAuthorizationCode(string? deviceAuthId, string? userCode, string redirectUri)
    {
        if (string.IsNullOrWhiteSpace(deviceAuthId)
            || !_deviceAuthorizations.TryGetValue(deviceAuthId, out var authorization)
            || !string.Equals(authorization.UserCode, NormalizeUserCode(userCode), StringComparison.Ordinal))
        {
            throw new OAuthProtocolException("invalid_grant", "设备授权码无效。", 400);
        }

        if (authorization.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _deviceAuthorizations.TryRemove(deviceAuthId, out _);
            throw new OAuthProtocolException("expired_token", "设备授权已过期。", 400);
        }

        if (!authorization.IsApproved)
        {
            throw new OAuthProtocolException("authorization_pending", "等待用户完成授权。", 404);
        }

        if (!_deviceAuthorizations.TryRemove(deviceAuthId, out authorization))
        {
            throw new OAuthProtocolException("invalid_grant", "设备授权码已被使用。", 400);
        }

        var authorizationCode = CreateToken();
        _codes[authorizationCode] = new AuthorizationCode(
            authorization.ClientId,
            redirectUri,
            authorization.CodeChallenge,
            DateTimeOffset.UtcNow.AddSeconds(AuthorizationCodeLifetimeSeconds));
        return new DeviceAuthorizationCodeResult(authorizationCode, authorization.CodeChallenge, authorization.CodeVerifier);
    }

    public bool Revoke(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        _accessTokens.TryRemove(token, out _);
        _refreshTokens.TryRemove(token, out _);
        return true;
    }

    public bool IsAccessTokenValid(string token) =>
        _accessTokens.TryGetValue(token, out var grant) && grant.ExpiresAt > DateTimeOffset.UtcNow;

    private OAuthTokenResult IssueTokens(string? scope, string? clientId = null)
    {
        var accessToken = CreateToken();
        var refreshToken = CreateToken();
        var normalizedScope = string.IsNullOrWhiteSpace(scope) ? "openid profile email" : scope.Trim();
        _accessTokens[accessToken] = new AccessGrant(DateTimeOffset.UtcNow.AddSeconds(AccessTokenLifetimeSeconds));
        _refreshTokens[refreshToken] = new RefreshGrant(
            clientId ?? "local-client",
            normalizedScope,
            DateTimeOffset.UtcNow.AddDays(30));
        return new OAuthTokenResult(
            accessToken,
            refreshToken,
            "Bearer",
            AccessTokenLifetimeSeconds,
            normalizedScope,
            CreateLocalIdToken(clientId));
    }

    private static bool IsCodeVerifierValid(string? challenge, string? verifier)
    {
        if (string.IsNullOrWhiteSpace(challenge))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(verifier))
        {
            return false;
        }

        var calculated = CreateCodeChallenge(verifier);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(calculated),
            Encoding.ASCII.GetBytes(challenge));
    }

    private static string CreateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static string CreateCodeChallenge(string verifier)
    {
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string CreateLocalIdToken(string? clientId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "none", typ = "JWT" }));
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            sub = string.IsNullOrWhiteSpace(clientId) ? "local-client" : clientId,
            iss = "llm-gateway",
            iat = now,
            exp = now + AccessTokenLifetimeSeconds
        }));
        return $"{header}.{payload}.local-gateway-signature";
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string CreateUserCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(8);
        var code = new char[8];
        for (var index = 0; index < code.Length; index++)
        {
            code[index] = alphabet[bytes[index] % alphabet.Length];
        }

        return string.Concat(code.AsSpan(0, 4), "-", code.AsSpan(4, 4));
    }

    private static string NormalizeUserCode(string? userCode) =>
        (userCode ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);

    private sealed record AuthorizationCode(string ClientId, string RedirectUri, string? CodeChallenge, DateTimeOffset ExpiresAt);
    private sealed record RefreshGrant(string ClientId, string Scope, DateTimeOffset ExpiresAt);
    private sealed record AccessGrant(DateTimeOffset ExpiresAt);
    private sealed record DeviceAuthorization(
        string ClientId,
        string UserCode,
        string Scope,
        DateTimeOffset ExpiresAt,
        string CodeVerifier,
        string CodeChallenge,
        bool IsApproved = false);
}

public sealed class OAuthProtocolException(string error, string description, int statusCode) : Exception(description)
{
    public string Error { get; } = error;
    public int StatusCode { get; } = statusCode;
}