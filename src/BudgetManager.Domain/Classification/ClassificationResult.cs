namespace BudgetManager.Domain.Classification;

public sealed record ClassificationReason(string Code, string Message, string? MetricKey = null);

public sealed record ClassificationResult(
    HealthStatus Status,
    decimal? Score,
    IReadOnlyList<ClassificationReason> Reasons);
