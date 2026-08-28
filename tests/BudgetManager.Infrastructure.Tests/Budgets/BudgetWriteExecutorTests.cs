using BudgetManager.Application.Budgets;
using BudgetManager.Application.GitHub;
using BudgetManager.Domain.Budgets;
using BudgetManager.Domain.Classification;

namespace BudgetManager.Infrastructure.Tests.Budgets;

public sealed class BudgetWriteExecutorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_marks_conflict_without_patching_when_GitHub_drifted()
    {
        var repository = new RecordingRepository();
        var client = new RecordingBudgetClient(CreateBudget(1_100));
        var executor = new BudgetWriteExecutor(client, repository, new FixedTimeProvider(Now));

        var result = await executor.ExecuteAsync(repository.Candidate.Id);

        Assert.Equal(BudgetWriteOutcomeKind.Conflict, result.Outcome);
        Assert.Equal(0, client.UpdateCount);
        Assert.Equal("github-budget-drift", repository.ConflictCode);
    }

    [Fact]
    public async Task ExecuteAsync_patches_and_marks_applied_when_expected_amount_matches()
    {
        var repository = new RecordingRepository();
        var client = new RecordingBudgetClient(CreateBudget(1_000));
        var executor = new BudgetWriteExecutor(client, repository, new FixedTimeProvider(Now));

        var result = await executor.ExecuteAsync(repository.Candidate.Id);

        Assert.Equal(BudgetWriteOutcomeKind.Applied, result.Outcome);
        Assert.Equal(1, client.UpdateCount);
        Assert.Equal(1_250, repository.AppliedBudget?.BudgetAmount);
    }

    [Fact]
    public async Task Proposal_rejects_non_green_automatic_mode_after_financial_guardrails_pass()
    {
        var repository = new RecordingRepository();
        var service = new BudgetChangeProposalService(repository, new FixedTimeProvider(Now));
        var policy = new BudgetIncreaseGuardrailPolicy(
            500,
            50m,
            700,
            20m,
            TimeSpan.FromDays(7),
            TimeSpan.FromDays(2));
        var request = new BudgetIncreaseRequest(
            "budget-1",
            1_000,
            1_200,
            1_100m,
            0,
            Now.AddHours(-1),
            "fingerprint");

        var result = await service.CreateAsync(
            Guid.NewGuid(),
            policy,
            request,
            HealthStatus.Yellow,
            true,
            "{}",
            TimeSpan.FromDays(2),
            cancellationToken: default);

        Assert.False(result.Created);
        Assert.Contains("Green", result.RejectionReason, StringComparison.Ordinal);
    }

    private static GitHubBudget CreateBudget(long amount) => new(
        "budget-1",
        "BundlePricing",
        amount,
        true,
        "enterprise",
        string.Empty,
        null,
        500m,
        "ai_credits",
        new GitHubBudgetAlerting(false, []));

    private sealed class RecordingBudgetClient(GitHubBudget currentBudget) : IGitHubBudgetClient
    {
        public int UpdateCount { get; private set; }

        public Task<GitHubBudgetPage> GetBudgetsAsync(string enterpriseSlug, int page = 1, int perPage = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GitHubBudget> GetBudgetAsync(string enterpriseSlug, string budgetId, CancellationToken cancellationToken = default) => Task.FromResult(currentBudget);

        public Task<GitHubBudgetUpdateResult> UpdateBudgetAmountAsync(string enterpriseSlug, string budgetId, long budgetAmount, CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            return Task.FromResult(new GitHubBudgetUpdateResult(
                "updated",
                currentBudget with { BudgetAmount = budgetAmount }));
        }
    }

    private sealed class RecordingRepository : IBudgetChangeRepository
    {
        public BudgetChangeExecutionCandidate Candidate { get; } = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "acme",
            "budget-1",
            1_000,
            1_250,
            Guid.NewGuid());

        public string? ConflictCode { get; private set; }

        public GitHubBudget? AppliedBudget { get; private set; }

        public Task<Guid> CreateAsync(PendingBudgetChange change, CancellationToken cancellationToken = default) => Task.FromResult(Guid.NewGuid());

        public Task<bool> ApproveAsync(Guid requestId, Guid expectedConcurrencyToken, string actorObjectId, DateTimeOffset decidedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> RejectAsync(Guid requestId, Guid expectedConcurrencyToken, string actorObjectId, string? reason, DateTimeOffset decidedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BudgetChangeExecutionCandidate?> GetExecutionCandidateAsync(Guid requestId, CancellationToken cancellationToken = default) => Task.FromResult<BudgetChangeExecutionCandidate?>(Candidate);

        public Task<IReadOnlyList<Guid>> GetApprovedRequestIdsAsync(int maximumCount, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Guid>>([Candidate.Id]);

        public Task<bool> TryBeginExecutionAsync(Guid requestId, Guid expectedConcurrencyToken, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task MarkAppliedAsync(Guid requestId, GitHubBudget updatedBudget, DateTimeOffset appliedAt, CancellationToken cancellationToken = default)
        {
            AppliedBudget = updatedBudget;
            return Task.CompletedTask;
        }

        public Task MarkConflictAsync(Guid requestId, string failureCode, DateTimeOffset detectedAt, CancellationToken cancellationToken = default)
        {
            ConflictCode = failureCode;
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(Guid requestId, string failureCode, DateTimeOffset failedAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
