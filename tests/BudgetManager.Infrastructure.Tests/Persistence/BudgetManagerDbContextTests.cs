using BudgetManager.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Tests.Persistence;

public sealed class BudgetManagerDbContextTests
{
    [Fact]
    public async Task Schema_persists_the_core_operational_records()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BudgetManagerDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new BudgetManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var now = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
        var enterpriseId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var changeRequestId = Guid.NewGuid();

        context.Enterprises.Add(new EnterpriseRecord
        {
            Id = enterpriseId,
            Slug = "acme",
            DisplayName = "Acme Enterprise",
            CreatedAt = now,
            UpdatedAt = now,
        });
        context.ManagedEntities.Add(new ManagedEntityRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            ScopeKind = ScopeKind.Organization,
            ExternalId = "engineering",
            DisplayName = "Engineering",
            ParentScopeKind = ScopeKind.Enterprise,
            ParentExternalId = "acme",
            IsActive = true,
            ObservedAt = now,
        });
        context.DailyMetrics.Add(new DailyMetricRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            ScopeKind = ScopeKind.Organization,
            ScopeExternalId = "engineering",
            MetricKey = "active_seat_percent",
            MetricValue = 75m,
            Availability = "available",
            Source = "copilot.users-1-day",
            MetricDay = new DateOnly(2026, 8, 23),
            IngestedAt = now,
        });
        context.Policies.Add(new PolicyRecord
        {
            Id = policyId,
            EnterpriseId = enterpriseId,
            Name = "Default health",
            Version = 1,
            Mode = ClassificationPolicyMode.Weighted,
            Governance = PolicyGovernance.Enforced,
            ScopeKind = ScopeKind.Enterprise,
            ScopeExternalId = "acme",
            DefinitionJson = "{}",
            IsActive = true,
            CreatedBy = "admin-object-id",
            CreatedAt = now,
        });
        context.ClassificationSnapshots.Add(new ClassificationSnapshotRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            ScopeKind = ScopeKind.Organization,
            ScopeExternalId = "engineering",
            PolicyId = policyId,
            PolicyVersion = 1,
            Status = "green",
            Score = 75m,
            ReasonsJson = "[]",
            InputsFingerprint = "classification-fingerprint",
            EvaluatedAt = now,
        });
        context.BudgetSnapshots.Add(new BudgetSnapshotRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            BudgetId = "budget-1",
            BudgetType = "BundlePricing",
            BudgetScope = "enterprise",
            BudgetAmount = 1_000,
            ConsumedAmount = 425.5m,
            ProductSku = "ai_credits",
            PreventFurtherUsage = true,
            AlertingJson = "{}",
            IsEffective = true,
            ObservedAt = now,
        });
        context.BudgetChangeRequests.Add(new BudgetChangeRequestRecord
        {
            Id = changeRequestId,
            EnterpriseId = enterpriseId,
            BudgetId = "budget-1",
            ExpectedCurrentAmount = 1_000,
            ProposedAmount = 1_250,
            ForecastAmount = 1_150m,
            DataFingerprint = "budget-change-fingerprint",
            EvidenceJson = "{}",
            Status = BudgetChangeStatus.PendingApproval,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = now,
            ExpiresAt = now.AddDays(2),
        });
        context.ApprovalDecisions.Add(new ApprovalDecisionRecord
        {
            Id = Guid.NewGuid(),
            BudgetChangeRequestId = changeRequestId,
            Decision = "approved",
            ActorObjectId = "admin-object-id",
            CallbackNonceHash = "nonce-hash",
            DecidedAt = now,
        });
        context.BudgetBaselines.Add(new BudgetBaselineRecord
        {
            EnterpriseId = enterpriseId,
            BudgetId = "budget-1",
            BaselineAmount = 1_000,
            Mode = BaselineReconciliationMode.ProtectedAutomatic,
            LastToolWrittenAmount = 1_250,
            UpdatedBy = "admin-object-id",
            UpdatedAt = now,
        });
        context.OutboxMessages.Add(new OutboxMessageRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            EventType = "budget.change.requested.v1",
            EventFingerprint = "outbox-fingerprint",
            PayloadJson = "{}",
            Status = OutboxStatus.Pending,
            OccurredAt = now,
        });
        context.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            EventType = "budget.change.requested",
            ActorType = "service",
            ActorId = "rule-engine",
            TargetType = "budget",
            TargetId = "budget-1",
            DataJson = "{}",
            CorrelationId = "correlation-1",
            OccurredAt = now,
        });
        context.RetentionPolicies.Add(new RetentionPolicyRecord
        {
            EnterpriseId = enterpriseId,
            RawReportDays = 30,
            UserMetricDays = 365,
            AggregateMetricDays = 730,
            NotificationDays = 365,
            AuditDays = 2_555,
            UpdatedAt = now,
        });

        await context.SaveChangesAsync();

        Assert.Equal(1, await context.Enterprises.CountAsync());
        Assert.Equal(1, await context.DailyMetrics.CountAsync());
        Assert.Equal(1, await context.BudgetSnapshots.CountAsync());
        Assert.Equal(1, await context.ClassificationSnapshots.CountAsync());
        Assert.Equal(1, await context.BudgetChangeRequests.CountAsync());
        Assert.Equal(1, await context.OutboxMessages.CountAsync());
        Assert.Equal(1, await context.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Schema_rejects_duplicate_metric_facts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BudgetManagerDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new BudgetManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var enterpriseId = Guid.NewGuid();
        var metricDay = new DateOnly(2026, 8, 23);
        context.DailyMetrics.AddRange(
            CreateMetric(enterpriseId, metricDay),
            CreateMetric(enterpriseId, metricDay));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static DailyMetricRecord CreateMetric(Guid enterpriseId, DateOnly metricDay) => new()
    {
        Id = Guid.NewGuid(),
        EnterpriseId = enterpriseId,
        ScopeKind = ScopeKind.Enterprise,
        ScopeExternalId = "acme",
        MetricKey = "active_users",
        MetricValue = 100m,
        Availability = "available",
        Source = "copilot.enterprise-1-day",
        MetricDay = metricDay,
        IngestedAt = DateTimeOffset.UtcNow,
    };
}
