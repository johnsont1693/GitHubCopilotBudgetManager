namespace BudgetManager.Worker;

public sealed class WorkerRuntimeOptions
{
    public required string SqlConnectionString { get; init; }

    public Guid EnterpriseId { get; init; }

    public string? EnterpriseSlug { get; init; }

    public string? GitHubAppIssuer { get; init; }

    public long GitHubAppInstallationId { get; init; }

    public Uri? ReportBlobContainerUri { get; init; }

    public IReadOnlyList<string> SignedReportAllowedHosts { get; init; } = [];

    public string? ServiceBusNamespace { get; init; }

    public string? ServiceBusQueueName { get; init; }

    public string? GraphGitHubLoginProperty { get; init; }

    public static WorkerRuntimeOptions FromEnvironment(string command)
    {
        var sqlConnectionString = RequireEnvironmentVariable("BUDGET_MANAGER_SQL_CONNECTION_STRING");
        var requireGitHub = command is "sync-budgets" or "ingest-daily" or "execute-approved";
        var requireReports = command == "ingest-daily";
        var requireMessaging = command == "dispatch-outbox";
        var requireGraph = command == "sync-identities";
        var requireEnterpriseOnly = command is "classify" or "forecast" or "apply-retention" or "reconcile-baselines" or "plan-notifications";
        if (!requireGitHub && !requireMessaging && !requireGraph && !requireEnterpriseOnly)
        {
            return new WorkerRuntimeOptions { SqlConnectionString = sqlConnectionString };
        }

        if (requireMessaging)
        {
            return new WorkerRuntimeOptions
            {
                SqlConnectionString = sqlConnectionString,
                ServiceBusNamespace = RequireEnvironmentVariable("SERVICE_BUS_NAMESPACE"),
                ServiceBusQueueName = RequireEnvironmentVariable("SERVICE_BUS_QUEUE_NAME"),
            };
        }

        if (requireGraph)
        {
            var graphEnterpriseIdText = RequireEnvironmentVariable("GITHUB_ENTERPRISE_ID");
            if (!Guid.TryParse(graphEnterpriseIdText, out var graphEnterpriseId)
                || graphEnterpriseId == Guid.Empty)
            {
                throw new InvalidOperationException("GITHUB_ENTERPRISE_ID must be a non-empty GUID.");
            }

            return new WorkerRuntimeOptions
            {
                SqlConnectionString = sqlConnectionString,
                EnterpriseId = graphEnterpriseId,
                GraphGitHubLoginProperty = RequireEnvironmentVariable("GRAPH_GITHUB_LOGIN_PROPERTY"),
            };
        }

        if (requireEnterpriseOnly)
        {
            var enterpriseOnlyIdText = RequireEnvironmentVariable("GITHUB_ENTERPRISE_ID");
            if (!Guid.TryParse(enterpriseOnlyIdText, out var enterpriseOnlyId)
                || enterpriseOnlyId == Guid.Empty)
            {
                throw new InvalidOperationException("GITHUB_ENTERPRISE_ID must be a non-empty GUID.");
            }

            return new WorkerRuntimeOptions
            {
                SqlConnectionString = sqlConnectionString,
                EnterpriseId = enterpriseOnlyId,
            };
        }

        var enterpriseIdText = RequireEnvironmentVariable("GITHUB_ENTERPRISE_ID");
        if (!Guid.TryParse(enterpriseIdText, out var enterpriseId) || enterpriseId == Guid.Empty)
        {
            throw new InvalidOperationException("GITHUB_ENTERPRISE_ID must be a non-empty GUID.");
        }

        var installationIdText = RequireEnvironmentVariable("GITHUB_APP_INSTALLATION_ID");
        if (!long.TryParse(installationIdText, out var installationId) || installationId <= 0)
        {
            throw new InvalidOperationException("GITHUB_APP_INSTALLATION_ID must be a positive integer.");
        }

        Uri? containerUri = null;
        string[] allowedHosts = [];
        if (requireReports)
        {
            var containerUriText = RequireEnvironmentVariable("REPORT_BLOB_CONTAINER_URI");
            if (!Uri.TryCreate(containerUriText, UriKind.Absolute, out containerUri)
                || containerUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("REPORT_BLOB_CONTAINER_URI must be an absolute HTTPS URI.");
            }

            allowedHosts = RequireEnvironmentVariable("SIGNED_REPORT_ALLOWED_HOSTS")
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }

        return new WorkerRuntimeOptions
        {
            SqlConnectionString = sqlConnectionString,
            EnterpriseId = enterpriseId,
            EnterpriseSlug = RequireEnvironmentVariable("GITHUB_ENTERPRISE_SLUG"),
            GitHubAppIssuer = RequireEnvironmentVariable("GITHUB_APP_ISSUER"),
            GitHubAppInstallationId = installationId,
            ReportBlobContainerUri = containerUri,
            SignedReportAllowedHosts = allowedHosts,
        };
    }

    private static string RequireEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Required environment variable '{name}' is not configured.")
            : value;
    }
}
