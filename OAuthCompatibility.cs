using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

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

public sealed class OAuthCompatibilityStore
{
    private const int AuthorizationCodeLifetimeSeconds = 300;
    private const int AccessTokenLifetimeSeconds = 3600;
    private readonly ConcurrentDictionary<string, AuthorizationCode> _codes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RefreshGrant> _refreshTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AccessGrant> _accessTokens = new(StringComparer.Ordinal);

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
        return new OAuthTokenResult(accessToken, refreshToken, "Bearer", AccessTokenLifetimeSeconds, normalizedScope);
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

        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var calculated = Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(calculated),
            Encoding.ASCII.GetBytes(challenge));
    }

    private static string CreateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private sealed record AuthorizationCode(string ClientId, string RedirectUri, string? CodeChallenge, DateTimeOffset ExpiresAt);
    private sealed record RefreshGrant(string ClientId, string Scope, DateTimeOffset ExpiresAt);
    private sealed record AccessGrant(DateTimeOffset ExpiresAt);
}

public sealed class OAuthProtocolException(string error, string description, int statusCode) : Exception(description)
{
    public string Error { get; } = error;
    public int StatusCode { get; } = statusCode;
}