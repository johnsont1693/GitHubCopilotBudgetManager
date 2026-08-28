using System.Text.Json;
using BudgetManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Api;

internal static class DevelopmentDataSeeder
{
    public static readonly Guid EnterpriseId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static async Task SeedAsync(
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        if (await dbContext.Enterprises.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var policyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        dbContext.Enterprises.Add(new EnterpriseRecord
        {
            Id = EnterpriseId,
            Slug = "contoso-demo",
            DisplayName = "Contoso Engineering",
            CreatedAt = now,
            UpdatedAt = now,
        });
        dbContext.BudgetSnapshots.AddRange(
            CreateBudget("budget-enterprise", "enterprise", "Contoso Engineering", 125_000, 82_400m, true, now),
            CreateBudget("budget-platform", "cost_center", "Platform", 38_000, 31_700m, false, now),
            CreateBudget("budget-apps", "organization", "Business Apps", 42_000, 20_100m, false, now));
        dbContext.Policies.Add(new PolicyRecord
        {
            Id = policyId,
            EnterpriseId = EnterpriseId,
            Name = "Enterprise health",
            Version = 1,
            Mode = ClassificationPolicyMode.Weighted,
            Governance = PolicyGovernance.Enforced,
            ScopeKind = ScopeKind.Enterprise,
            ScopeExternalId = "contoso-demo",
            DefinitionJson = "{\"metricRules\":[{\"metricKey\":\"daily_active_users\",\"weight\":1,\"poorThreshold\":100,\"goodThreshold\":1000,\"direction\":\"HigherIsBetter\",\"maximumAgeDays\":3}],\"yellowMinimumScore\":40,\"greenMinimumScore\":70}",
            IsActive = true,
            CreatedBy = "demo-admin",
            CreatedAt = now,
        });
        dbContext.ClassificationSnapshots.AddRange(
            CreateClassification(policyId, ScopeKind.Enterprise, "contoso-demo", "green", 78m, now),
            CreateClassification(policyId, ScopeKind.Organization, "business-apps", "yellow", 58m, now),
            CreateClassification(policyId, ScopeKind.CostCenter, "platform", "red", 31m, now),
            CreateClassification(policyId, ScopeKind.Team, "developer-experience", "unknown", null, now),
            CreateClassification(policyId, ScopeKind.Repository, "repo-portal", "green", 81m, now),
            CreateClassification(policyId, ScopeKind.User, "ada-l", "green", 84m, now));
        dbContext.ManagedEntities.AddRange(
            CreateEntity(ScopeKind.Enterprise, "contoso-demo", "Contoso Engineering", null, null, now),
            CreateEntity(ScopeKind.Organization, "business-apps", "Business Apps", ScopeKind.Enterprise, "contoso-demo", now),
            CreateEntity(ScopeKind.CostCenter, "platform", "Platform", ScopeKind.Enterprise, "contoso-demo", now, "{\"ownerPrincipalNames\":[\"platform-owner@contoso.com\"]}"),
            CreateEntity(ScopeKind.Team, "developer-experience", "Developer Experience", ScopeKind.Organization, "business-apps", now),
            CreateEntity(ScopeKind.Repository, "repo-portal", "contoso/developer-portal", ScopeKind.Organization, "business-apps", now),
            CreateEntity(ScopeKind.User, "ada-l", "ada-l", ScopeKind.Organization, "business-apps", now),
            CreateEntity(ScopeKind.User, "grace-h", "grace-h", ScopeKind.Organization, "business-apps", now));
        var metricDay = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-3);
        dbContext.DailyMetrics.AddRange(
            CreateMetric(ScopeKind.Enterprise, "contoso-demo", "daily_active_users", 802m, metricDay, now),
            CreateMetric(ScopeKind.Organization, "business-apps", "daily_active_users", 516m, metricDay, now),
            CreateMetric(ScopeKind.CostCenter, "platform", "daily_active_users", 183m, metricDay, now),
            CreateMetric(ScopeKind.Team, "developer-experience", "daily_active_users", null, metricDay, now, "suppressed", "GitHub omits teams with fewer than five seated users."),
            CreateMetric(ScopeKind.Repository, "repo-portal", "pull_requests.total_merged", 14m, metricDay, now),
            CreateMetric(ScopeKind.User, "ada-l", "code_generation_activity_count", 22m, metricDay, now));
        dbContext.EntityMemberships.AddRange(
            CreateMembership(ScopeKind.Team, "developer-experience", ScopeKind.User, "ada-l", metricDay),
            CreateMembership(ScopeKind.CostCenter, "platform", ScopeKind.User, "ada-l", metricDay));
        dbContext.IngestionManifests.Add(new IngestionManifestRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            ReportType = "UsersDay",
            ReportStartDay = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-3),
            ReportEndDay = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-3),
            BlobName = "raw/contoso-demo/UsersDay/demo.ndjson",
            ContentSha256 = "DEMO",
            Status = IngestionStatus.Succeeded,
            RecordCount = 15_420,
            StartedAt = now.AddMinutes(-8),
            CompletedAt = now.AddMinutes(-5),
        });
        dbContext.IdentityMappings.AddRange(
            CreateIdentity("ada-l", "ada@contoso.com", IdentityMappingStatus.Matched, now),
            CreateIdentity("grace-h", null, IdentityMappingStatus.Unmatched, now));
        dbContext.BudgetChangeRequests.Add(new BudgetChangeRequestRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            BudgetId = "budget-platform",
            ExpectedCurrentAmount = 38_000,
            ProposedAmount = 44_000,
            ForecastAmount = 41_200m,
            DataFingerprint = "demo-change",
            EvidenceJson = "{\"forecastHeadroomPercent\":7}",
            Status = BudgetChangeStatus.PendingApproval,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = now.AddHours(-2),
            ExpiresAt = now.AddDays(2),
        });
        dbContext.ForecastSnapshots.AddRange(
            CreateForecast("budget-enterprise", 125_000, 82_400m, 112_500m, now),
            CreateForecast("budget-platform", 38_000, 31_700m, 41_200m, now),
            CreateForecast("budget-apps", 42_000, 20_100m, 34_500m, now));
        dbContext.RetentionPolicies.Add(new RetentionPolicyRecord
        {
            EnterpriseId = EnterpriseId,
            RawReportDays = 90,
            UserMetricDays = 365,
            AggregateMetricDays = 730,
            NotificationDays = 365,
            AuditDays = 2_555,
            UpdatedAt = now,
        });
        dbContext.BudgetBaselines.Add(new BudgetBaselineRecord
        {
            EnterpriseId = EnterpriseId,
            BudgetId = "budget-platform",
            BaselineAmount = 38_000,
            Mode = BaselineReconciliationMode.ApprovalOnly,
            UpdatedBy = "demo-admin",
            UpdatedAt = now,
        });
        dbContext.BudgetUserStateSnapshots.Add(new BudgetUserStateSnapshotRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            BudgetId = "budget-enterprise",
            UserLogin = "ada-l",
            ConsumedAmount = 72m,
            TargetAmount = 100m,
            ObservedAt = now,
        });
        dbContext.BillingReportExports.Add(new BillingReportExportRecord
        {
            EnterpriseId = EnterpriseId,
            ReportId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ReportType = "detailed",
            StartDate = new DateOnly(now.Year, now.Month, 1),
            EndDate = DateOnly.FromDateTime(now.UtcDateTime),
            Status = "ready",
            DownloadUrlCount = 1,
            CreatedAt = now.AddMinutes(-20),
            Actor = "demo-admin",
            FirstObservedAt = now.AddMinutes(-15),
            LastObservedAt = now,
        });
        const string notificationFingerprint = "health:demo:yellow";
        dbContext.OutboxMessages.Add(new OutboxMessageRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            EventType = "classification.health.changed.v1",
            EventFingerprint = notificationFingerprint,
            PayloadJson = "{}",
            Status = OutboxStatus.Processed,
            OccurredAt = now.AddHours(-1),
            ProcessedAt = now.AddMinutes(-58),
        });
        dbContext.NotificationDeliveries.Add(new NotificationDeliveryRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            EventFingerprint = notificationFingerprint,
            Channel = "Outlook",
            RecipientKey = "ada@contoso.com",
            TemplateVersion = "health-v1",
            Status = DeliveryStatus.Sent,
            AttemptCount = 1,
            ProviderMessageId = "demo-message",
            CreatedAt = now.AddHours(-1),
            DeliveredAt = now.AddMinutes(-57),
        });
        dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            EventType = "demo.seeded",
            ActorType = "service",
            ActorId = "development-seeder",
            TargetType = "enterprise",
            TargetId = "contoso-demo",
            DataJson = "{}",
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAt = now,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static BudgetSnapshotRecord CreateBudget(
        string id,
        string scope,
        string entityName,
        long amount,
        decimal consumed,
        bool effective,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            BudgetId = id,
            BudgetType = "BundlePricing",
            BudgetScope = scope,
            BudgetEntityName = entityName,
            BudgetAmount = amount,
            ConsumedAmount = consumed,
            ProductSku = "ai_credits",
            PreventFurtherUsage = true,
            AlertingJson = "{\"willAlert\":true}",
            IsEffective = effective,
            ObservedAt = now,
        };

    private static ForecastSnapshotRecord CreateForecast(
        string budgetId,
        long budgetAmount,
        decimal monthToDateSpend,
        decimal projectedSpend,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            BudgetId = budgetId,
            PeriodStart = new DateOnly(now.Year, now.Month, 1),
            PeriodEnd = new DateOnly(now.Year, now.Month, 1).AddMonths(1).AddDays(-1),
            AsOfDay = DateOnly.FromDateTime(now.UtcDateTime),
            BudgetAmount = budgetAmount,
            MonthToDateSpend = monthToDateSpend,
            ProjectedSpend = projectedSpend,
            UtilizationPercent = decimal.Round(projectedSpend / budgetAmount * 100m, 2),
            IsUsable = true,
            ComponentsJson = "{\"reasons\":[\"Development fixture\"]}",
            InputsFingerprint = $"demo-forecast-{budgetId}",
            EvaluatedAt = now,
        };

    private static ClassificationSnapshotRecord CreateClassification(
        Guid policyId,
        ScopeKind scope,
        string scopeId,
        string status,
        decimal? score,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            ScopeKind = scope,
            ScopeExternalId = scopeId,
            PolicyId = policyId,
            PolicyVersion = 1,
            Status = status,
            Score = score,
            ReasonsJson = JsonSerializer.Serialize(new[]
            {
                new
                {
                    code = score is null ? "metric.suppressed" : "metric.scored",
                    message = score is null
                        ? "Required telemetry was suppressed or unavailable."
                        : $"The weighted policy produced a score of {score:0.#}.",
                    metricKey = "daily_active_users",
                },
            }),
            InputsFingerprint = $"demo-{scope}-{scopeId}",
            EvaluatedAt = now,
        };

    private static IdentityMappingRecord CreateIdentity(
        string login,
        string? userPrincipalName,
        IdentityMappingStatus status,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            GitHubLogin = login,
            EntraObjectId = userPrincipalName is null ? null : Guid.NewGuid().ToString("D"),
            UserPrincipalName = userPrincipalName,
            Department = "Engineering",
            Status = status,
            Source = "demo",
            UpdatedAt = now,
        };

    private static ManagedEntityRecord CreateEntity(
        ScopeKind scopeKind,
        string externalId,
        string displayName,
        ScopeKind? parentScopeKind,
        string? parentExternalId,
        DateTimeOffset now,
        string? metadataJson = null) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            ScopeKind = scopeKind,
            ExternalId = externalId,
            DisplayName = displayName,
            ParentScopeKind = parentScopeKind,
            ParentExternalId = parentExternalId,
            MetadataJson = metadataJson,
            IsActive = true,
            ObservedAt = now,
        };

    private static DailyMetricRecord CreateMetric(
        ScopeKind scopeKind,
        string scopeExternalId,
        string metricKey,
        decimal? metricValue,
        DateOnly metricDay,
        DateTimeOffset now,
        string availability = "available",
        string? availabilityDetail = null) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            ScopeKind = scopeKind,
            ScopeExternalId = scopeExternalId,
            MetricKey = metricKey,
            MetricValue = metricValue,
            Availability = availability,
            AvailabilityDetail = availabilityDetail,
            Source = "demo",
            MetricDay = metricDay,
            IngestedAt = now,
        };

    private static EntityMembershipRecord CreateMembership(
        ScopeKind parentScopeKind,
        string parentExternalId,
        ScopeKind memberScopeKind,
        string memberExternalId,
        DateOnly observedOn) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            ParentScopeKind = parentScopeKind,
            ParentExternalId = parentExternalId,
            MemberScopeKind = memberScopeKind,
            MemberExternalId = memberExternalId,
            ObservedOn = observedOn,
        };
}
