using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BudgetManager.Application.GitHub;

namespace BudgetManager.Infrastructure.GitHub;

public sealed class GitHubAppAccessTokenProvider : IGitHubAccessTokenProvider, IDisposable
{
    private const string AcceptMediaType = "application/vnd.github+json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly IGitHubAppPrivateKeyProvider privateKeyProvider;
    private readonly GitHubAppTokenOptions tokenOptions;
    private readonly GitHubApiOptions apiOptions;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private CachedToken? cachedToken;
    private bool disposed;

    public GitHubAppAccessTokenProvider(
        HttpClient httpClient,
        IGitHubAppPrivateKeyProvider privateKeyProvider,
        GitHubAppTokenOptions tokenOptions,
        GitHubApiOptions? apiOptions = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(privateKeyProvider);
        ArgumentNullException.ThrowIfNull(tokenOptions);

        this.httpClient = httpClient;
        this.privateKeyProvider = privateKeyProvider;
        this.tokenOptions = tokenOptions;
        this.apiOptions = apiOptions ?? new GitHubApiOptions();
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var now = timeProvider.GetUtcNow();
        if (IsUsable(cachedToken, now))
        {
            return cachedToken!.Value;
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            now = timeProvider.GetUtcNow();
            if (IsUsable(cachedToken, now))
            {
                return cachedToken!.Value;
            }

            cachedToken = await RequestInstallationTokenAsync(now, cancellationToken);
            return cachedToken.Value;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        refreshLock.Dispose();
        disposed = true;
    }

    private bool IsUsable(CachedToken? token, DateTimeOffset now) =>
        token is not null && token.ExpiresAt - tokenOptions.TokenRefreshSkew > now;

    private async Task<CachedToken> RequestInstallationTokenAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var appJwt = await CreateAppJwtAsync(now, cancellationToken);
        var path = $"app/installations/{tokenOptions.InstallationId}/access_tokens";
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(apiOptions.BaseAddress, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(AcceptMediaType));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appJwt);
        request.Headers.Add("X-GitHub-Api-Version", apiOptions.ApiVersion);
        request.Headers.UserAgent.ParseAdd("github-copilot-budget-manager/0.1");
        request.Content = JsonContent.Create(new { }, options: JsonOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubApiException(
                $"GitHub App installation token exchange returned HTTP {(int)response.StatusCode}.",
                response.StatusCode,
                response.Headers.TryGetValues("X-GitHub-Request-Id", out var requestIds)
                    ? requestIds.FirstOrDefault()
                    : null);
        }

        var token = await response.Content.ReadFromJsonAsync<InstallationTokenResponse>(
            JsonOptions,
            cancellationToken);
        if (token is null || string.IsNullOrWhiteSpace(token.Token))
        {
            throw new GitHubApiException(
                "GitHub returned an invalid installation token response.",
                response.StatusCode,
                null);
        }

        return new CachedToken(token.Token, token.ExpiresAt);
    }

    private async Task<string> CreateAppJwtAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var privateKeyPem = await privateKeyProvider.GetPrivateKeyPemAsync(cancellationToken);
        var issuedAt = now.AddSeconds(-60).ToUnixTimeSeconds();
        var expiresAt = now.AddMinutes(9).ToUnixTimeSeconds();
        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "RS256",
            typ = "JWT",
        }));
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iat = issuedAt,
            exp = expiresAt,
            iss = tokenOptions.Issuer,
        }));
        var signingInput = Encoding.ASCII.GetBytes($"{header}.{payload}");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var signature = rsa.SignData(
            signingInput,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{header}.{payload}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed record CachedToken(string Value, DateTimeOffset ExpiresAt);

    private sealed record InstallationTokenResponse(
        string Token,
        [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);
}
