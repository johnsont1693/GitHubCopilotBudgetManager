namespace BudgetManager.Application.Identity;

public sealed record EntraUserProfile(
    string ObjectId,
    string? UserPrincipalName,
    string? Department,
    string? CostCenterCode,
    string? GitHubLogin);

public sealed record GitHubIdentityCandidate(string GitHubLogin, string? GitHubCostCenterId);

public sealed record IdentityMapping(
    string GitHubLogin,
    string? EntraObjectId,
    string? UserPrincipalName,
    string? Department,
    string? EntraCostCenterCode,
    string? GitHubCostCenterId,
    string Status,
    string Source);

public sealed record IdentitySynchronizationResult(
    int CandidateCount,
    int MatchedCount,
    int UnmatchedCount,
    int DuplicateCount);

public interface IEntraDirectoryClient
{
    Task<IReadOnlyList<EntraUserProfile>> GetUsersAsync(
        CancellationToken cancellationToken = default);
}

public interface IIdentityMappingStore
{
    Task<IReadOnlyList<GitHubIdentityCandidate>> GetCandidatesAsync(
        Guid enterpriseId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        Guid enterpriseId,
        IReadOnlyList<IdentityMapping> mappings,
        DateTimeOffset synchronizedAt,
        CancellationToken cancellationToken = default);
}

public sealed class IdentitySynchronizationService
{
    private readonly IEntraDirectoryClient directoryClient;
    private readonly IIdentityMappingStore mappingStore;
    private readonly TimeProvider timeProvider;

    public IdentitySynchronizationService(
        IEntraDirectoryClient directoryClient,
        IIdentityMappingStore mappingStore,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(directoryClient);
        ArgumentNullException.ThrowIfNull(mappingStore);
        this.directoryClient = directoryClient;
        this.mappingStore = mappingStore;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IdentitySynchronizationResult> SynchronizeAsync(
        Guid enterpriseId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(enterpriseId, Guid.Empty);
        var candidates = await mappingStore.GetCandidatesAsync(enterpriseId, cancellationToken);
        var users = await directoryClient.GetUsersAsync(cancellationToken);
        var usersByLogin = users
            .Where(user => !string.IsNullOrWhiteSpace(user.GitHubLogin))
            .GroupBy(user => user.GitHubLogin!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var mappings = new List<IdentityMapping>(candidates.Count);
        var matched = 0;
        var unmatched = 0;
        var duplicate = 0;

        foreach (var candidate in candidates)
        {
            if (!usersByLogin.TryGetValue(candidate.GitHubLogin, out var matches))
            {
                mappings.Add(new IdentityMapping(
                    candidate.GitHubLogin,
                    null,
                    null,
                    null,
                    null,
                    candidate.GitHubCostCenterId,
                    "Unmatched",
                    "entra-attribute"));
                unmatched++;
                continue;
            }

            if (matches.Length > 1)
            {
                mappings.Add(new IdentityMapping(
                    candidate.GitHubLogin,
                    null,
                    null,
                    null,
                    null,
                    candidate.GitHubCostCenterId,
                    "Duplicate",
                    "entra-attribute"));
                duplicate++;
                continue;
            }

            var user = matches[0];
            mappings.Add(new IdentityMapping(
                candidate.GitHubLogin,
                user.ObjectId,
                user.UserPrincipalName,
                user.Department,
                user.CostCenterCode,
                candidate.GitHubCostCenterId,
                "Matched",
                "entra-attribute"));
            matched++;
        }

        await mappingStore.SaveAsync(
            enterpriseId,
            mappings.AsReadOnly(),
            timeProvider.GetUtcNow(),
            cancellationToken);
        return new IdentitySynchronizationResult(candidates.Count, matched, unmatched, duplicate);
    }
}
