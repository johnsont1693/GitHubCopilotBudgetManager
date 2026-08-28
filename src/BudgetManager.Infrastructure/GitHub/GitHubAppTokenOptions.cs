namespace BudgetManager.Infrastructure.GitHub;

public sealed record GitHubAppTokenOptions
{
    public GitHubAppTokenOptions(
        string issuer,
        long installationId,
        TimeSpan? tokenRefreshSkew = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(installationId);

        var refreshSkew = tokenRefreshSkew ?? TimeSpan.FromMinutes(5);
        ArgumentOutOfRangeException.ThrowIfLessThan(refreshSkew, TimeSpan.Zero);

        Issuer = issuer;
        InstallationId = installationId;
        TokenRefreshSkew = refreshSkew;
    }

    public string Issuer { get; }

    public long InstallationId { get; }

    public TimeSpan TokenRefreshSkew { get; }
}
