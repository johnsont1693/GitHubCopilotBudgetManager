using BudgetManager.Application.GitHub;
using BudgetManager.Domain.Budgets;
using BudgetManager.Domain.Classification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BudgetManager.Application.Budgets;

public sealed record PendingBudgetChange(
    Guid EnterpriseId,
    string BudgetId,
    long ExpectedCurrentAmount,
    long ProposedAmount,
    decimal ForecastAmount,
    string DataFingerprint,
    string EvidenceJson,
    bool AutomaticMode,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string RequestedBy = "rule-engine");

public sealed record BudgetChangeCreationResult(
    bool Created,
    Guid? RequestId,
    BudgetIncreaseDecision GuardrailDecision,
    string? RejectionReason);

public sealed record BudgetChangeExecutionCandidate(
    Guid Id,
    Guid EnterpriseId,
    string EnterpriseSlug,
    string BudgetId,
    long ExpectedCurrentAmount,
    long ProposedAmount,
    Guid ConcurrencyToken);

public enum BudgetWriteOutcomeKind
{
    Applied = 0,
    Conflict = 1,
    NotExecutable = 2,
    Failed = 3,
}

public sealed record BudgetWriteOutcome(BudgetWriteOutcomeKind Outcome, string Message);

public interface IBudgetChangeRepository
{
    Task<Guid> CreateAsync(PendingBudgetChange change, CancellationToken cancellationToken = default);

    Task<bool> ApproveAsync(
        Guid requestId,
        Guid expectedConcurrencyToken,
        string actorObjectId,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken = default);

    Task<bool> RejectAsync(
        Guid requestId,
        Guid expectedConcurrencyToken,
        string actorObjectId,
        string? reason,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken = default);

    Task<BudgetChangeExecutionCandidate?> GetExecutionCandidateAsync(
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetApprovedRequestIdsAsync(
        int maximumCount,
        CancellationToken cancellationToken = default);

    Task<bool> TryBeginExecutionAsync(
        Guid requestId,
        Guid expectedConcurrencyToken,
        CancellationToken cancellationToken = default);

    Task MarkAppliedAsync(
        Guid requestId,
        GitHubBudget updatedBudget,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken = default);

    Task MarkConflictAsync(
        Guid requestId,
        string failureCode,
        DateTimeOffset detectedAt,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        Guid requestId,
        string failureCode,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken = default);
}

public sealed class BudgetChangeProposalService
{
    private readonly IBudgetChangeRepository repository;
    private readonly TimeProvider timeProvider;

    public BudgetChangeProposalService(
        IBudgetChangeRepository repository,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        this.repository = repository;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<BudgetChangeCreationResult> CreateAsync(
        Guid enterpriseId,
        BudgetIncreaseGuardrailPolicy policy,
        BudgetIncreaseRequest request,
        HealthStatus healthStatus,
        bool automaticMode,
        string evidenceJson,
        TimeSpan approvalLifetime,
        string requestedBy = "rule-engine",
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(enterpriseId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(approvalLifetime, TimeSpan.Zero);
        var now = timeProvider.GetUtcNow();
        var decision = BudgetIncreaseGuardrailEvaluator.Evaluate(policy, request, now);
        if (!decision.IsAllowed)
        {
            return new BudgetChangeCreationResult(false, null, decision, "One or more financial guardrails failed.");
        }

        if (automaticMode && healthStatus != HealthStatus.Green)
        {
            return new BudgetChangeCreationResult(
                false,
                null,
                decision,
                "Automatic increases require a Green health classification.");
        }

        var id = await repository.CreateAsync(new PendingBudgetChange(
            enterpriseId,
            request.BudgetId,
            request.CurrentAmount,
            request.ProposedAmount,
            request.ForecastAmount,
            request.DataFingerprint,
            evidenceJson,
            automaticMode,
            now,
            now.Add(approvalLifetime),
            requestedBy), cancellationToken);
        return new BudgetChangeCreationResult(true, id, decision, null);
    }
}

public sealed partial class BudgetWriteExecutor
{
    private readonly IGitHubBudgetClient budgetClient;
    private readonly ILogger<BudgetWriteExecutor> logger;
    private readonly IBudgetChangeRepository repository;
    private readonly TimeProvider timeProvider;

    public BudgetWriteExecutor(
        IGitHubBudgetClient budgetClient,
        IBudgetChangeRepository repository,
        TimeProvider? timeProvider = null,
        ILogger<BudgetWriteExecutor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(budgetClient);
        ArgumentNullException.ThrowIfNull(repository);
        this.budgetClient = budgetClient;
        this.logger = logger ?? NullLogger<BudgetWriteExecutor>.Instance;
        this.repository = repository;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<BudgetWriteOutcome> ExecuteAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(requestId, Guid.Empty);
        var candidate = await repository.GetExecutionCandidateAsync(requestId, cancellationToken);
        if (candidate is null
            || !await repository.TryBeginExecutionAsync(
                requestId,
                candidate.ConcurrencyToken,
                cancellationToken))
        {
            return new BudgetWriteOutcome(
                BudgetWriteOutcomeKind.NotExecutable,
                "The request is not approved or was changed by another operation.");
        }

        var now = timeProvider.GetUtcNow();
        try
        {
            var currentBudget = await budgetClient.GetBudgetAsync(
                candidate.EnterpriseSlug,
                candidate.BudgetId,
                cancellationToken);
            if (currentBudget.BudgetAmount != candidate.ExpectedCurrentAmount)
            {
                LogBudgetDrift(
                    logger,
                    requestId,
                    candidate.BudgetId,
                    candidate.ExpectedCurrentAmount,
                    currentBudget.BudgetAmount);
                await repository.MarkConflictAsync(
                    requestId,
                    "github-budget-drift",
                    now,
                    cancellationToken);
                return new BudgetWriteOutcome(
                    BudgetWriteOutcomeKind.Conflict,
                    "GitHub budget changed after the request was created.");
            }

            var result = await budgetClient.UpdateBudgetAmountAsync(
                candidate.EnterpriseSlug,
                candidate.BudgetId,
                candidate.ProposedAmount,
                cancellationToken);
            await repository.MarkAppliedAsync(requestId, result.Budget, now, cancellationToken);
            LogBudgetApplied(
                logger,
                requestId,
                candidate.BudgetId,
                result.Budget.BudgetAmount);
            return new BudgetWriteOutcome(BudgetWriteOutcomeKind.Applied, result.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException)
        {
            LogBudgetFailure(
                logger,
                exception,
                requestId,
                candidate.BudgetId);
            await repository.MarkFailedAsync(
                requestId,
                exception.GetType().Name,
                now,
                cancellationToken);
            return new BudgetWriteOutcome(BudgetWriteOutcomeKind.Failed, "GitHub budget update failed.");
        }
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Budget request {RequestId} was blocked by drift for budget {BudgetId}; expected {ExpectedAmount} and observed {ObservedAmount}")]
    private static partial void LogBudgetDrift(
        ILogger logger,
        Guid requestId,
        string budgetId,
        long expectedAmount,
        long observedAmount);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Budget request {RequestId} applied budget {BudgetId} amount {BudgetAmount}")]
    private static partial void LogBudgetApplied(
        ILogger logger,
        Guid requestId,
        string budgetId,
        long budgetAmount);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "Budget request {RequestId} failed while updating budget {BudgetId}")]
    private static partial void LogBudgetFailure(
        ILogger logger,
        Exception exception,
        Guid requestId,
        string budgetId);
}
