using System.Net;
using System.Text;
using System.Text.Json;
using LlmGateway.Desktop.Services;
using Xunit;

namespace LlmGateway.Tests;

public sealed class AililiAccountServiceTests
{
    [Fact]
    public async Task RegistrationAndTokenCreationAreSeparateAndSaveCredentials()
    {
        var root = Path.Combine(Path.GetTempPath(), $"llm-gateway-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var handler = new AililiHandler();
            var paths = new AppPaths(root, root);
            var service = new AililiAccountService(paths, new HttpClient(handler));

            var registered = await service.RegisterAccountAsync(TestContext.Current.CancellationToken);

            Assert.StartsWith("codex_", registered.Username);
            Assert.Equal(18, registered.Password.Length);
            Assert.Empty(registered.CodexKey);
            Assert.Empty(registered.ImageKey);
            Assert.Equal(2, handler.RequestCount);

            var account = await service.CreateTokensAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("sk-codex-key", account.CodexKey);
            Assert.Equal("sk-image-key", account.ImageKey);
            Assert.Equal(8, handler.RequestCount);
            Assert.True(File.Exists(paths.AililiCredentialsPath));
            var saved = service.Load();
            Assert.Equal(account, saved);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private sealed class AililiHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var path = request.RequestUri!.PathAndQuery;
            if (path == "/api/user/register")
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var payload = JsonDocument.Parse(body);
                Assert.Equal(payload.RootElement.GetProperty("password").GetString(), payload.RootElement.GetProperty("password2").GetString());
                Assert.Equal(string.Empty, payload.RootElement.GetProperty("aff_code").GetString());
                return Json("{\"success\":true,\"message\":\"\"}");
            }
            if (path == "/api/user/login")
            {
                return Json("{\"success\":true,\"message\":\"\",\"data\":{\"access_token\":\"access-token\"}}");
            }

            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("access-token", request.Headers.Authorization?.Parameter);
            if (request.Method == HttpMethod.Post && path == "/api/token/")
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Assert.Contains("\"unlimited_quota\":true", body);
                Assert.Contains(RequestCount == 4
                    ? "\"group\":\"OpenAI-gpt\""
                    : "\"group\":\"gpt-image-2\"", body);
                return Json("{\"success\":true,\"message\":\"\"}");
            }
            if (request.Method == HttpMethod.Get && path == "/api/token/?p=0&page_size=20")
            {
                return RequestCount == 3
                    ? Json("{\"success\":true,\"message\":\"\",\"data\":{\"items\":[]}}")
                    : Json("{\"success\":true,\"message\":\"\",\"data\":{\"items\":[{\"id\":11,\"name\":\"Codex\"},{\"id\":12,\"name\":\"生图\"}]}}");
            }
            if (path == "/api/token/11/key")
            {
                return Json("{\"success\":true,\"message\":\"\",\"data\":{\"key\":\"codex-key\"}}");
            }
            if (path == "/api/token/12/key")
            {
                return Json("{\"success\":true,\"message\":\"\",\"data\":{\"key\":\"sk-image-key\"}}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }
}