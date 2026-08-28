using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Persistence;

public sealed class BudgetManagerDbContext(DbContextOptions<BudgetManagerDbContext> options)
    : DbContext(options)
{
    public DbSet<EnterpriseRecord> Enterprises => Set<EnterpriseRecord>();

    public DbSet<ManagedEntityRecord> ManagedEntities => Set<ManagedEntityRecord>();

    public DbSet<EntityMembershipRecord> EntityMemberships => Set<EntityMembershipRecord>();

    public DbSet<IdentityMappingRecord> IdentityMappings => Set<IdentityMappingRecord>();

    public DbSet<RetentionPolicyRecord> RetentionPolicies => Set<RetentionPolicyRecord>();

    public DbSet<DailyMetricRecord> DailyMetrics => Set<DailyMetricRecord>();

    public DbSet<PolicyRecord> Policies => Set<PolicyRecord>();

    public DbSet<ClassificationSnapshotRecord> ClassificationSnapshots => Set<ClassificationSnapshotRecord>();

    public DbSet<IngestionManifestRecord> IngestionManifests => Set<IngestionManifestRecord>();

    public DbSet<ForecastSnapshotRecord> ForecastSnapshots => Set<ForecastSnapshotRecord>();

    public DbSet<BudgetSnapshotRecord> BudgetSnapshots => Set<BudgetSnapshotRecord>();

    public DbSet<BudgetUserStateSnapshotRecord> BudgetUserStateSnapshots => Set<BudgetUserStateSnapshotRecord>();

    public DbSet<BillingReportExportRecord> BillingReportExports => Set<BillingReportExportRecord>();

    public DbSet<BudgetChangeRequestRecord> BudgetChangeRequests => Set<BudgetChangeRequestRecord>();

    public DbSet<ApprovalDecisionRecord> ApprovalDecisions => Set<ApprovalDecisionRecord>();

    public DbSet<BudgetBaselineRecord> BudgetBaselines => Set<BudgetBaselineRecord>();

    public DbSet<NotificationDeliveryRecord> NotificationDeliveries => Set<NotificationDeliveryRecord>();

    public DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();

    public DbSet<AuditEventRecord> AuditEvents => Set<AuditEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        BudgetManagerModelConfiguration.Configure(modelBuilder);
    }
}
