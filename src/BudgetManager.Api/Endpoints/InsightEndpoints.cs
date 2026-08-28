using System.Security.Claims;
using System.Text.Json;
using BudgetManager.Api.Security;
using BudgetManager.Application.Classification;
using BudgetManager.Application.Exports;
using BudgetManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Api.Endpoints;

public static class InsightEndpoints
{
    public static RouteGroupBuilder MapInsightEndpoints(
        this RouteGroupBuilder group,
        bool authenticationEnabled)
    {
        group.MapGet("/classifications/{snapshotId:guid}", (
            Guid snapshotId,
            Guid enterpriseId,
            ClaimsPrincipal user,
            BudgetManagerDbContext dbContext,
            CancellationToken cancellationToken) => GetClassificationDetail(
                snapshotId,
                enterpriseId,
                CanViewUsers(authenticationEnabled, user),
                dbContext,
                cancellationToken));
        group.MapGet("/hierarchy", (
            Guid enterpriseId,
            int page,
            int pageSize,
            ClaimsPrincipal user,
            BudgetManagerDbContext dbContext,
            CancellationToken cancellationToken) => GetHierarchy(
                enterpriseId,
                page,
                pageSize,
                CanViewUsers(authenticationEnabled, user),
                dbContext,
                cancellationToken));
        var notifications = group.MapGet("/notifications", GetNotifications);
        var userStates = group.MapGet("/budget-user-states", GetBudgetUserStates);
        group.MapGet("/operations/billing-exports", GetBillingExports);
        group.MapGet("/trends/classifications", (
            Guid enterpriseId,
            int days,
            ClaimsPrincipal user,
            BudgetManagerDbContext dbContext,
            CancellationToken cancellationToken) => GetClassificationTrend(
                enterpriseId,
                days,
                CanViewUsers(authenticationEnabled, user),
                dbContext,
                cancellationToken));
        group.MapGet("/trends/budgets", GetBudgetTrend);
        group.MapGet("/exports/audit.csv", ExportAudit);
        group.MapGet("/exports/classifications.csv", (
            Guid enterpriseId,
            int maximumRows,
            ClaimsPrincipal user,
            HttpContext httpContext,
            BudgetManagerDbContext dbContext,
            CancellationToken cancellationToken) => ExportClassifications(
                enterpriseId,
                maximumRows,
                CanViewUsers(authenticationEnabled, user),
                user,
                httpContext,
                dbContext,
                cancellationToken));
        var identityExport = group.MapGet("/exports/identity-mappings.csv", ExportIdentityMappings);

        if (authenticationEnabled)
        {
            notifications.RequireAuthorization(ApiPolicies.EnterpriseAdmin);
            userStates.RequireAuthorization(ApiPolicies.EnterpriseAdmin);
            identityExport.RequireAuthorization(ApiPolicies.EnterpriseAdmin);
        }

        return group;
    }

    private static async Task<IResult> GetClassificationDetail(
        Guid snapshotId,
        Guid enterpriseId,
        bool canViewUsers,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (enterpriseId == Guid.Empty)
        {
            return InvalidEnterpriseId();
        }

        var snapshot = await dbContext.ClassificationSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == snapshotId
                && item.EnterpriseId == enterpriseId, cancellationToken);
        if (snapshot is null)
        {
            return Results.NotFound();
        }

        if (snapshot.ScopeKind == ScopeKind.User && !canViewUsers)
        {
            return Results.Forbid();
        }

        var policy = await dbContext.Policies
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == snapshot.PolicyId, cancellationToken);
        var evaluatedOn = DateOnly.FromDateTime(snapshot.EvaluatedAt.UtcDateTime);
        var metricHistory = await dbContext.DailyMetrics
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId
                && item.ScopeKind == snapshot.ScopeKind
                && item.ScopeExternalId == snapshot.ScopeExternalId
                && item.MetricDay <= evaluatedOn)
            .ToListAsync(cancellationToken);
        var latestMetrics = metricHistory
            .GroupBy(item => item.MetricKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.MaxBy(item => item.MetricDay)!)
            .OrderBy(item => item.MetricKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var catalog = MetricCatalog.All.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        var requiredMetricKeys = policy is null
            ? []
            : GetRequiredMetricKeys(policy.Mode, policy.DefinitionJson);
        var availableKeys = latestMetrics
            .Where(item => item.Availability.Equals("available", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.MetricKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Results.Ok(new
        {
            snapshot.Id,
            snapshot.ScopeKind,
            snapshot.ScopeExternalId,
            snapshot.Status,
            snapshot.Score,
            snapshot.EvaluatedAt,
            snapshot.InputsFingerprint,
            reasons = ParseJson(snapshot.ReasonsJson),
            coverage = new
            {
                required = requiredMetricKeys.Length,
                available = requiredMetricKeys.Count(availableKeys.Contains),
            },
            policy = policy is null ? null : new
            {
                policy.Id,
                policy.Name,
                policy.Version,
                policy.Mode,
                policy.Governance,
                definition = ParseJson(policy.DefinitionJson),
            },
            observations = latestMetrics.Select(item =>
            {
                catalog.TryGetValue(item.MetricKey, out var definition);
                return new
                {
                    item.MetricKey,
                    displayName = definition?.DisplayName ?? item.MetricKey,
                    category = definition?.Category ?? "Uncataloged",
                    unit = definition?.Unit,
                    direction = definition?.Direction.ToString(),
                    caveat = definition?.Caveat,
                    item.MetricValue,
                    item.Availability,
                    item.AvailabilityDetail,
                    item.Source,
                    item.MetricDay,
                    ageDays = evaluatedOn.DayNumber - item.MetricDay.DayNumber,
                    isRequired = requiredMetricKeys.Contains(item.MetricKey, StringComparer.OrdinalIgnoreCase),
                };
            }),
        });
    }

    private static async Task<IResult> GetHierarchy(
        Guid enterpriseId,
        int page,
        int pageSize,
        bool canViewUsers,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (enterpriseId == Guid.Empty)
        {
            return InvalidEnterpriseId();
        }

        (page, pageSize) = NormalizePagination(page, pageSize, 500);
        var query = dbContext.ManagedEntities
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId && item.IsActive);
        if (!canViewUsers)
        {
            query = query.Where(item => item.ScopeKind != ScopeKind.User);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var entities = await query
            .OrderBy(item => item.ScopeKind)
            .ThenBy(item => item.DisplayName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var classifications = await dbContext.ClassificationSnapshots
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId)
            .ToListAsync(cancellationToken);
        var latestClassifications = classifications
            .Where(item => canViewUsers || item.ScopeKind != ScopeKind.User)
            .GroupBy(item => $"{item.ScopeKind}:{item.ScopeExternalId}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.MaxBy(item => item.EvaluatedAt)!,
                StringComparer.OrdinalIgnoreCase);
        var membershipHistory = await dbContext.EntityMemberships
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId)
            .ToListAsync(cancellationToken);
        var memberships = membershipHistory
            .Where(item => canViewUsers || item.MemberScopeKind != ScopeKind.User)
            .GroupBy(item => $"{item.ParentScopeKind}:{item.ParentExternalId}:{item.MemberScopeKind}:{item.MemberExternalId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.MaxBy(item => item.ObservedOn)!)
            .Take(5_000)
            .ToArray();
        var enterprise = await dbContext.Enterprises
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == enterpriseId, cancellationToken);

        return Results.Ok(new
        {
            page,
            pageSize,
            totalCount,
            root = enterprise is null ? null : new
            {
                scopeKind = ScopeKind.Enterprise,
                externalId = enterprise.Slug,
                displayName = enterprise.DisplayName,
            },
            items = entities.Select(item =>
            {
                latestClassifications.TryGetValue($"{item.ScopeKind}:{item.ExternalId}", out var classification);
                return new
                {
                    item.Id,
                    item.ScopeKind,
                    item.ExternalId,
                    item.DisplayName,
                    item.ParentScopeKind,
                    item.ParentExternalId,
                    item.ObservedAt,
                    classification = classification is null ? null : new
                    {
                        classification.Id,
                        classification.Status,
                        classification.Score,
                        classification.EvaluatedAt,
                    },
                };
            }),
            memberships,
        });
    }

    private static async Task<IResult> GetNotifications(
        Guid enterpriseId,
        string? status,
        string? recipient,
        int page,
        int pageSize,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (enterpriseId == Guid.Empty)
        {
            return InvalidEnterpriseId();
        }

        (page, pageSize) = NormalizePagination(page, pageSize);
        var query = dbContext.NotificationDeliveries
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<DeliveryStatus>(status, true, out var parsedStatus))
            {
                return Results.Problem(statusCode: 400, title: "Invalid delivery status");
            }

            query = query.Where(item => item.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(recipient))
        {
            query = query.Where(item => item.RecipientKey.Contains(recipient));
        }

        var deliveries = await query.ToListAsync(cancellationToken);
        var outbox = await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId)
            .ToDictionaryAsync(item => item.EventFingerprint, StringComparer.Ordinal, cancellationToken);
        var items = deliveries
            .OrderByDescending(item => item.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item =>
            {
                outbox.TryGetValue(item.EventFingerprint, out var message);
                return new
                {
                    item.Id,
                    item.EventFingerprint,
                    eventType = message?.EventType,
                    outboxStatus = message?.Status,
                    item.Channel,
                    item.RecipientKey,
                    item.TemplateVersion,
                    item.Status,
                    item.AttemptCount,
                    item.ProviderMessageId,
                    item.LastErrorCode,
                    item.CreatedAt,
                    item.DeliveredAt,
                };
            })
            .ToArray();
        return Results.Ok(new { page, pageSize, totalCount = deliveries.Count, items });
    }

    private static async Task<IResult> GetBudgetUserStates(
        Guid enterpriseId,
        string? budgetId,
        string? userLogin,
        int page,
        int pageSize,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        (page, pageSize) = NormalizePagination(page, pageSize);
        var query = dbContext.BudgetUserStateSnapshots
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId);
        if (!string.IsNullOrWhiteSpace(budgetId))
        {
            query = query.Where(item => item.BudgetId == budgetId);
        }

        if (!string.IsNullOrWhiteSpace(userLogin))
        {
            query = query.Where(item => item.UserLogin.Contains(userLogin));
        }

        var history = await query.ToListAsync(cancellationToken);
        var latest = history
            .GroupBy(item => $"{item.BudgetId}:{item.UserLogin}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.MaxBy(item => item.ObservedAt)!)
            .OrderBy(item => item.BudgetId, StringComparer.Ordinal)
            .ThenBy(item => item.UserLogin, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Results.Ok(new
        {
            page,
            pageSize,
            totalCount = latest.Length,
            items = latest.Skip((page - 1) * pageSize).Take(pageSize),
        });
    }

    private static async Task<IResult> GetBillingExports(
        Guid enterpriseId,
        int page,
        int pageSize,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        (page, pageSize) = NormalizePagination(page, pageSize);
        var query = dbContext.BillingReportExports
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId);
        var items = await query
            .OrderByDescending(item => item.StartDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Results.Ok(new
        {
            page,
            pageSize,
            totalCount = await query.CountAsync(cancellationToken),
            items,
        });
    }

    private static async Task<IResult> GetClassificationTrend(
        Guid enterpriseId,
        int days,
        bool canViewUsers,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, 90);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        var snapshots = await dbContext.ClassificationSnapshots
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId)
            .ToListAsync(cancellationToken);
        var items = snapshots
            .Where(item => item.EvaluatedAt >= cutoff
                && (canViewUsers || item.ScopeKind != ScopeKind.User))
            .GroupBy(item => new
            {
                Day = DateOnly.FromDateTime(item.EvaluatedAt.UtcDateTime),
                Status = item.Status.ToLowerInvariant(),
            })
            .Select(group => new { group.Key.Day, group.Key.Status, Count = group.Count() })
            .OrderBy(item => item.Day)
            .ThenBy(item => item.Status)
            .ToArray();
        return Results.Ok(new { days, items });
    }

    private static async Task<IResult> GetBudgetTrend(
        Guid enterpriseId,
        int days,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, 90);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        var history = await dbContext.BudgetSnapshots
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId)
            .ToListAsync(cancellationToken);
        var items = history
            .Where(item => item.ObservedAt >= cutoff)
            .GroupBy(item => new
            {
                Day = DateOnly.FromDateTime(item.ObservedAt.UtcDateTime),
                item.BudgetId,
            })
            .Select(group => group.MaxBy(item => item.ObservedAt)!)
            .GroupBy(item => DateOnly.FromDateTime(item.ObservedAt.UtcDateTime))
            .Select(group => new
            {
                Day = group.Key,
                BudgetAmount = group.Sum(item => item.BudgetAmount),
                ConsumedAmount = group.Sum(item => item.ConsumedAmount),
            })
            .OrderBy(item => item.Day)
            .ToArray();
        return Results.Ok(new { days, items });
    }

    private static async Task<IResult> ExportAudit(
        Guid enterpriseId,
        int maximumRows,
        ClaimsPrincipal user,
        HttpContext httpContext,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        maximumRows = NormalizeMaximumRows(maximumRows);
        var query = dbContext.AuditEvents.AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId);
        var history = await query.ToListAsync(cancellationToken);
        var items = history.OrderBy(item => item.OccurredAt).Take(maximumRows).ToArray();
        var document = EvidenceCsvWriter.Write(
            ["schema_version", "id", "enterprise_id", "occurred_at", "event_type", "actor_type", "actor_id", "target_type", "target_id", "correlation_id", "data_json"],
            items.Select(item => (IReadOnlyList<object?>)
            [1, item.Id, item.EnterpriseId, item.OccurredAt, item.EventType, item.ActorType, item.ActorId, item.TargetType, item.TargetId, item.CorrelationId, item.DataJson]));
        await RecordExportAsync(enterpriseId, "audit", document, user, dbContext, cancellationToken);
        return CsvResult(document, "budget-manager-audit.csv", httpContext);
    }

    private static async Task<IResult> ExportClassifications(
        Guid enterpriseId,
        int maximumRows,
        bool canViewUsers,
        ClaimsPrincipal user,
        HttpContext httpContext,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        maximumRows = NormalizeMaximumRows(maximumRows);
        var query = dbContext.ClassificationSnapshots.AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId);
        if (!canViewUsers)
        {
            query = query.Where(item => item.ScopeKind != ScopeKind.User);
        }

        var history = await query.ToListAsync(cancellationToken);
        var items = history.OrderBy(item => item.EvaluatedAt).Take(maximumRows).ToArray();
        var document = EvidenceCsvWriter.Write(
            ["schema_version", "id", "enterprise_id", "scope_kind", "scope_external_id", "policy_id", "policy_version", "status", "score", "reasons_json", "inputs_fingerprint", "evaluated_at"],
            items.Select(item => (IReadOnlyList<object?>)
            [1, item.Id, item.EnterpriseId, item.ScopeKind, item.ScopeExternalId, item.PolicyId, item.PolicyVersion, item.Status, item.Score, item.ReasonsJson, item.InputsFingerprint, item.EvaluatedAt]));
        await RecordExportAsync(enterpriseId, "classifications", document, user, dbContext, cancellationToken);
        return CsvResult(document, "budget-manager-classifications.csv", httpContext);
    }

    private static async Task<IResult> ExportIdentityMappings(
        Guid enterpriseId,
        int maximumRows,
        ClaimsPrincipal user,
        HttpContext httpContext,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        maximumRows = NormalizeMaximumRows(maximumRows);
        var items = await dbContext.IdentityMappings.AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId)
            .OrderBy(item => item.GitHubLogin)
            .Take(maximumRows)
            .ToListAsync(cancellationToken);
        var document = EvidenceCsvWriter.Write(
            ["schema_version", "id", "enterprise_id", "github_login", "entra_object_id", "user_principal_name", "department", "entra_cost_center_code", "github_cost_center_id", "status", "source", "updated_at"],
            items.Select(item => (IReadOnlyList<object?>)
            [1, item.Id, item.EnterpriseId, item.GitHubLogin, item.EntraObjectId, item.UserPrincipalName, item.Department, item.EntraCostCenterCode, item.GitHubCostCenterId, item.Status, item.Source, item.UpdatedAt]));
        await RecordExportAsync(enterpriseId, "identity-mappings", document, user, dbContext, cancellationToken);
        return CsvResult(document, "budget-manager-identity-mappings.csv", httpContext);
    }

    private static async Task RecordExportAsync(
        Guid enterpriseId,
        string exportKind,
        EvidenceCsvDocument document,
        ClaimsPrincipal user,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            EventType = "evidence.export.generated",
            ActorType = "user",
            ActorId = ResolveActor(user),
            TargetType = "export",
            TargetId = exportKind,
            DataJson = JsonSerializer.Serialize(new
            {
                exportKind,
                document.RowCount,
                document.ContentSha256,
            }),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static IResult CsvResult(
        EvidenceCsvDocument document,
        string downloadName,
        HttpContext httpContext)
    {
        httpContext.Response.Headers["X-Content-SHA256"] = document.ContentSha256;
        httpContext.Response.Headers["X-Row-Count"] = document.RowCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.File(document.Content, "text/csv; charset=utf-8", downloadName);
    }

    private static string[] GetRequiredMetricKeys(
        ClassificationPolicyMode mode,
        string definitionJson)
    {
        using var document = JsonDocument.Parse(definitionJson);
        var root = document.RootElement;
        if (mode == ClassificationPolicyMode.Weighted
            && root.TryGetProperty("metricRules", out var metricRules))
        {
            return metricRules.EnumerateArray()
                .Where(item => !item.TryGetProperty("isRequired", out var required) || required.GetBoolean())
                .Select(item => item.GetProperty("metricKey").GetString()!)
                .ToArray();
        }

        if (mode == ClassificationPolicyMode.Precedence
            && root.TryGetProperty("requiredMetrics", out var requiredMetrics))
        {
            return requiredMetrics.EnumerateArray()
                .Select(item => item.GetProperty("metricKey").GetString()!)
                .ToArray();
        }

        return [];
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static bool CanViewUsers(bool authenticationEnabled, ClaimsPrincipal user) =>
        !authenticationEnabled || user.IsInRole(ApiRoles.EnterpriseAdmin);

    private static (int Page, int PageSize) NormalizePagination(
        int page,
        int pageSize,
        int maximumPageSize = 100) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, maximumPageSize));

    private static int NormalizeMaximumRows(int maximumRows) =>
        Math.Clamp(maximumRows == 0 ? 50_000 : maximumRows, 1, 100_000);

    private static IResult InvalidEnterpriseId() => Results.Problem(
        statusCode: 400,
        title: "A non-empty enterpriseId is required");

    private static string ResolveActor(ClaimsPrincipal user) =>
        user.FindFirst("oid")?.Value
        ?? user.FindFirst("sub")?.Value
        ?? "development-user";
}
