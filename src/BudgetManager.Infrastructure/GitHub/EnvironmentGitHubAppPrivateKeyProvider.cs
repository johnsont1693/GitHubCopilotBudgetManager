using BudgetManager.Application.GitHub;

namespace BudgetManager.Infrastructure.GitHub;

public sealed class EnvironmentGitHubAppPrivateKeyProvider : IGitHubAppPrivateKeyProvider
{
    public const string DefaultEnvironmentVariable = "GITHUB_APP_PRIVATE_KEY";

    private readonly string environmentVariable;

    public EnvironmentGitHubAppPrivateKeyProvider(string environmentVariable = DefaultEnvironmentVariable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariable);
        this.environmentVariable = environmentVariable;
    }

    public ValueTask<string> GetPrivateKeyPemAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var privateKey = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            throw new InvalidOperationException(
                $"GitHub App private key environment variable '{environmentVariable}' is not configured.");
        }

        return ValueTask.FromResult(privateKey.Replace("\\n", "\n", StringComparison.Ordinal));
    }
}
