using System.Text.Json;
using BudgetManager.Application.Budgets;
using BudgetManager.Application.Messaging;
using BudgetManager.Domain.Budgets;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Persistence;

public sealed class EfBaselineReconciliationStore(
    BudgetManagerDbContext dbContext,
    WorkflowEventOptions workflowEventOptions) : IBaselineReconciliationStore
{
    public async Task<IReadOnlyList<BaselineReconciliationItem>> GetItemsAsync(Guid enterpriseId, CancellationToken cancellationToken = default)
    {
        var baselines = await dbContext.BudgetBaselines.AsNoTracking().Where(item => item.EnterpriseId == enterpriseId).ToListAsync(cancellationToken);
        var history = await dbContext.BudgetSnapshots.AsNoTracking().Where(item => item.EnterpriseId == enterpriseId).ToListAsync(cancellationToken);
        var latest = history.GroupBy(item => item.BudgetId, StringComparer.Ordinal).ToDictionary(
            group => group.Key,
            group => group.MaxBy(item => item.ObservedAt)!,
            StringComparer.Ordinal);
        return baselines.Where(item => latest.ContainsKey(item.BudgetId)).Select(item => new BaselineReconciliationItem(
            item.EnterpriseId,
            item.BudgetId,
            latest[item.BudgetId].BudgetAmount,
            item.BaselineAmount,
            item.LastToolWrittenAmount,
            Enum.Parse<BaselineMode>(item.Mode.ToString(), true))).ToArray();
    }

    public async Task SaveDecisionsAsync(
        Guid enterpriseId,
        IReadOnlyList<(BaselineReconciliationItem Item, BaselineReconciliationDecision Decision)> decisions,
        DateTimeOffset evaluatedAt,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!dryRun)
        {
            foreach (var (item, decision) in decisions)
            {
                var status = decision.Action switch
                {
                    BaselineReconciliationAction.ResetAutomatically => BudgetChangeStatus.Approved,
                    BaselineReconciliationAction.RequestApproval => BudgetChangeStatus.PendingApproval,
                    _ => (BudgetChangeStatus?)null,
                };
                if (status is null)
                {
                    continue;
                }

                var fingerprint = $"baseline:{item.BudgetId}:{evaluatedAt:yyyy-MM}";
                if (await dbContext.BudgetChangeRequests.AnyAsync(request => request.EnterpriseId == enterpriseId && request.BudgetId == item.BudgetId && request.DataFingerprint == fingerprint, cancellationToken))
                {
                    continue;
                }

                var requestId = Guid.NewGuid();
                dbContext.BudgetChangeRequests.Add(new BudgetChangeRequestRecord
                {
                    Id = requestId,
                    EnterpriseId = enterpriseId,
                    BudgetId = item.BudgetId,
                    ExpectedCurrentAmount = item.CurrentAmount,
                    ProposedAmount = item.BaselineAmount,
                    ForecastAmount = item.BaselineAmount,
                    DataFingerprint = fingerprint,
                    EvidenceJson = JsonSerializer.Serialize(decision),
                    Status = status.Value,
                    AutomaticMode = status == BudgetChangeStatus.Approved,
                    ConcurrencyToken = Guid.NewGuid(),
                    CreatedAt = evaluatedAt,
                    ExpiresAt = evaluatedAt.AddDays(7),
                });
                dbContext.OutboxMessages.Add(new OutboxMessageRecord
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = enterpriseId,
                    EventType = "budget.baseline.reconciliation.requested.v1",
                    EventFingerprint = $"baseline:{requestId:N}",
                    PayloadJson = WorkflowEventSerializer.Serialize(
                        enterpriseId,
                        "budget.baseline.reconciliation.requested.v1",
                        $"baseline:{requestId:N}",
                        "Copilot budget baseline reconciliation",
                        new
                        {
                            summary = $"Budget {item.BudgetId} requires baseline reconciliation.",
                            requestId,
                            item.BudgetId,
                            decision.Action,
                        },
                        evaluatedAt,
                        workflowEventOptions.DashboardUrl,
                        new WorkflowEventRecipients([], [], workflowEventOptions.AdminPrincipalNames)),
                    Status = OutboxStatus.Pending,
                    OccurredAt = evaluatedAt,
                });
            }
        }

        dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            EventType = dryRun ? "budget.baseline.reconciliation.previewed" : "budget.baseline.reconciliation.evaluated",
            ActorType = "service",
            ActorId = "baseline-reconciliation",
            TargetType = "enterprise",
            TargetId = enterpriseId.ToString("N"),
            DataJson = JsonSerializer.Serialize(new
            {
                decisionCount = decisions.Count,
                dryRun,
                decisions = decisions.Select(item => new
                {
                    item.Item.BudgetId,
                    item.Item.Mode,
                    item.Item.LastToolWrittenAmount,
                    item.Decision.Action,
                    item.Decision.CurrentAmount,
                    item.Decision.BaselineAmount,
                    item.Decision.Reason,
                }),
            }),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAt = evaluatedAt,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
