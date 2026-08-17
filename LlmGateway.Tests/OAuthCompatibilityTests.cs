using System.Security.Cryptography;
using System.Text;
using Xunit;

public sealed class OAuthCompatibilityTests
{
    [Fact]
    public void ExchangesPkceAuthorizationCodeOnlyOnce()
    {
        var store = new OAuthCompatibilityStore();
        const string verifier = "test-pkce-verifier";
        var authorization = store.Authorize(new OAuthAuthorizationRequest(
            "codex-client",
            "http://127.0.0.1:1455/auth/callback",
            "state-value",
            CreateChallenge(verifier),
            "S256",
            "openid profile"));

        var token = store.Exchange(
            authorization.Code,
            "codex-client",
            "http://127.0.0.1:1455/auth/callback",
            verifier,
            null);

        Assert.Equal("state-value", authorization.State);
        Assert.True(store.IsAccessTokenValid(token.AccessToken));
        Assert.Throws<OAuthProtocolException>(() => store.Exchange(
            authorization.Code,
            "codex-client",
            "http://127.0.0.1:1455/auth/callback",
            verifier,
            null));
    }

    [Fact]
    public void RefreshRotatesTokenAndRevokeInvalidatesAccess()
    {
        var store = new OAuthCompatibilityStore();
        var authorization = store.Authorize(new OAuthAuthorizationRequest(
            "codex-client",
            "http://127.0.0.1:1455/auth/callback",
            null,
            null,
            null,
            "openid"));
        var initial = store.Exchange(
            authorization.Code,
            "codex-client",
            "http://127.0.0.1:1455/auth/callback",
            null,
            null);

        var refreshed = store.Refresh(initial.RefreshToken, "codex-client", null);
        store.Revoke(refreshed.AccessToken);

        Assert.NotEqual(initial.RefreshToken, refreshed.RefreshToken);
        Assert.False(store.IsAccessTokenValid(refreshed.AccessToken));
        Assert.Throws<OAuthProtocolException>(() => store.Refresh(initial.RefreshToken, "codex-client", null));
    }

    [Fact]
    public void DeviceAuthorizationWaitsForApprovalThenIssuesTokensOnce()
    {
        var store = new OAuthCompatibilityStore();
        var device = store.StartDeviceAuthorization("codex-client", "openid", "http://127.0.0.1:23002/deviceauth/verify");

        var pending = Assert.Throws<OAuthProtocolException>(() => store.CompleteDeviceAuthorization(device.DeviceAuthId));
        Assert.Equal("authorization_pending", pending.Error);

        store.ApproveDeviceAuthorization(device.UserCode);
        var token = store.CompleteDeviceAuthorization(device.DeviceAuthId);

        Assert.True(store.IsAccessTokenValid(token.AccessToken));
        Assert.Throws<OAuthProtocolException>(() => store.CompleteDeviceAuthorization(device.DeviceAuthId));
    }

    private static string CreateChallenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}