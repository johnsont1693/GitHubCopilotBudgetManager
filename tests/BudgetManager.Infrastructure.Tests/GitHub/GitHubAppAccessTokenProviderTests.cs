using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BudgetManager.Application.GitHub;
using BudgetManager.Infrastructure.GitHub;

namespace BudgetManager.Infrastructure.Tests.GitHub;

public sealed class GitHubAppAccessTokenProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAccessTokenAsync_signs_the_app_jwt_and_exchanges_it()
    {
        using var rsa = RSA.Create(2048);
        var privateKey = rsa.ExportRSAPrivateKeyPem();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                "https://api.github.test/app/installations/42/access_tokens",
                request.RequestUri?.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("2026-03-10", Assert.Single(request.Headers.GetValues("X-GitHub-Api-Version")));

            var jwt = request.Headers.Authorization!.Parameter!;
            var parts = jwt.Split('.');
            Assert.Equal(3, parts.Length);
            using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            Assert.Equal("github-app-client-id", payload.RootElement.GetProperty("iss").GetString());
            Assert.Equal(Now.AddSeconds(-60).ToUnixTimeSeconds(), payload.RootElement.GetProperty("iat").GetInt64());
            Assert.Equal(Now.AddMinutes(9).ToUnixTimeSeconds(), payload.RootElement.GetProperty("exp").GetInt64());

            var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            Assert.True(rsa.VerifyData(
                signingInput,
                Base64UrlDecode(parts[2]),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1));

            await Task.CompletedTask;
            return JsonResponse(HttpStatusCode.Created, $$"""
                {
                  "token": "installation-token",
                  "expires_at": "{{Now.AddHours(1):O}}"
                }
                """);
        }));
        using var provider = new GitHubAppAccessTokenProvider(
            httpClient,
            new FixedPrivateKeyProvider(privateKey),
            new GitHubAppTokenOptions("github-app-client-id", 42),
            new GitHubApiOptions(new Uri("https://api.github.test/")),
            new FixedTimeProvider(Now));

        var token = await provider.GetAccessTokenAsync();

        Assert.Equal("installation-token", token);
    }

    [Fact]
    public async Task GetAccessTokenAsync_reuses_a_token_outside_the_refresh_window()
    {
        var requestCount = 0;
        using var rsa = RSA.Create(2048);
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestCount++;
            return Task.FromResult(JsonResponse(HttpStatusCode.Created, $$"""
                {
                  "token": "installation-token",
                  "expires_at": "{{Now.AddHours(1):O}}"
                }
                """));
        }));
        using var provider = new GitHubAppAccessTokenProvider(
            httpClient,
            new FixedPrivateKeyProvider(rsa.ExportRSAPrivateKeyPem()),
            new GitHubAppTokenOptions("github-app-client-id", 42),
            new GitHubApiOptions(new Uri("https://api.github.test/")),
            new FixedTimeProvider(Now));

        var first = await provider.GetAccessTokenAsync();
        var second = await provider.GetAccessTokenAsync();

        Assert.Equal(first, second);
        Assert.Equal(1, requestCount);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class FixedPrivateKeyProvider(string privateKey) : IGitHubAppPrivateKeyProvider
    {
        public ValueTask<string> GetPrivateKeyPemAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(privateKey);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }
}
