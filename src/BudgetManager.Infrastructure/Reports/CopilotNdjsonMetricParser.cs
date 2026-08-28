using System.Text;
using System.Text.Json;
using BudgetManager.Application.GitHub;
using BudgetManager.Application.Reports;

namespace BudgetManager.Infrastructure.Reports;

public sealed class CopilotNdjsonMetricParser : ICopilotMetricParser
{
    private const int MaximumLineCharacters = 4_194_304;

    private static readonly HashSet<string> NumericMetricKeys = new(StringComparer.Ordinal)
    {
        "ai_credits_used",
        "user_initiated_interaction_count",
        "code_generation_activity_count",
        "code_acceptance_activity_count",
        "loc_suggested_to_add_sum",
        "loc_suggested_to_delete_sum",
        "loc_added_sum",
        "loc_deleted_sum",
        "daily_active_users",
        "weekly_active_users",
        "monthly_active_users",
        "daily_active_cli_users",
        "daily_active_copilot_app_users",
        "monthly_active_chat_users",
        "monthly_active_agent_users",
    };

    private static readonly HashSet<string> BooleanMetricKeys = new(StringComparer.Ordinal)
    {
        "used_agent",
        "used_chat",
        "used_cli",
        "used_copilot_app",
        "used_copilot_cloud_agent",
        "used_copilot_coding_agent",
        "used_copilot_code_review_active",
        "used_copilot_code_review_passive",
    };

    private static readonly HashSet<string> PullRequestMetricKeys = new(StringComparer.Ordinal)
    {
        "median_minutes_to_merge",
        "median_minutes_to_merge_copilot_authored",
        "median_minutes_to_merge_copilot_reviewed",
        "total_applied_suggestions",
        "total_copilot_applied_suggestions",
        "total_copilot_suggestions",
        "total_created",
        "total_created_by_copilot",
        "total_merged",
        "total_merged_created_by_copilot",
        "total_merged_reviewed_by_copilot",
        "total_reviewed",
        "total_reviewed_by_copilot",
        "total_suggestions",
    };

    public ParsedCopilotReport Parse(
        ReadOnlyMemory<byte> content,
        string enterpriseSlug,
        CopilotMetricReportKind reportKind,
        DateOnly fallbackDay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(enterpriseSlug);
        var metrics = new List<NormalizedMetric>();
        var entities = new Dictionary<string, NormalizedEntity>(StringComparer.OrdinalIgnoreCase);
        var memberships = new HashSet<NormalizedMembership>();
        AddEntity(entities, new NormalizedEntity("Enterprise", enterpriseSlug, enterpriseSlug, null, null, null));
        var text = Encoding.UTF8.GetString(content.Span);
        using var reader = new StringReader(text);
        var source = $"copilot.{reportKind}";

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.Length > MaximumLineCharacters)
            {
                throw new InvalidDataException("Copilot report line exceeds the parser limit.");
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var metricDay = TryGetDate(root, "day") ?? fallbackDay;
            var (scopeKind, scopeId) = ResolveScope(root, enterpriseSlug, reportKind);
            CaptureHierarchy(root, enterpriseSlug, metricDay, entities, memberships);

            foreach (var property in root.EnumerateObject())
            {
                if (NumericMetricKeys.Contains(property.Name)
                    && property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetDecimal(out var number))
                {
                    metrics.Add(CreateMetric(scopeKind, scopeId, property.Name, number, source, metricDay));
                }
                else if (BooleanMetricKeys.Contains(property.Name)
                    && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    metrics.Add(CreateMetric(
                        scopeKind,
                        scopeId,
                        property.Name,
                        property.Value.GetBoolean() ? 1m : 0m,
                        source,
                        metricDay));
                }
            }

            AddPullRequestMetrics(root, scopeKind, scopeId, source, metricDay, metrics);
        }

        return new ParsedCopilotReport(
            metrics.AsReadOnly(),
            entities.Values.ToArray(),
            memberships.ToArray());
    }

    private static void AddPullRequestMetrics(
        JsonElement root,
        string scopeKind,
        string scopeId,
        string source,
        DateOnly metricDay,
        List<NormalizedMetric> metrics)
    {
        if (!root.TryGetProperty("pull_requests", out var pullRequests)
            || pullRequests.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in pullRequests.EnumerateObject())
        {
            if (PullRequestMetricKeys.Contains(property.Name)
                && property.Value.ValueKind == JsonValueKind.Number
                && property.Value.TryGetDecimal(out var value))
            {
                metrics.Add(CreateMetric(
                    scopeKind,
                    scopeId,
                    $"pull_requests.{property.Name}",
                    value,
                    source,
                    metricDay));
            }
        }
    }

    private static void CaptureHierarchy(
        JsonElement root,
        string enterpriseSlug,
        DateOnly metricDay,
        IDictionary<string, NormalizedEntity> entities,
        HashSet<NormalizedMembership> memberships)
    {
        var organizationId = TryGetString(root, "organization_id");
        var organizationName = TryGetString(root, "repo_owner_name") ?? organizationId;
        if (organizationId is not null)
        {
            AddEntity(entities, new NormalizedEntity(
                "Organization",
                organizationId,
                organizationName!,
                "Enterprise",
                enterpriseSlug,
                null));
        }

        var userLogin = TryGetString(root, "user_login");
        if (userLogin is not null)
        {
            AddEntity(entities, new NormalizedEntity(
                "User",
                userLogin,
                userLogin,
                organizationId is null ? "Enterprise" : "Organization",
                organizationId ?? enterpriseSlug,
                CreateMetadata(root, "user_id")));
        }

        var repositoryId = TryGetString(root, "repo_id");
        if (repositoryId is not null)
        {
            var repositoryName = TryGetString(root, "repo_name") ?? repositoryId;
            var repositoryOwner = TryGetString(root, "repo_owner_name");
            AddEntity(entities, new NormalizedEntity(
                "Repository",
                repositoryId,
                repositoryOwner is null ? repositoryName : $"{repositoryOwner}/{repositoryName}",
                organizationId is null ? "Enterprise" : "Organization",
                organizationId ?? enterpriseSlug,
                CreateMetadata(root, "repo_visibility", "repo_owner_name", "repo_name")));
        }

        var teamId = TryGetString(root, "team_id");
        var teamSlug = TryGetString(root, "slug");
        if (teamId is not null && teamSlug is not null)
        {
            AddEntity(entities, new NormalizedEntity(
                "Team",
                teamId,
                teamSlug,
                organizationId is null ? "Enterprise" : "Organization",
                organizationId ?? enterpriseSlug,
                JsonSerializer.Serialize(new { slug = teamSlug })));
            if (userLogin is not null)
            {
                memberships.Add(new NormalizedMembership(
                    "Team",
                    teamId,
                    "User",
                    userLogin,
                    metricDay));
            }
        }
    }

    private static void AddEntity(
        IDictionary<string, NormalizedEntity> entities,
        NormalizedEntity entity) => entities[$"{entity.ScopeKind}:{entity.ExternalId}"] = entity;

    private static string? CreateMetadata(JsonElement root, params string[] propertyNames)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var propertyName in propertyNames)
        {
            if (!root.TryGetProperty(propertyName, out var property))
            {
                continue;
            }

            values[propertyName] = property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.GetRawText(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        }

        return values.Count == 0 ? null : JsonSerializer.Serialize(values);
    }

    private static NormalizedMetric CreateMetric(
        string scopeKind,
        string scopeId,
        string key,
        decimal value,
        string source,
        DateOnly day) => new(
            scopeKind,
            scopeId,
            key,
            value,
            "available",
            null,
            source,
            day);

    private static (string ScopeKind, string ScopeId) ResolveScope(
        JsonElement root,
        string enterpriseSlug,
        CopilotMetricReportKind reportKind)
    {
        if (TryGetString(root, "user_login") is { } userLogin)
        {
            return ("User", userLogin);
        }

        if (TryGetString(root, "repo_id") is { } repositoryId)
        {
            return ("Repository", repositoryId);
        }

        if (TryGetString(root, "organization_id") is { } organizationId)
        {
            return ("Organization", organizationId);
        }

        return reportKind == CopilotMetricReportKind.RepositoriesDay
            ? ("Repository", TryGetString(root, "repository_name") ?? "unknown")
            : ("Enterprise", enterpriseSlug);
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
    }

    private static DateOnly? TryGetDate(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
        && DateOnly.TryParse(property.GetString(), out var value)
            ? value
            : null;
}
