using System.Net;
using System.Text;
using Azure;
using Azure.Core;
using BudgetManager.Application.Identity;
using BudgetManager.Infrastructure.Identity;

namespace BudgetManager.Infrastructure.Tests.Identity;

public sealed class IdentitySynchronizationTests
{
    [Fact]
    public async Task Graph_client_maps_configured_attribute_and_follows_continuation()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            JsonResponse("""
                {"value":[{"id":"1","userPrincipalName":"ada@contoso.com","department":"Engineering","employeeOrgData":{"costCenter":"CC-1"},"extension_githubLogin":"ada-l"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/users?$skiptoken=next"}
                """),
            JsonResponse("""
                {"value":[{"id":"2","userPrincipalName":"grace@contoso.com","department":"Research","extension_githubLogin":"grace-h"}]}
                """),
        ]);
        using var client = new HttpClient(new QueueHandler(responses));
        var directory = new GraphDirectoryClient(
            client,
            new FixedCredential(),
            new GraphDirectoryOptions("extension_githubLogin"));

        var users = await directory.GetUsersAsync();

        Assert.Equal(2, users.Count);
        Assert.Equal("CC-1", users[0].CostCenterCode);
        Assert.Equal("grace-h", users[1].GitHubLogin);
    }

    [Fact]
    public async Task Synchronization_marks_matched_unmatched_and_duplicate_logins()
    {
        var store = new RecordingStore([
            new GitHubIdentityCandidate("ada-l", "cc-1"),
            new GitHubIdentityCandidate("missing", null),
            new GitHubIdentityCandidate("duplicate", null),
        ]);
        var directory = new FixedDirectory([
            new EntraUserProfile("1", "ada@contoso.com", "Engineering", "CC-1", "ada-l"),
            new EntraUserProfile("2", "one@contoso.com", null, null, "duplicate"),
            new EntraUserProfile("3", "two@contoso.com", null, null, "duplicate"),
        ]);
        var service = new IdentitySynchronizationService(directory, store);

        var result = await service.SynchronizeAsync(Guid.NewGuid());

        Assert.Equal(1, result.MatchedCount);
        Assert.Equal(1, result.UnmatchedCount);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Contains(store.Mappings!, item => item.GitHubLogin == "ada-l" && item.Status == "Matched");
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class FixedCredential : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) => new("graph-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class QueueHandler(Queue<HttpResponseMessage> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responses.Dequeue());
    }

    private sealed class FixedDirectory(IReadOnlyList<EntraUserProfile> users) : IEntraDirectoryClient
    {
        public Task<IReadOnlyList<EntraUserProfile>> GetUsersAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(users);
    }

    private sealed class RecordingStore(IReadOnlyList<GitHubIdentityCandidate> candidates) : IIdentityMappingStore
    {
        public IReadOnlyList<IdentityMapping>? Mappings { get; private set; }

        public Task<IReadOnlyList<GitHubIdentityCandidate>> GetCandidatesAsync(
            Guid enterpriseId,
            CancellationToken cancellationToken = default) => Task.FromResult(candidates);

        public Task SaveAsync(
            Guid enterpriseId,
            IReadOnlyList<IdentityMapping> mappings,
            DateTimeOffset synchronizedAt,
            CancellationToken cancellationToken = default)
        {
            Mappings = mappings;
            return Task.CompletedTask;
        }
    }
}
