using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using BudgetManager.Application.Budgets;
using BudgetManager.Application.Classification;
using BudgetManager.Application.GitHub;
using BudgetManager.Application.Identity;
using BudgetManager.Application.Messaging;
using BudgetManager.Application.Notifications;
using BudgetManager.Application.Operations;
using BudgetManager.Application.Reports;
using BudgetManager.Infrastructure.GitHub;
using BudgetManager.Infrastructure.Identity;
using BudgetManager.Infrastructure.Messaging;
using BudgetManager.Infrastructure.Persistence;
using BudgetManager.Infrastructure.Reports;
using BudgetManager.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

const string workerHelp = """
        GitHub Copilot Budget Manager worker

        Usage:
            dotnet run --project src/BudgetManager.Worker -- <command> [options]

        Budget automation:
            sync-budgets                  Read current GitHub budgets into SQL.
            execute-approved [request-id] Apply one request, or up to 100 approved requests.

        Notification automation:
            classify                      Create health signals consumed by notification planning.
            plan-notifications            Create deduplicated notification and outbox records.
            dispatch-outbox               Publish pending outbox records to Service Bus.

        Supporting operations:
            migrate                       Apply SQL migrations.
            ingest-daily [yyyy-MM-dd]     Ingest Copilot metrics; defaults to three days ago.
            sync-identities               Refresh GitHub-to-Entra identity mappings.
            forecast                      Refresh budget forecasts.
            reconcile-baselines [--dry-run]
            apply-retention [--dry-run]

        Required environment variables:
            All commands: BUDGET_MANAGER_SQL_CONNECTION_STRING
            Enterprise commands: GITHUB_ENTERPRISE_ID
            GitHub commands: GITHUB_ENTERPRISE_SLUG, GITHUB_APP_ISSUER,
                GITHUB_APP_INSTALLATION_ID, GITHUB_APP_PRIVATE_KEY
            Budget execution: BUDGET_WRITES_ENABLED=true
            Notification planning: recipients come from mappings, owners, or semicolon-separated
                NOTIFICATION_ADMIN_RECIPIENTS; DASHBOARD_URL is optional
            Outbox dispatch: SERVICE_BUS_NAMESPACE, SERVICE_BUS_QUEUE_NAME
            Optional email-only mode: NOTIFICATION_CHANNELS=Outlook
            Optional for budget-only use: PUBLISH_BUDGET_LIFECYCLE_EVENTS=false

        Safety:
            execute-approved never creates or approves a request. It re-reads GitHub and only
            updates requests already approved by the application. Notification planning does
            not send email; dispatch and an authorized Logic Apps or Power Automate flow are
            separate steps.
        """;
string[] knownCommands =
[
        "migrate",
        "sync-budgets",
        "ingest-daily",
        "sync-identities",
        "classify",
        "forecast",
        "plan-notifications",
        "reconcile-baselines",
        "apply-retention",
        "execute-approved",
        "dispatch-outbox",
];

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(workerHelp);
    return;
}

var command = args[0].ToLowerInvariant();
if (!knownCommands.Contains(command, StringComparer.Ordinal))
{
    Console.Error.WriteLine($"Unknown worker command '{command}'.\n\n{workerHelp}");
    Environment.ExitCode = 2;
    return;
}

if (command == "execute-approved"
    && (!bool.TryParse(Environment.GetEnvironmentVariable("BUDGET_WRITES_ENABLED"), out var budgetWritesEnabled)
        || !budgetWritesEnabled))
{
    Console.Error.WriteLine(
        "Budget execution is disabled. Set BUDGET_WRITES_ENABLED=true only after completing read-only validation and granting approved GitHub write access.");
    Environment.ExitCode = 3;
    return;
}

var requiresGitHub = command is "sync-budgets" or "ingest-daily" or "execute-approved";
var runtimeOptions = WorkerRuntimeOptions.FromEnvironment(command);
var publishBudgetLifecycleEventsText = Environment.GetEnvironmentVariable(
    "PUBLISH_BUDGET_LIFECYCLE_EVENTS");
if (!string.IsNullOrWhiteSpace(publishBudgetLifecycleEventsText)
    && !bool.TryParse(publishBudgetLifecycleEventsText, out _))
{
    throw new InvalidOperationException(
        "PUBLISH_BUDGET_LIFECYCLE_EVENTS must be 'true' or 'false' when configured.");
}

var publishBudgetLifecycleEvents = string.IsNullOrWhiteSpace(publishBudgetLifecycleEventsText)
    || bool.Parse(publishBudgetLifecycleEventsText);
var workflowEventOptions = new WorkflowEventOptions(
    Environment.GetEnvironmentVariable("DASHBOARD_URL"),
    (Environment.GetEnvironmentVariable("NOTIFICATION_ADMIN_RECIPIENTS") ?? string.Empty)
        .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
    publishBudgetLifecycleEvents);

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(runtimeOptions);
builder.Services.AddSingleton(workflowEventOptions);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddBudgetManagerSqlPersistence(runtimeOptions.SqlConnectionString);

if (requiresGitHub)
{
    builder.Services.AddHttpClient("github", client => client.Timeout = TimeSpan.FromSeconds(100));
    builder.Services.AddHttpClient("signed-reports", client => client.Timeout = TimeSpan.FromMinutes(10));
    builder.Services.AddSingleton<IGitHubAppPrivateKeyProvider, EnvironmentGitHubAppPrivateKeyProvider>();
    builder.Services.AddSingleton(new GitHubAppTokenOptions(
        runtimeOptions.GitHubAppIssuer!,
        runtimeOptions.GitHubAppInstallationId));
    builder.Services.AddSingleton<IGitHubAccessTokenProvider>(provider => new GitHubAppAccessTokenProvider(
        provider.GetRequiredService<IHttpClientFactory>().CreateClient("github"),
        provider.GetRequiredService<IGitHubAppPrivateKeyProvider>(),
        provider.GetRequiredService<GitHubAppTokenOptions>()));
    builder.Services.AddScoped<IGitHubBudgetClient>(provider => new GitHubBudgetClient(
        provider.GetRequiredService<IHttpClientFactory>().CreateClient("github"),
        provider.GetRequiredService<IGitHubAccessTokenProvider>()));
    builder.Services.AddScoped<IGitHubEnterpriseClient>(provider => new GitHubEnterpriseClient(
        provider.GetRequiredService<IHttpClientFactory>().CreateClient("github"),
        provider.GetRequiredService<IGitHubAccessTokenProvider>()));
    builder.Services.AddScoped<IBudgetSnapshotStore, EfBudgetSnapshotStore>();
    builder.Services.AddScoped<BudgetSynchronizationService>();
    builder.Services.AddScoped<IBudgetChangeRepository, EfBudgetChangeRepository>();
    builder.Services.AddScoped<BudgetWriteExecutor>();
    if (command == "ingest-daily")
    {
        builder.Services.AddSingleton(new SignedReportDownloadOptions(runtimeOptions.SignedReportAllowedHosts));
        builder.Services.AddScoped<ISignedReportDownloader>(provider => new HttpSignedReportDownloader(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("signed-reports"),
            provider.GetRequiredService<SignedReportDownloadOptions>()));
        builder.Services.AddSingleton(new BlobContainerClient(
            runtimeOptions.ReportBlobContainerUri,
            new DefaultAzureCredential()));
        builder.Services.AddScoped<IReportArchive, AzureBlobReportArchive>();
        builder.Services.AddScoped<ICopilotMetricParser, CopilotNdjsonMetricParser>();
        builder.Services.AddScoped<IReportIngestionStore, EfReportIngestionStore>();
        builder.Services.AddScoped<CopilotReportIngestionService>();
    }
}

if (command == "dispatch-outbox")
{
    builder.Services.AddSingleton(new ServiceBusClient(
        runtimeOptions.ServiceBusNamespace,
        new DefaultAzureCredential()));
    builder.Services.AddSingleton(provider => provider
        .GetRequiredService<ServiceBusClient>()
        .CreateSender(runtimeOptions.ServiceBusQueueName));
    builder.Services.AddScoped<IOutboxStore, EfOutboxStore>();
    builder.Services.AddScoped<IIntegrationEventPublisher, ServiceBusIntegrationEventPublisher>();
    builder.Services.AddScoped<OutboxDispatcher>();
}

if (command == "sync-identities")
{
    builder.Services.AddHttpClient("graph", client => client.Timeout = TimeSpan.FromMinutes(2));
    builder.Services.AddSingleton<Azure.Core.TokenCredential>(new DefaultAzureCredential());
    builder.Services.AddSingleton(new GraphDirectoryOptions(runtimeOptions.GraphGitHubLoginProperty!));
    builder.Services.AddScoped<IEntraDirectoryClient>(provider => new GraphDirectoryClient(
        provider.GetRequiredService<IHttpClientFactory>().CreateClient("graph"),
        provider.GetRequiredService<Azure.Core.TokenCredential>(),
        provider.GetRequiredService<GraphDirectoryOptions>()));
    builder.Services.AddScoped<IIdentityMappingStore, EfIdentityMappingStore>();
    builder.Services.AddScoped<IdentitySynchronizationService>();
}

if (command == "classify")
{
    builder.Services.AddScoped<IClassificationStore, EfClassificationStore>();
    builder.Services.AddScoped<ClassificationRunService>();
}

if (command == "forecast")
{
    builder.Services.AddScoped<IBudgetForecastStore, EfBudgetForecastStore>();
    builder.Services.AddScoped<BudgetForecastService>();
}

if (command == "apply-retention")
{
    builder.Services.AddScoped<IRetentionStore, EfRetentionStore>();
    builder.Services.AddScoped<RetentionService>();
}

if (command == "reconcile-baselines")
{
    builder.Services.AddScoped<IBaselineReconciliationStore, EfBaselineReconciliationStore>();
    builder.Services.AddScoped<BaselineReconciliationService>();
}

if (command == "plan-notifications")
{
    var channels = (Environment.GetEnvironmentVariable("NOTIFICATION_CHANNELS") ?? "Teams;Outlook")
        .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    builder.Services.AddSingleton(new NotificationDeliveryOptions(channels));
    builder.Services.AddScoped<IHealthNotificationStore, EfHealthNotificationStore>();
    builder.Services.AddScoped<HealthNotificationPlanner>();
}

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var cancellationToken = CancellationToken.None;
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BudgetManager.Worker");
WorkerLog.CommandStarted(logger, command);

try
{
    switch (command)
    {
        case "migrate":
            await scope.ServiceProvider
                .GetRequiredService<BudgetManagerDbContext>()
                .Database.MigrateAsync(cancellationToken);
            break;

        case "sync-budgets":
            var synchronization = await scope.ServiceProvider
                .GetRequiredService<BudgetSynchronizationService>()
                .SynchronizeAsync(
                    runtimeOptions.EnterpriseId,
                    runtimeOptions.EnterpriseSlug!,
                    cancellationToken);
            WorkerLog.BudgetsSynchronized(
                logger,
                synchronization.BudgetCount,
                synchronization.CostCenterCount,
                synchronization.UserStateCount);
            break;

        case "ingest-daily":
            var reportDay = args.Length > 1
                ? DateOnly.ParseExact(args[1], "yyyy-MM-dd")
                : DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3);
            var ingestionService = scope.ServiceProvider.GetRequiredService<CopilotReportIngestionService>();
            foreach (var reportKind in new[]
            {
                CopilotMetricReportKind.EnterpriseDay,
                CopilotMetricReportKind.UsersDay,
                CopilotMetricReportKind.RepositoriesDay,
                CopilotMetricReportKind.UserTeamsDay,
            })
            {
                var ingestion = await ingestionService.IngestAsync(
                    runtimeOptions.EnterpriseId,
                    runtimeOptions.EnterpriseSlug!,
                    reportKind,
                    reportDay,
                    cancellationToken);
                WorkerLog.ReportIngested(
                    logger,
                    ingestion.FileCount,
                    reportKind,
                    ingestion.MetricCount,
                    reportDay);
            }

            break;

        case "execute-approved":
            var repository = scope.ServiceProvider.GetRequiredService<IBudgetChangeRepository>();
            var executor = scope.ServiceProvider.GetRequiredService<BudgetWriteExecutor>();
            var requestIds = args.Length > 1
                ? new[] { Guid.Parse(args[1]) }
                : await repository.GetApprovedRequestIdsAsync(100, cancellationToken);
            var failedWrites = 0;
            foreach (var requestId in requestIds)
            {
                var outcome = await executor.ExecuteAsync(requestId, cancellationToken);
                if (outcome.Outcome == BudgetWriteOutcomeKind.Failed)
                {
                    failedWrites++;
                    WorkerLog.BudgetOutcomeError(
                        logger,
                        requestId,
                        outcome.Outcome,
                        outcome.Message);
                }
                else if (outcome.Outcome is BudgetWriteOutcomeKind.Conflict or BudgetWriteOutcomeKind.NotExecutable)
                {
                    WorkerLog.BudgetOutcomeWarning(
                        logger,
                        requestId,
                        outcome.Outcome,
                        outcome.Message);
                }
            }

            if (failedWrites > 0)
            {
                throw new InvalidOperationException($"{failedWrites} approved budget writes failed.");
            }

            break;

        case "dispatch-outbox":
            var dispatch = await scope.ServiceProvider
                .GetRequiredService<OutboxDispatcher>()
                .DispatchAsync(100, cancellationToken);
            WorkerLog.OutboxDispatched(
                logger,
                dispatch.Published,
                dispatch.Failed);
            if (dispatch.Failed > 0)
            {
                throw new InvalidOperationException($"{dispatch.Failed} outbox messages failed to publish.");
            }

            break;

        case "sync-identities":
            await scope.ServiceProvider
                .GetRequiredService<IdentitySynchronizationService>()
                .SynchronizeAsync(runtimeOptions.EnterpriseId, cancellationToken);
            break;

        case "classify":
            await scope.ServiceProvider
                .GetRequiredService<ClassificationRunService>()
                .RunAsync(runtimeOptions.EnterpriseId, cancellationToken);
            break;

        case "forecast":
            await scope.ServiceProvider
                .GetRequiredService<BudgetForecastService>()
                .RunAsync(runtimeOptions.EnterpriseId, null, cancellationToken);
            break;

        case "apply-retention":
            await scope.ServiceProvider
                .GetRequiredService<RetentionService>()
                .ExecuteAsync(runtimeOptions.EnterpriseId, args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase), cancellationToken);
            break;

        case "reconcile-baselines":
            await scope.ServiceProvider
                .GetRequiredService<BaselineReconciliationService>()
                .ReconcileAsync(runtimeOptions.EnterpriseId, args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase), cancellationToken);
            break;

        case "plan-notifications":
            await scope.ServiceProvider
                .GetRequiredService<HealthNotificationPlanner>()
                .PlanAsync(
                    runtimeOptions.EnterpriseId,
                    workflowEventOptions.AdminPrincipalNames,
                    workflowEventOptions.DashboardUrl,
                    cancellationToken);
            break;

        default:
            throw new InvalidOperationException(
                $"Unknown worker command '{command}'.");
    }

    WorkerLog.CommandCompleted(logger, command);
}
catch (Exception exception)
{
    WorkerLog.CommandFailed(logger, exception, command);
    throw;
}
