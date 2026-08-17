using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using System.Text.Json;

public static class OAuthEndpoints
{
    public static void Map(WebApplication app, OAuthCompatibilityStore store, string issuer)
    {
        app.MapPost("/deviceauth/usercode", async (HttpContext context) =>
        {
            var values = await ReadValuesAsync(context);
            try
            {
                var verificationUri = issuer + "/deviceauth/verify";
                var result = store.StartDeviceAuthorization(
                    values.GetValueOrDefault("client_id"),
                    values.GetValueOrDefault("scope"),
                    verificationUri);
                var verificationUriComplete = QueryHelpers.AddQueryString(verificationUri, "user_code", result.UserCode);
                return Results.Json(new
                {
                    device_auth_id = result.DeviceAuthId,
                    device_code = result.DeviceAuthId,
                    user_code = result.UserCode,
                    verification_uri = result.VerificationUri,
                    verification_uri_complete = verificationUriComplete,
                    expires_in = result.ExpiresIn,
                    interval = result.Interval
                });
            }
            catch (OAuthProtocolException exception)
            {
                return Error(exception);
            }
        });

        app.MapPost("/deviceauth/token", async (HttpContext context) =>
        {
            var values = await ReadValuesAsync(context);
            try
            {
                var result = store.CompleteDeviceAuthorization(
                    values.GetValueOrDefault("device_auth_id") ?? values.GetValueOrDefault("device_code"));
                return Token(result);
            }
            catch (OAuthProtocolException exception)
            {
                return Error(exception);
            }
        });

        app.MapGet("/deviceauth/verify", (HttpContext context) =>
        {
            var userCode = System.Net.WebUtility.HtmlEncode(context.Request.Query["user_code"].ToString());
            return Results.Content($"""
                <!doctype html>
                <html lang="zh-CN"><head><meta charset="utf-8"><title>确认设备授权</title></head>
                <body><main><h1>确认设备授权</h1><form method="post">
                <label>用户码 <input name="user_code" value="{userCode}" autocomplete="one-time-code" required></label>
                <button type="submit">确认授权</button></form></main></body></html>
                """, "text/html; charset=utf-8");
        });

        app.MapPost("/deviceauth/verify", async (HttpContext context) =>
        {
            var values = await ReadValuesAsync(context);
            try
            {
                store.ApproveDeviceAuthorization(values.GetValueOrDefault("user_code"));
                return Results.Content("<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><title>授权完成</title><p>设备已授权。你可以返回命令行。</p></html>", "text/html; charset=utf-8");
            }
            catch (OAuthProtocolException exception)
            {
                return Error(exception);
            }
        });

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
                return Error(exception);
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

                return Token(result);
            }
            catch (OAuthProtocolException exception)
            {
                return Error(exception);
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
            device_authorization_endpoint = issuer + "/deviceauth/usercode",
            response_types_supported = new[] { "code" },
            grant_types_supported = new[]
            {
                "authorization_code",
                "refresh_token",
                "urn:ietf:params:oauth:grant-type:device_code"
            },
            code_challenge_methods_supported = new[] { "S256" },
            token_endpoint_auth_methods_supported = new[] { "none", "client_secret_post" }
        }));
    }

    private static IResult Token(OAuthTokenResult result) => Results.Json(new
    {
        access_token = result.AccessToken,
        refresh_token = result.RefreshToken,
        token_type = result.TokenType,
        expires_in = result.ExpiresIn,
        scope = result.Scope,
        id_token = result.IdToken,
        account_id = result.AccountId
    });

    private static IResult Error(OAuthProtocolException exception) =>
        Results.Json(new { error = exception.Error, error_description = exception.Message }, statusCode: exception.StatusCode);

    private static async Task<Dictionary<string, string>> ReadValuesAsync(HttpContext context)
    {
        if (context.Request.HasFormContentType)
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            return form.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal);
        }

        using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new OAuthProtocolException("invalid_request", "请求体必须是 JSON 对象或表单。", 400);
        }

        return document.RootElement.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }
}