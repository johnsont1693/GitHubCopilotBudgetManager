namespace BudgetManager.Application.GitHub;

public interface IGitHubAccessTokenProvider
{
    ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
