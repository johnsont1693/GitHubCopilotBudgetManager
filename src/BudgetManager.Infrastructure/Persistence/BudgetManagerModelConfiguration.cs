using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Persistence;

internal static class BudgetManagerModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        ConfigureEnterprise(modelBuilder);
        ConfigureHierarchy(modelBuilder);
        ConfigureAnalytics(modelBuilder);
        ConfigureWorkflow(modelBuilder);
    }

    private static void ConfigureEnterprise(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EnterpriseRecord>(entity =>
        {
            entity.ToTable("Enterprises");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Slug).HasMaxLength(100);
            entity.Property(item => item.DisplayName).HasMaxLength(256);
            entity.HasIndex(item => item.Slug).IsUnique();
        });

        modelBuilder.Entity<IdentityMappingRecord>(entity =>
        {
            entity.ToTable("IdentityMappings");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.GitHubLogin).HasMaxLength(100);
            entity.Property(item => item.EntraObjectId).HasMaxLength(64);
            entity.Property(item => item.UserPrincipalName).HasMaxLength(320);
            entity.Property(item => item.Department).HasMaxLength(128);
            entity.Property(item => item.EntraCostCenterCode).HasMaxLength(128);
            entity.Property(item => item.GitHubCostCenterId).HasMaxLength(128);
            entity.Property(item => item.Source).HasMaxLength(64);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(item => new { item.EnterpriseId, item.GitHubLogin }).IsUnique();
            entity.HasIndex(item => new { item.EnterpriseId, item.EntraObjectId });
        });

        modelBuilder.Entity<RetentionPolicyRecord>(entity =>
        {
            entity.ToTable("RetentionPolicies");
            entity.HasKey(item => item.EnterpriseId);
        });
    }

    private static void ConfigureHierarchy(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ManagedEntityRecord>(entity =>
        {
            entity.ToTable("ManagedEntities");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ScopeKind).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ExternalId).HasMaxLength(256);
            entity.Property(item => item.DisplayName).HasMaxLength(512);
            entity.Property(item => item.ParentScopeKind).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ParentExternalId).HasMaxLength(256);
            entity.HasIndex(item => new { item.EnterpriseId, item.ScopeKind, item.ExternalId }).IsUnique();
            entity.HasIndex(item => new
            {
                item.EnterpriseId,
                item.ParentScopeKind,
                item.ParentExternalId,
            });
        });

        modelBuilder.Entity<EntityMembershipRecord>(entity =>
        {
            entity.ToTable("EntityMemberships");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ParentScopeKind).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.MemberScopeKind).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ParentExternalId).HasMaxLength(256);
            entity.Property(item => item.MemberExternalId).HasMaxLength(256);
            entity.HasIndex(item => new
            {
                item.EnterpriseId,
                item.ParentScopeKind,
                item.ParentExternalId,
                item.MemberScopeKind,
                item.MemberExternalId,
                item.ObservedOn,
            }).IsUnique();
        });
    }

    private static void ConfigureAnalytics(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DailyMetricRecord>(entity =>
        {
            entity.ToTable("DailyMetrics");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ScopeKind).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ScopeExternalId).HasMaxLength(256);
            entity.Property(item => item.MetricKey).HasMaxLength(128);
            entity.Property(item => item.MetricValue).HasPrecision(28, 8);
            entity.Property(item => item.Availability).HasMaxLength(32);
            entity.Property(item => item.Source).HasMaxLength(64);
            entity.HasIndex(item => new
            {
                item.EnterpriseId,
                item.ScopeKind,
                item.ScopeExternalId,
                item.MetricKey,
                item.MetricDay,
            }).IsUnique();
            entity.HasIndex(item => new { item.EnterpriseId, item.MetricDay, item.MetricKey });
        });

        modelBuilder.Entity<PolicyRecord>(entity =>
        {
            entity.ToTable("Policies");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(256);
            entity.Property(item => item.Mode).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.Governance).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ScopeKind).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ScopeExternalId).HasMaxLength(256);
            entity.Property(item => item.CreatedBy).HasMaxLength(128);
            entity.HasIndex(item => new { item.EnterpriseId, item.Name, item.Version }).IsUnique();
            entity.HasIndex(item => new
            {
                item.EnterpriseId,
                item.ScopeKind,
                item.ScopeExternalId,
                item.IsActive,
            });
        });

        modelBuilder.Entity<ClassificationSnapshotRecord>(entity =>
        {
            entity.ToTable("ClassificationSnapshots");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ScopeKind).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ScopeExternalId).HasMaxLength(256);
            entity.Property(item => item.Status).HasMaxLength(32);
            entity.Property(item => item.Score).HasPrecision(9, 4);
            entity.Property(item => item.InputsFingerprint).HasMaxLength(128);
            entity.HasIndex(item => new
            {
                item.EnterpriseId,
                item.ScopeKind,
                item.ScopeExternalId,
                item.EvaluatedAt,
            });
            entity.HasIndex(item => new { item.PolicyId, item.InputsFingerprint }).IsUnique();
        });

        modelBuilder.Entity<IngestionManifestRecord>(entity =>
        {
            entity.ToTable("IngestionManifests");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ReportType).HasMaxLength(64);
            entity.Property(item => item.BlobName).HasMaxLength(1024);
            entity.Property(item => item.SourceUrlHash).HasMaxLength(128);
            entity.Property(item => item.ContentSha256).HasMaxLength(128);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ErrorCode).HasMaxLength(128);
            entity.HasIndex(item => new
            {
                item.EnterpriseId,
                item.ReportType,
                item.ReportStartDay,
                item.ReportEndDay,
                item.ContentSha256,
            }).IsUnique();
            entity.HasIndex(item => new { item.EnterpriseId, item.Status, item.StartedAt });
        });

        modelBuilder.Entity<ForecastSnapshotRecord>(entity =>
        {
            entity.ToTable("ForecastSnapshots");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.BudgetId).HasMaxLength(128);
            entity.Property(item => item.MonthToDateSpend).HasPrecision(28, 8);
            entity.Property(item => item.ProjectedSpend).HasPrecision(28, 8);
            entity.Property(item => item.UtilizationPercent).HasPrecision(9, 4);
            entity.Property(item => item.InputsFingerprint).HasMaxLength(128);
            entity.HasIndex(item => new { item.EnterpriseId, item.BudgetId, item.InputsFingerprint }).IsUnique();
            entity.HasIndex(item => new { item.EnterpriseId, item.AsOfDay });
        });
    }

    private static void ConfigureWorkflow(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BudgetUserStateSnapshotRecord>(entity =>
        {
            entity.ToTable("BudgetUserStateSnapshots");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.BudgetId).HasMaxLength(128);
            entity.Property(item => item.UserLogin).HasMaxLength(100);
            entity.Property(item => item.ConsumedAmount).HasPrecision(28, 8);
            entity.Property(item => item.TargetAmount).HasPrecision(28, 8);
            entity.Property(item => item.OverrideBudgetId).HasMaxLength(128);
            entity.HasIndex(item => new
            {
                item.EnterpriseId,
                item.BudgetId,
                item.UserLogin,
                item.ObservedAt,
            }).IsUnique();
            entity.HasIndex(item => new { item.EnterpriseId, item.UserLogin, item.ObservedAt });
        });

        modelBuilder.Entity<BillingReportExportRecord>(entity =>
        {
            entity.ToTable("BillingReportExports");
            entity.HasKey(item => new { item.EnterpriseId, item.ReportId });
            entity.Property(item => item.ReportType).HasMaxLength(64);
            entity.Property(item => item.Status).HasMaxLength(32);
            entity.Property(item => item.Actor).HasMaxLength(100);
            entity.HasIndex(item => new { item.EnterpriseId, item.Status, item.LastObservedAt });
        });

        modelBuilder.Entity<BudgetSnapshotRecord>(entity =>
        {
            entity.ToTable("BudgetSnapshots");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.BudgetId).HasMaxLength(128);
            entity.Property(item => item.BudgetType).HasMaxLength(64);
            entity.Property(item => item.BudgetScope).HasMaxLength(64);
            entity.Property(item => item.BudgetEntityName).HasMaxLength(512);
            entity.Property(item => item.UserLogin).HasMaxLength(100);
            entity.Property(item => item.ConsumedAmount).HasPrecision(28, 8);
            entity.Property(item => item.ProductSku).HasMaxLength(128);
            entity.HasIndex(item => new { item.EnterpriseId, item.BudgetId, item.ObservedAt }).IsUnique();
            entity.HasIndex(item => new { item.EnterpriseId, item.IsEffective, item.ObservedAt });
        });

        modelBuilder.Entity<BudgetChangeRequestRecord>(entity =>
        {
            entity.ToTable("BudgetChangeRequests");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.BudgetId).HasMaxLength(128);
            entity.Property(item => item.ForecastAmount).HasPrecision(28, 8);
            entity.Property(item => item.DataFingerprint).HasMaxLength(128);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.FailureCode).HasMaxLength(128);
            entity.Property(item => item.ConcurrencyToken).IsConcurrencyToken();
            entity.HasIndex(item => new { item.EnterpriseId, item.BudgetId, item.DataFingerprint }).IsUnique();
            entity.HasIndex(item => new { item.EnterpriseId, item.Status, item.CreatedAt });
        });

        modelBuilder.Entity<ApprovalDecisionRecord>(entity =>
        {
            entity.ToTable("ApprovalDecisions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Decision).HasMaxLength(32);
            entity.Property(item => item.ActorObjectId).HasMaxLength(64);
            entity.Property(item => item.CallbackNonceHash).HasMaxLength(128);
            entity.HasIndex(item => item.BudgetChangeRequestId).IsUnique();
            entity.HasIndex(item => item.CallbackNonceHash).IsUnique();
        });

        modelBuilder.Entity<BudgetBaselineRecord>(entity =>
        {
            entity.ToTable("BudgetBaselines");
            entity.HasKey(item => new { item.EnterpriseId, item.BudgetId });
            entity.Property(item => item.BudgetId).HasMaxLength(128);
            entity.Property(item => item.Mode).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.UpdatedBy).HasMaxLength(128);
        });

        modelBuilder.Entity<NotificationDeliveryRecord>(entity =>
        {
            entity.ToTable("NotificationDeliveries");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.EventFingerprint).HasMaxLength(128);
            entity.Property(item => item.Channel).HasMaxLength(32);
            entity.Property(item => item.RecipientKey).HasMaxLength(320);
            entity.Property(item => item.TemplateVersion).HasMaxLength(64);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ProviderMessageId).HasMaxLength(256);
            entity.Property(item => item.LastErrorCode).HasMaxLength(128);
            entity.HasIndex(item => new
            {
                item.EnterpriseId,
                item.EventFingerprint,
                item.Channel,
                item.RecipientKey,
            }).IsUnique();
        });

        modelBuilder.Entity<OutboxMessageRecord>(entity =>
        {
            entity.ToTable("OutboxMessages");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.EventType).HasMaxLength(128);
            entity.Property(item => item.EventFingerprint).HasMaxLength(128);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.LastErrorCode).HasMaxLength(128);
            entity.HasIndex(item => new { item.EnterpriseId, item.EventFingerprint }).IsUnique();
            entity.HasIndex(item => new { item.Status, item.OccurredAt });
        });

        modelBuilder.Entity<AuditEventRecord>(entity =>
        {
            entity.ToTable("AuditEvents");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.EventType).HasMaxLength(128);
            entity.Property(item => item.ActorType).HasMaxLength(32);
            entity.Property(item => item.ActorId).HasMaxLength(128);
            entity.Property(item => item.TargetType).HasMaxLength(64);
            entity.Property(item => item.TargetId).HasMaxLength(256);
            entity.Property(item => item.CorrelationId).HasMaxLength(128);
            entity.HasIndex(item => new { item.EnterpriseId, item.OccurredAt });
            entity.HasIndex(item => new { item.EnterpriseId, item.EventType, item.OccurredAt });
        });
    }
}
