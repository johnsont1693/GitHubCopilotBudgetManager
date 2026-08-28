using BudgetManager.Application.Budgets;
using BudgetManager.Application.GitHub;
using BudgetManager.Application.Reports;
using Microsoft.Extensions.Logging;

namespace BudgetManager.Worker;

internal static partial class WorkerLog
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Worker command {Command} started")]
    internal static partial void CommandStarted(ILogger logger, string command);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Information,
        Message = "Synchronized {BudgetCount} budgets, {CostCenterCount} cost centers, and {UserStateCount} user states")]
    internal static partial void BudgetsSynchronized(
        ILogger logger,
        int budgetCount,
        int costCenterCount,
        int userStateCount);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Information,
        Message = "Ingested {FileCount} {ReportKind} files containing {MetricCount} metrics for {ReportDay}")]
    internal static partial void ReportIngested(
        ILogger logger,
        int fileCount,
        CopilotMetricReportKind reportKind,
        int metricCount,
        DateOnly reportDay);

    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Error,
        Message = "Budget request {RequestId} finished with outcome {Outcome}: {Message}")]
    internal static partial void BudgetOutcomeError(
        ILogger logger,
        Guid requestId,
        BudgetWriteOutcomeKind outcome,
        string message);

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Warning,
        Message = "Budget request {RequestId} finished with outcome {Outcome}: {Message}")]
    internal static partial void BudgetOutcomeWarning(
        ILogger logger,
        Guid requestId,
        BudgetWriteOutcomeKind outcome,
        string message);

    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Information,
        Message = "Outbox dispatch published {PublishedCount} messages and failed {FailedCount}")]
    internal static partial void OutboxDispatched(ILogger logger, int publishedCount, int failedCount);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Information, Message = "Worker command {Command} completed")]
    internal static partial void CommandCompleted(ILogger logger, string command);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Critical, Message = "Worker command {Command} failed")]
    internal static partial void CommandFailed(ILogger logger, Exception exception, string command);
}
