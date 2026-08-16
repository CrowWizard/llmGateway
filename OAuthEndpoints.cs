using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;

public static class OAuthEndpoints
{
    public static void Map(WebApplication app, OAuthCompatibilityStore store, string issuer)
    {
        app.MapGet("/oauth/authorize", (HttpContext context) =>
        {
            var query = context.Request.Query;
            OAuthAuthorizationResult result;
            try
            {
                result = store.Authorize(new OAuthAuthorizationRequest(
                    query["client_id"].ToString(),
                    query["redirect_uri"].ToString(),
                    query["state"].ToString(),
                    query["code_challenge"].ToString(),
                    query["code_challenge_method"].ToString(),
                    query["scope"].ToString()));
            }
            catch (OAuthProtocolException exception)
            {
                return Results.Json(new { error = exception.Error, error_description = exception.Message }, statusCode: exception.StatusCode);
            }

            var redirect = new UriBuilder(result.RedirectUri);
            var parameters = QueryHelpers.ParseQuery(redirect.Query)
                .ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal);
            parameters["code"] = result.Code;
            if (!string.IsNullOrWhiteSpace(result.State))
            {
                parameters["state"] = result.State;
            }

            redirect.Query = QueryString.Create(parameters.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))).ToUriComponent();
            return Results.Redirect(redirect.Uri.ToString());
        });

        app.MapPost("/oauth/token", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            try
            {
                var grantType = form["grant_type"].ToString();
                OAuthTokenResult result = grantType switch
                {
                    "authorization_code" => store.Exchange(
                        form["code"].ToString(),
                        form["client_id"].ToString(),
                        form["redirect_uri"].ToString(),
                        form["code_verifier"].ToString(),
                        form["scope"].ToString()),
                    "refresh_token" => store.Refresh(
                        form["refresh_token"].ToString(),
                        form["client_id"].ToString(),
                        form["scope"].ToString()),
                    _ => throw new OAuthProtocolException("unsupported_grant_type", "仅支持 authorization_code 和 refresh_token。", 400)
                };

                return Results.Json(new
                {
                    access_token = result.AccessToken,
                    refresh_token = result.RefreshToken,
                    token_type = result.TokenType,
                    expires_in = result.ExpiresIn,
                    scope = result.Scope,
                    id_token = result.IdToken,
                    account_id = result.AccountId
                });
            }
            catch (OAuthProtocolException exception)
            {
                return Results.Json(new { error = exception.Error, error_description = exception.Message }, statusCode: exception.StatusCode);
            }
        });

        app.MapPost("/oauth/revoke", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            store.Revoke(form["token"].ToString());
            return Results.StatusCode(StatusCodes.Status200OK);
        });

        app.MapGet("/.well-known/openid-configuration", () => Results.Json(new
        {
            issuer,
            authorization_endpoint = issuer + "/oauth/authorize",
            token_endpoint = issuer + "/oauth/token",
            revocation_endpoint = issuer + "/oauth/revoke",
            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code", "refresh_token" },
            code_challenge_methods_supported = new[] { "S256" },
            token_endpoint_auth_methods_supported = new[] { "none", "client_secret_post" }
        }));
    }
}