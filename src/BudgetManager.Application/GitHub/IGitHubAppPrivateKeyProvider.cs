namespace BudgetManager.Application.GitHub;

public interface IGitHubAppPrivateKeyProvider
{
    ValueTask<string> GetPrivateKeyPemAsync(CancellationToken cancellationToken = default);
}
