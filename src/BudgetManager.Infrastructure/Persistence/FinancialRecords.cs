namespace BudgetManager.Infrastructure.Persistence;

public sealed class BudgetUserStateSnapshotRecord
{
    public Guid Id { get; set; }

    public Guid EnterpriseId { get; set; }

    public required string BudgetId { get; set; }

    public required string UserLogin { get; set; }

    public decimal ConsumedAmount { get; set; }

    public decimal TargetAmount { get; set; }

    public string? OverrideBudgetId { get; set; }

    public DateTimeOffset ObservedAt { get; set; }
}

public sealed class BillingReportExportRecord
{
    public Guid EnterpriseId { get; set; }

    public Guid ReportId { get; set; }

    public required string ReportType { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public required string Status { get; set; }

    public int DownloadUrlCount { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public string? Actor { get; set; }

    public DateTimeOffset FirstObservedAt { get; set; }

    public DateTimeOffset LastObservedAt { get; set; }
}
