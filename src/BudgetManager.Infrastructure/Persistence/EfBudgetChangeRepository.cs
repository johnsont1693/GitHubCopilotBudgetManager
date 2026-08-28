using System.Text.Json;
using BudgetManager.Application.Budgets;
using BudgetManager.Application.GitHub;
using BudgetManager.Application.Messaging;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Persistence;

public sealed class EfBudgetChangeRepository(
    BudgetManagerDbContext dbContext,
    WorkflowEventOptions workflowEventOptions) : IBudgetChangeRepository
{
    public async Task<Guid> CreateAsync(
        PendingBudgetChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        var existingId = await FindExistingRequestIdAsync(change, cancellationToken);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        var id = Guid.NewGuid();
        var concurrencyToken = Guid.NewGuid();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.BudgetChangeRequests.Add(new BudgetChangeRequestRecord
        {
            Id = id,
            EnterpriseId = change.EnterpriseId,
            BudgetId = change.BudgetId,
            ExpectedCurrentAmount = change.ExpectedCurrentAmount,
            ProposedAmount = change.ProposedAmount,
            ForecastAmount = change.ForecastAmount,
            DataFingerprint = change.DataFingerprint,
            EvidenceJson = change.EvidenceJson,
            Status = change.AutomaticMode ? BudgetChangeStatus.Approved : BudgetChangeStatus.PendingApproval,
            AutomaticMode = change.AutomaticMode,
            ConcurrencyToken = concurrencyToken,
            CreatedAt = change.CreatedAt,
            ExpiresAt = change.ExpiresAt,
        });
        AddAudit(
            change.EnterpriseId,
            "budget.change.requested",
            change.RequestedBy,
            change.BudgetId,
            change.CreatedAt,
            new { requestId = id, change.ProposedAmount, change.AutomaticMode });
        AddOutbox(
            change.EnterpriseId,
            "budget.change.requested.v1",
            $"budget-change:{id:N}:requested",
            "Copilot budget change requires approval",
            new
            {
                summary = $"Budget {change.BudgetId} is proposed to increase to {change.ProposedAmount}.",
                requestId = id,
                change.BudgetId,
                currentAmount = change.ExpectedCurrentAmount,
                change.ProposedAmount,
                change.ForecastAmount,
                concurrencyToken,
                change.ExpiresAt,
            },
            change.CreatedAt);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return id;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            existingId = await FindExistingRequestIdAsync(change, cancellationToken);
            if (existingId is not null)
            {
                return existingId.Value;
            }

            throw;
        }
    }

    public Task<bool> ApproveAsync(
        Guid requestId,
        Guid expectedConcurrencyToken,
        string actorObjectId,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken = default) => DecideAsync(
            requestId,
            expectedConcurrencyToken,
            actorObjectId,
            "approved",
            null,
            decidedAt,
            cancellationToken);

    public Task<bool> RejectAsync(
        Guid requestId,
        Guid expectedConcurrencyToken,
        string actorObjectId,
        string? reason,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken = default) => DecideAsync(
            requestId,
            expectedConcurrencyToken,
            actorObjectId,
            "rejected",
            reason,
            decidedAt,
            cancellationToken);

    public async Task<BudgetChangeExecutionCandidate?> GetExecutionCandidateAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        return await (
            from request in dbContext.BudgetChangeRequests.AsNoTracking()
            join enterprise in dbContext.Enterprises.AsNoTracking()
                on request.EnterpriseId equals enterprise.Id
            where request.Id == requestId && request.Status == BudgetChangeStatus.Approved
            select new BudgetChangeExecutionCandidate(
                request.Id,
                request.EnterpriseId,
                enterprise.Slug,
                request.BudgetId,
                request.ExpectedCurrentAmount,
                request.ProposedAmount,
                request.ConcurrencyToken))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetApprovedRequestIdsAsync(
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);
        return await dbContext.BudgetChangeRequests
            .AsNoTracking()
            .Where(item => item.Status == BudgetChangeStatus.Approved)
            .OrderBy(item => item.CreatedAt)
            .Select(item => item.Id)
            .Take(maximumCount)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryBeginExecutionAsync(
        Guid requestId,
        Guid expectedConcurrencyToken,
        CancellationToken cancellationToken = default)
    {
        var newToken = Guid.NewGuid();
        var affected = await dbContext.BudgetChangeRequests
            .Where(item => item.Id == requestId
                && item.Status == BudgetChangeStatus.Approved
                && item.ConcurrencyToken == expectedConcurrencyToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, BudgetChangeStatus.Executing)
                .SetProperty(item => item.ConcurrencyToken, newToken), cancellationToken);
        return affected == 1;
    }

    public async Task MarkAppliedAsync(
        Guid requestId,
        GitHubBudget updatedBudget,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updatedBudget);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var request = await GetExecutingAsync(requestId, cancellationToken);
        request.Status = BudgetChangeStatus.Applied;
        request.ExecutedAt = appliedAt;
        request.ConcurrencyToken = Guid.NewGuid();
        dbContext.BudgetSnapshots.Add(new BudgetSnapshotRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = request.EnterpriseId,
            BudgetId = updatedBudget.Id,
            BudgetType = updatedBudget.BudgetType,
            BudgetScope = updatedBudget.BudgetScope,
            BudgetEntityName = updatedBudget.BudgetEntityName,
            UserLogin = updatedBudget.User,
            BudgetAmount = updatedBudget.BudgetAmount,
            ConsumedAmount = updatedBudget.ConsumedAmount,
            ProductSku = updatedBudget.BudgetProductSku,
            PreventFurtherUsage = updatedBudget.PreventFurtherUsage,
            AlertingJson = JsonSerializer.Serialize(updatedBudget.BudgetAlerting),
            ObservedAt = appliedAt,
        });
        var baseline = await dbContext.BudgetBaselines.FindAsync(
            [request.EnterpriseId, request.BudgetId],
            cancellationToken);
        if (baseline is not null)
        {
            baseline.LastToolWrittenAmount = updatedBudget.BudgetAmount;
            baseline.UpdatedAt = appliedAt;
            baseline.UpdatedBy = "budget-write-executor";
        }

        AddAudit(request.EnterpriseId, "budget.change.applied", "budget-write-executor", request.BudgetId, appliedAt, new
        {
            requestId,
            updatedBudget.BudgetAmount,
        });
        AddOutbox(
            request.EnterpriseId,
            "budget.change.applied.v1",
            $"budget-change:{requestId:N}:applied",
            "Copilot budget change applied",
            new
            {
                summary = $"Budget {request.BudgetId} was updated to {updatedBudget.BudgetAmount}.",
                requestId,
                request.BudgetId,
                updatedBudget.BudgetAmount,
            },
            appliedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task MarkConflictAsync(
        Guid requestId,
        string failureCode,
        DateTimeOffset detectedAt,
        CancellationToken cancellationToken = default) => MarkTerminalAsync(
            requestId,
            BudgetChangeStatus.Conflict,
            failureCode,
            "budget.change.conflict",
            detectedAt,
            cancellationToken);

    public Task MarkFailedAsync(
        Guid requestId,
        string failureCode,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken = default) => MarkTerminalAsync(
            requestId,
            BudgetChangeStatus.Failed,
            failureCode,
            "budget.change.failed",
            failedAt,
            cancellationToken);

    private async Task<bool> DecideAsync(
        Guid requestId,
        Guid expectedConcurrencyToken,
        string actorObjectId,
        string decision,
        string? reason,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorObjectId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var request = await dbContext.BudgetChangeRequests.SingleOrDefaultAsync(item =>
            item.Id == requestId
            && item.Status == BudgetChangeStatus.PendingApproval
            && item.ConcurrencyToken == expectedConcurrencyToken,
            cancellationToken);
        if (request is null || request.ExpiresAt <= decidedAt)
        {
            return false;
        }

        request.Status = decision == "approved" ? BudgetChangeStatus.Approved : BudgetChangeStatus.Rejected;
        request.ConcurrencyToken = Guid.NewGuid();
        dbContext.ApprovalDecisions.Add(new ApprovalDecisionRecord
        {
            Id = Guid.NewGuid(),
            BudgetChangeRequestId = requestId,
            Decision = decision,
            ActorObjectId = actorObjectId,
            Reason = reason,
            CallbackNonceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{requestId:N}:{expectedConcurrencyToken:N}"))),
            DecidedAt = decidedAt,
        });
        AddAudit(request.EnterpriseId, $"budget.change.{decision}", actorObjectId, request.BudgetId, decidedAt, new
        {
            requestId,
            reason,
        });
        AddOutbox(
            request.EnterpriseId,
            $"budget.change.{decision}.v1",
            $"budget-change:{requestId:N}:{decision}",
            $"Copilot budget change {decision}",
            new
            {
                summary = $"Budget change for {request.BudgetId} was {decision}.",
                requestId,
                request.BudgetId,
                decision,
            },
            decidedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task MarkTerminalAsync(
        Guid requestId,
        BudgetChangeStatus status,
        string failureCode,
        string eventType,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var request = await GetExecutingAsync(requestId, cancellationToken);
        request.Status = status;
        request.FailureCode = failureCode;
        request.ExecutedAt = occurredAt;
        request.ConcurrencyToken = Guid.NewGuid();
        AddAudit(request.EnterpriseId, eventType, "budget-write-executor", request.BudgetId, occurredAt, new
        {
            requestId,
            failureCode,
        });
        AddOutbox(
            request.EnterpriseId,
            $"{eventType}.v1",
            $"budget-change:{requestId:N}:{status}",
            status == BudgetChangeStatus.Conflict
                ? "Copilot budget change blocked by drift"
                : "Copilot budget change failed",
            new
            {
                summary = $"Budget change for {request.BudgetId} ended as {status}.",
                requestId,
                request.BudgetId,
                failureCode,
            },
            occurredAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private Task<BudgetChangeRequestRecord> GetExecutingAsync(
        Guid requestId,
        CancellationToken cancellationToken) => dbContext.BudgetChangeRequests.SingleAsync(
            item => item.Id == requestId && item.Status == BudgetChangeStatus.Executing,
            cancellationToken);

    private async Task<Guid?> FindExistingRequestIdAsync(
        PendingBudgetChange change,
        CancellationToken cancellationToken) => await dbContext.BudgetChangeRequests
        .AsNoTracking()
        .Where(item => item.EnterpriseId == change.EnterpriseId
            && item.BudgetId == change.BudgetId
            && item.DataFingerprint == change.DataFingerprint)
        .Select(item => (Guid?)item.Id)
        .SingleOrDefaultAsync(cancellationToken);

    private void AddAudit(
        Guid enterpriseId,
        string eventType,
        string actorId,
        string budgetId,
        DateTimeOffset occurredAt,
        object data) => dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            EventType = eventType,
            ActorType = actorId is "automation" or "baseline-reconciliation" or "budget-write-executor" or "rule-engine"
                ? "service"
                : "user",
            ActorId = actorId,
            TargetType = "budget",
            TargetId = budgetId,
            DataJson = JsonSerializer.Serialize(data),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAt = occurredAt,
        });

    private void AddOutbox(
        Guid enterpriseId,
        string eventType,
        string fingerprint,
        string subject,
        object payload,
        DateTimeOffset occurredAt)
    {
        if (!workflowEventOptions.PublishBudgetLifecycleEvents)
        {
            return;
        }

        dbContext.OutboxMessages.Add(new OutboxMessageRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            EventType = eventType,
            EventFingerprint = fingerprint,
            PayloadJson = WorkflowEventSerializer.Serialize(
                enterpriseId,
                eventType,
                fingerprint,
                subject,
                payload,
                occurredAt,
                workflowEventOptions.DashboardUrl,
                new WorkflowEventRecipients([], [], workflowEventOptions.AdminPrincipalNames)),
            Status = OutboxStatus.Pending,
            OccurredAt = occurredAt,
        });
    }
}
