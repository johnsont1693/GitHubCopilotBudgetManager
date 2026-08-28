using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BudgetManager.Api.Contracts;
using BudgetManager.Api.Security;
using BudgetManager.Application.Budgets;
using BudgetManager.Application.Operations;
using BudgetManager.Domain.Budgets;
using BudgetManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Api.Endpoints;

public static class AdministrationEndpoints
{
    public static RouteGroupBuilder MapAdministrationEndpoints(
        this RouteGroupBuilder group,
        bool authenticationEnabled)
    {
        group.MapGet("/dashboard/overview", GetOverview);
        group.MapGet("/budgets", GetBudgets);
        group.MapGet("/classifications", (
            Guid enterpriseId,
            string? scopeKind,
            int page,
            int pageSize,
            System.Security.Claims.ClaimsPrincipal user,
            BudgetManagerDbContext dbContext,
            CancellationToken cancellationToken) => GetClassifications(
                enterpriseId,
                scopeKind,
                page,
                pageSize,
                authenticationEnabled,
                user,
                dbContext,
                cancellationToken));
        group.MapGet("/forecasts", GetForecasts);
        group.MapGet("/policies", GetPolicies);
        var createPolicy = group.MapPost("/policies", CreatePolicy);
        group.MapGet("/budget-change-requests", GetBudgetChangeRequests);
        var createBudgetChange = group.MapPost("/budget-change-requests", CreateBudgetChange);
        var approveBudgetChange = group.MapPost(
            "/budget-change-requests/{requestId:guid}/approve",
            ApproveBudgetChange);
        var rejectBudgetChange = group.MapPost(
            "/budget-change-requests/{requestId:guid}/reject",
            RejectBudgetChange);
        var identityMappings = group.MapGet("/identity-mappings", GetIdentityMappings);
        group.MapGet("/operations/ingestions", GetIngestions);
        var previewRetention = group.MapPost("/operations/retention-preview", PreviewRetention);
        var safetyHistory = group.MapGet("/operations/safety-history", GetSafetyHistory);
        group.MapGet("/audit", GetAuditEvents);
        group.MapGet("/settings/retention", GetRetentionSettings);
        var saveRetention = group.MapPut("/settings/retention", SaveRetentionSettings);
        group.MapGet("/budget-baselines", GetBudgetBaselines);
        var saveBaseline = group.MapPut("/budget-baselines/{budgetId}", SaveBudgetBaseline);
        var previewReconciliation = group.MapPost(
            "/budget-baselines/reconciliation-preview",
            PreviewBaselineReconciliation);
        group.MapInsightEndpoints(authenticationEnabled);

        createPolicy.RequireRateLimiting(ApiRateLimits.AdministrativeWrites);
        createBudgetChange.RequireRateLimiting(ApiRateLimits.AdministrativeWrites);
        approveBudgetChange.RequireRateLimiting(ApiRateLimits.AdministrativeWrites);
        rejectBudgetChange.RequireRateLimiting(ApiRateLimits.AdministrativeWrites);
        previewRetention.RequireRateLimiting(ApiRateLimits.AdministrativeWrites);
        saveRetention.RequireRateLimiting(ApiRateLimits.AdministrativeWrites);
        saveBaseline.RequireRateLimiting(ApiRateLimits.AdministrativeWrites);
        previewReconciliation.RequireRateLimiting(ApiRateLimits.AdministrativeWrites);

        if (authenticationEnabled)
        {
            createPolicy.RequireAuthorization(ApiPolicies.EnterpriseAdmin);
            createBudgetChange.RequireAuthorization(ApiPolicies.EnterpriseAdmin);
            approveBudgetChange.RequireAuthorization(ApiPolicies.EnterpriseAdmin);
            rejectBudgetChange.RequireAuthorization(ApiPolicies.EnterpriseAdmin);
            identityMappings.RequireAuthorization(ApiPolicies.EnterpriseAdmin);
            previewRetention.RequireAuthorization(ApiPolicies.EnterpriseAdmin);
            safetyHistory.RequireAuthorization(ApiPolicies.EnterpriseAdmin);
            saveRetention.RequireAuthorization(ApiPolicies.EnterpriseAdmin);
            saveBaseline.RequireAuthorization(ApiPolicies.EnterpriseAdmin);
            previewReconciliation.RequireAuthorization(ApiPolicies.EnterpriseAdmin);
        }

        return group;
    }

    private static async Task<IResult> GetOverview(
        Guid enterpriseId,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (enterpriseId == Guid.Empty)
        {
            return InvalidEnterpriseId();
        }

        var latestBudgets = await LoadLatestBudgetsAsync(
            enterpriseId,
            dbContext,
            cancellationToken);
        var latestBudgetObservation = latestBudgets.Count == 0
            ? (DateTimeOffset?)null
            : latestBudgets.Max(item => item.ObservedAt);
        var classificationQuery = dbContext.ClassificationSnapshots
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId);
        var latestClassifications = dbContext.Database.IsSqlite()
            ? (await classificationQuery.ToListAsync(cancellationToken))
                .OrderByDescending(item => item.EvaluatedAt)
                .Take(5_000)
                .ToList()
            : await classificationQuery
                .OrderByDescending(item => item.EvaluatedAt)
                .Take(5_000)
                .ToListAsync(cancellationToken);
        var classificationCounts = latestClassifications
            .GroupBy(item => new { item.ScopeKind, item.ScopeExternalId })
            .Select(group => group.First())
            .GroupBy(item => item.Status, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var ingestionQuery = dbContext.IngestionManifests
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId);
        var latestIngestion = dbContext.Database.IsSqlite()
            ? (await ingestionQuery.ToListAsync(cancellationToken)).MaxBy(item => item.StartedAt)
            : await ingestionQuery
                .OrderByDescending(item => item.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);
        var pendingApprovals = await dbContext.BudgetChangeRequests
            .AsNoTracking()
            .CountAsync(item => item.EnterpriseId == enterpriseId
                && item.Status == BudgetChangeStatus.PendingApproval, cancellationToken);
        var forecastQuery = dbContext.ForecastSnapshots
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId);
        var latestForecasts = dbContext.Database.IsSqlite()
            ? (await forecastQuery.ToListAsync(cancellationToken))
                .GroupBy(item => item.BudgetId, StringComparer.Ordinal)
                .Select(group => group.MaxBy(item => item.EvaluatedAt)!)
                .ToList()
            : await forecastQuery
                .GroupBy(item => item.BudgetId)
                .Select(group => group.OrderByDescending(item => item.EvaluatedAt).First())
                .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            enterpriseId,
            totalBudget = latestBudgets.Sum(item => item.BudgetAmount),
            consumedAmount = latestBudgets.Sum(item => item.ConsumedAmount),
            effectiveBudgetId = latestBudgets.SingleOrDefault(item => item.IsEffective)?.BudgetId,
            classificationCounts,
            pendingApprovals,
            projectedSpend = latestForecasts.Where(item => item.IsUsable).Sum(item => item.ProjectedSpend),
            maximumForecastUtilization = latestForecasts.Count == 0
                ? null
                : latestForecasts.Max(item => item.UtilizationPercent),
            latestBudgetObservation,
            latestIngestion = latestIngestion is null ? null : new
            {
                latestIngestion.ReportType,
                latestIngestion.Status,
                latestIngestion.ReportEndDay,
                latestIngestion.CompletedAt,
            },
        });
    }

    private static async Task<IResult> GetBudgets(
        Guid enterpriseId,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (enterpriseId == Guid.Empty)
        {
            return InvalidEnterpriseId();
        }

        var budgets = await LoadLatestBudgetsAsync(enterpriseId, dbContext, cancellationToken);
        return Results.Ok(budgets);
    }

    private static async Task<IResult> GetClassifications(
        Guid enterpriseId,
        string? scopeKind,
        int page,
        int pageSize,
        bool authenticationEnabled,
        System.Security.Claims.ClaimsPrincipal user,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (enterpriseId == Guid.Empty)
        {
            return InvalidEnterpriseId();
        }

        (page, pageSize) = NormalizePagination(page, pageSize);
        var query = dbContext.ClassificationSnapshots
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId);
        if (authenticationEnabled && !user.IsInRole(ApiRoles.EnterpriseAdmin))
        {
            query = query.Where(item => item.ScopeKind != ScopeKind.User);
        }
        if (!string.IsNullOrWhiteSpace(scopeKind))
        {
            if (!Enum.TryParse<ScopeKind>(scopeKind, true, out var parsedScope))
            {
                return Results.Problem(statusCode: 400, title: "Invalid scope kind");
            }

            query = query.Where(item => item.ScopeKind == parsedScope);
        }

        var history = dbContext.Database.IsSqlite()
            ? (await query.ToListAsync(cancellationToken))
                .OrderByDescending(item => item.EvaluatedAt)
                .ToList()
            : await query
                .OrderByDescending(item => item.EvaluatedAt)
                .ToListAsync(cancellationToken);
        var latest = history
            .GroupBy(item => new { item.ScopeKind, item.ScopeExternalId })
            .Select(group => group.MaxBy(item => item.EvaluatedAt)!)
            .OrderByDescending(item => item.EvaluatedAt)
            .ToArray();
        var items = latest
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArray();
        return Results.Ok(new { page, pageSize, totalCount = latest.Length, items });
    }

    private static async Task<IResult> GetPolicies(
        Guid enterpriseId,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (enterpriseId == Guid.Empty)
        {
            return InvalidEnterpriseId();
        }

        return Results.Ok(await dbContext.Policies
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId)
            .OrderBy(item => item.Name)
            .ThenByDescending(item => item.Version)
            .ToListAsync(cancellationToken));
    }

    private static async Task<IResult> GetForecasts(
        Guid enterpriseId,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ForecastSnapshots
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId);
        var items = dbContext.Database.IsSqlite()
            ? (await query.ToListAsync(cancellationToken))
                .OrderByDescending(item => item.EvaluatedAt)
                .ToList()
            : await query.OrderByDescending(item => item.EvaluatedAt).ToListAsync(cancellationToken);
        return Results.Ok(items);
    }

    private static async Task<IResult> CreatePolicy(
        CreatePolicyRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (request.EnterpriseId == Guid.Empty || string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.Problem(statusCode: 400, title: "Enterprise ID and policy name are required");
        }

        if (!Enum.TryParse<ClassificationPolicyMode>(request.Mode, true, out var mode)
            || !Enum.TryParse<PolicyGovernance>(request.Governance, true, out var governance)
            || !Enum.TryParse<ScopeKind>(request.ScopeKind, true, out var scopeKind))
        {
            return Results.Problem(statusCode: 400, title: "Invalid policy enum value");
        }

        var version = (await dbContext.Policies
            .Where(item => item.EnterpriseId == request.EnterpriseId && item.Name == request.Name)
            .MaxAsync(item => (int?)item.Version, cancellationToken) ?? 0) + 1;
        var record = new PolicyRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = request.EnterpriseId,
            Name = request.Name,
            Version = version,
            Mode = mode,
            Governance = governance,
            ScopeKind = scopeKind,
            ScopeExternalId = request.ScopeExternalId,
            DefinitionJson = request.Definition.GetRawText(),
            IsActive = true,
            CreatedBy = ResolveActor(user),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.Policies.Add(record);
        dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = request.EnterpriseId,
            EventType = "policy.created",
            ActorType = "user",
            ActorId = ResolveActor(user),
            TargetType = "policy",
            TargetId = record.Id.ToString("N"),
            DataJson = JsonSerializer.Serialize(new { record.Name, record.Version }),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAt = record.CreatedAt,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/policies/{record.Id}", record);
    }

    private static async Task<IResult> GetIdentityMappings(
        Guid enterpriseId,
        int page,
        int pageSize,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        (page, pageSize) = NormalizePagination(page, pageSize);
        var items = await dbContext.IdentityMappings
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId)
            .OrderBy(item => item.GitHubLogin)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Results.Ok(new { page, pageSize, items });
    }

    private static async Task<IResult> GetBudgetChangeRequests(
        Guid enterpriseId,
        int page,
        int pageSize,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        (page, pageSize) = NormalizePagination(page, pageSize);
        var query = dbContext.BudgetChangeRequests
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId);
        var items = dbContext.Database.IsSqlite()
            ? (await query.ToListAsync(cancellationToken))
                .OrderByDescending(item => item.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList()
            : await query
                .OrderByDescending(item => item.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        return Results.Ok(new { page, pageSize, items });
    }

    private static async Task<IResult> CreateBudgetChange(
        CreateBudgetChangeRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        BudgetIncreaseGuardrailPolicy policy,
        BudgetChangeApiOptions options,
        BudgetChangeProposalService service,
        BudgetManagerDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request.AutomaticMode)
        {
            return Results.UnprocessableEntity(new
            {
                rejectionReason = "The built-in API requires a separate administrator approval. Automatic mode is available only to separately governed custom hosts.",
            });
        }

        try
        {
            if (request.EnterpriseId == Guid.Empty
                || string.IsNullOrWhiteSpace(request.Proposal.BudgetId)
                || request.Proposal.ProposedAmount <= 0)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Enterprise ID, budget ID, and a positive proposed amount are required");
            }

            var budgetQuery = dbContext.BudgetSnapshots
                .AsNoTracking()
                .Where(item => item.EnterpriseId == request.EnterpriseId
                    && item.BudgetId == request.Proposal.BudgetId);
            var budget = dbContext.Database.IsSqlite()
                ? (await budgetQuery.ToListAsync(cancellationToken)).MaxBy(item => item.ObservedAt)
                : await budgetQuery.OrderByDescending(item => item.ObservedAt).FirstOrDefaultAsync(cancellationToken);
            if (budget is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Synchronize the authoritative GitHub budget before creating a proposal");
            }

            var forecastQuery = dbContext.ForecastSnapshots
                .AsNoTracking()
                .Where(item => item.EnterpriseId == request.EnterpriseId
                    && item.BudgetId == request.Proposal.BudgetId);
            var forecast = dbContext.Database.IsSqlite()
                ? (await forecastQuery.ToListAsync(cancellationToken)).MaxBy(item => item.EvaluatedAt)
                : await forecastQuery.OrderByDescending(item => item.EvaluatedAt).FirstOrDefaultAsync(cancellationToken);
            if (forecast is null || !forecast.IsUsable || forecast.ProjectedSpend is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "A usable persisted forecast is required before creating a proposal");
            }

            if (forecast.BudgetAmount != budget.BudgetAmount)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The latest forecast does not match the authoritative budget; refresh forecasts first");
            }

            var now = timeProvider.GetUtcNow();
            var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var appliedQuery = dbContext.BudgetChangeRequests
                .AsNoTracking()
                .Where(item => item.EnterpriseId == request.EnterpriseId
                    && item.BudgetId == request.Proposal.BudgetId
                    && item.Status == BudgetChangeStatus.Applied);
            var appliedRequests = dbContext.Database.IsSqlite()
                ? await appliedQuery.ToListAsync(cancellationToken)
                : await appliedQuery
                    .Where(item => item.ExecutedAt >= monthStart)
                    .ToListAsync(cancellationToken);
            var monthlyRequests = dbContext.Database.IsSqlite()
                ? appliedRequests.Where(item => item.ExecutedAt >= monthStart)
                : appliedRequests;
            var cumulativeMonthlyIncrease = monthlyRequests.Sum(item =>
                Math.Max(0, item.ProposedAmount - item.ExpectedCurrentAmount));
            var lastApplied = dbContext.Database.IsSqlite()
                ? appliedRequests.MaxBy(item => item.ExecutedAt)
                : await appliedQuery
                    .OrderByDescending(item => item.ExecutedAt)
                    .FirstOrDefaultAsync(cancellationToken);
            var dataAsOf = budget.ObservedAt <= forecast.EvaluatedAt
                ? budget.ObservedAt
                : forecast.EvaluatedAt;
            var dataFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{budget.Id:N}|{forecast.Id:N}|{request.Proposal.ProposedAmount}|{cumulativeMonthlyIncrease}|{lastApplied?.DataFingerprint}")));
            var evidenceJson = JsonSerializer.Serialize(new
            {
                source = "persisted-budget-and-forecast",
                budgetSnapshotId = budget.Id,
                budget.ObservedAt,
                budget.BudgetAmount,
                budget.ConsumedAmount,
                forecastSnapshotId = forecast.Id,
                forecast.EvaluatedAt,
                forecast.AsOfDay,
                forecast.ProjectedSpend,
                forecast.InputsFingerprint,
                cumulativeMonthlyIncrease,
                lastAppliedRequestId = lastApplied?.Id,
            });
            var proposal = new BudgetIncreaseRequest(
                request.Proposal.BudgetId,
                budget.BudgetAmount,
                request.Proposal.ProposedAmount,
                forecast.ProjectedSpend.Value,
                cumulativeMonthlyIncrease,
                dataAsOf,
                dataFingerprint,
                lastApplied?.ExecutedAt,
                lastApplied?.DataFingerprint);
            var result = await service.CreateAsync(
                request.EnterpriseId,
                policy,
                proposal,
                BudgetManager.Domain.Classification.HealthStatus.Unknown,
                false,
                evidenceJson,
                options.ApprovalLifetime,
                ResolveActor(user),
                cancellationToken);
            return result.Created
                ? Results.Created($"/api/v1/budget-change-requests/{result.RequestId}", result)
                : Results.UnprocessableEntity(result);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(
                statusCode: 400,
                title: "Invalid budget change request",
                detail: exception.Message);
        }
    }

    private static async Task<IResult> ApproveBudgetChange(
        Guid requestId,
        BudgetDecisionRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        IBudgetChangeRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var approved = await repository.ApproveAsync(
            requestId,
            request.ExpectedConcurrencyToken,
            ResolveActor(user),
            timeProvider.GetUtcNow(),
            cancellationToken);
        return approved ? Results.NoContent() : Results.Conflict();
    }

    private static async Task<IResult> RejectBudgetChange(
        Guid requestId,
        BudgetDecisionRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        IBudgetChangeRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var rejected = await repository.RejectAsync(
            requestId,
            request.ExpectedConcurrencyToken,
            ResolveActor(user),
            request.Reason,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return rejected ? Results.NoContent() : Results.Conflict();
    }

    private static async Task<IResult> GetIngestions(
        Guid enterpriseId,
        int page,
        int pageSize,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        (page, pageSize) = NormalizePagination(page, pageSize);
        var query = dbContext.IngestionManifests
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId);
        var items = dbContext.Database.IsSqlite()
            ? (await query.ToListAsync(cancellationToken))
                .OrderByDescending(item => item.StartedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList()
            : await query
                .OrderByDescending(item => item.StartedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        return Results.Ok(new { page, pageSize, items });
    }

    private static async Task<IResult> PreviewRetention(
        Guid enterpriseId,
        RetentionService service,
        CancellationToken cancellationToken)
    {
        if (enterpriseId == Guid.Empty)
        {
            return InvalidEnterpriseId();
        }

        try
        {
            return Results.Ok(await service.ExecuteAsync(enterpriseId, true, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Retention preview is unavailable",
                detail: exception.Message);
        }
    }

    private static async Task<IResult> GetSafetyHistory(
        Guid enterpriseId,
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
        string[] eventTypes =
        [
            "retention.previewed",
            "retention.applied",
            "budget.baseline.reconciliation.previewed",
            "budget.baseline.reconciliation.evaluated",
        ];
        var query = dbContext.AuditEvents
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId
                && eventTypes.Contains(item.EventType));
        var records = dbContext.Database.IsSqlite()
            ? (await query.ToListAsync(cancellationToken))
                .OrderByDescending(item => item.OccurredAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList()
            : await query
                .OrderByDescending(item => item.OccurredAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        var items = records.Select(item => new
        {
            item.Id,
            item.EventType,
            item.ActorId,
            item.CorrelationId,
            item.OccurredAt,
            evidence = ParseEvidence(item.DataJson),
        });
        return Results.Ok(new { page, pageSize, items });
    }

    private static JsonElement ParseEvidence(string dataJson)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(dataJson);
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new { unavailable = true });
        }
    }

    private static async Task<IResult> GetAuditEvents(
        Guid enterpriseId,
        int page,
        int pageSize,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        (page, pageSize) = NormalizePagination(page, pageSize);
        var query = dbContext.AuditEvents
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId);
        var items = dbContext.Database.IsSqlite()
            ? (await query.ToListAsync(cancellationToken))
                .OrderByDescending(item => item.OccurredAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList()
            : await query
                .OrderByDescending(item => item.OccurredAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        return Results.Ok(new { page, pageSize, items });
    }

    private static async Task<IResult> GetRetentionSettings(
        Guid enterpriseId,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken) => Results.Ok(await dbContext.RetentionPolicies
        .AsNoTracking()
        .SingleOrDefaultAsync(item => item.EnterpriseId == enterpriseId, cancellationToken));

    private static async Task<IResult> SaveRetentionSettings(
        RetentionSettingsRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (request.EnterpriseId == Guid.Empty
            || request.RawReportDays <= 0
            || request.UserMetricDays <= 0
            || request.AggregateMetricDays <= 0
            || request.NotificationDays <= 0
            || request.AuditDays <= 0)
        {
            return Results.Problem(statusCode: 400, title: "All retention durations must be positive");
        }

        var record = await dbContext.RetentionPolicies.FindAsync([request.EnterpriseId], cancellationToken);
        if (record is null)
        {
            record = new RetentionPolicyRecord { EnterpriseId = request.EnterpriseId };
            dbContext.RetentionPolicies.Add(record);
        }

        record.RawReportDays = request.RawReportDays;
        record.UserMetricDays = request.UserMetricDays;
        record.AggregateMetricDays = request.AggregateMetricDays;
        record.NotificationDays = request.NotificationDays;
        record.AuditDays = request.AuditDays;
        record.LegalHold = request.LegalHold;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = request.EnterpriseId,
            EventType = "retention.settings.updated",
            ActorType = "user",
            ActorId = ResolveActor(user),
            TargetType = "retention-policy",
            TargetId = request.EnterpriseId.ToString("N"),
            DataJson = JsonSerializer.Serialize(new
            {
                request.RawReportDays,
                request.UserMetricDays,
                request.AggregateMetricDays,
                request.NotificationDays,
                request.AuditDays,
                request.LegalHold,
            }),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAt = record.UpdatedAt,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(record);
    }

    private static async Task<IResult> GetBudgetBaselines(
        Guid enterpriseId,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken) => Results.Ok(await dbContext.BudgetBaselines
        .AsNoTracking()
        .Where(item => item.EnterpriseId == enterpriseId)
        .OrderBy(item => item.BudgetId)
        .ToListAsync(cancellationToken));

    private static async Task<IResult> SaveBudgetBaseline(
        string budgetId,
        BudgetBaselineRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(budgetId, request.BudgetId, StringComparison.Ordinal)
            || request.EnterpriseId == Guid.Empty
            || request.BaselineAmount < 0
            || !Enum.TryParse<BaselineReconciliationMode>(request.Mode, true, out var mode))
        {
            return Results.Problem(statusCode: 400, title: "Invalid baseline configuration");
        }

        var record = await dbContext.BudgetBaselines.FindAsync(
            [request.EnterpriseId, request.BudgetId],
            cancellationToken);
        if (record is null)
        {
            record = new BudgetBaselineRecord
            {
                EnterpriseId = request.EnterpriseId,
                BudgetId = request.BudgetId,
                UpdatedBy = ResolveActor(user),
            };
            dbContext.BudgetBaselines.Add(record);
        }

        record.BaselineAmount = request.BaselineAmount;
        record.Mode = mode;
        record.UpdatedBy = ResolveActor(user);
        record.UpdatedAt = DateTimeOffset.UtcNow;
        dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = request.EnterpriseId,
            EventType = "budget.baseline.updated",
            ActorType = "user",
            ActorId = ResolveActor(user),
            TargetType = "budget-baseline",
            TargetId = request.BudgetId,
            DataJson = JsonSerializer.Serialize(new
            {
                request.BaselineAmount,
                mode,
            }),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAt = record.UpdatedAt,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(record);
    }

    private static async Task<IResult> PreviewBaselineReconciliation(
        Guid enterpriseId,
        BaselineReconciliationService service,
        CancellationToken cancellationToken) => Results.Ok(
        await service.ReconcileAsync(enterpriseId, true, cancellationToken));

    private static (int Page, int PageSize) NormalizePagination(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));

    private static async Task<List<BudgetSnapshotRecord>> LoadLatestBudgetsAsync(
        Guid enterpriseId,
        BudgetManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var query = dbContext.BudgetSnapshots
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId);
        if (dbContext.Database.IsSqlite())
        {
            var history = await query.ToListAsync(cancellationToken);
            return history
                .GroupBy(item => item.BudgetId, StringComparer.Ordinal)
                .Select(group => group.MaxBy(item => item.ObservedAt)!)
                .OrderBy(item => item.BudgetScope, StringComparer.Ordinal)
                .ThenBy(item => item.BudgetEntityName, StringComparer.Ordinal)
                .ToList();
        }

        return await query
            .GroupBy(item => item.BudgetId)
            .Select(group => group.OrderByDescending(item => item.ObservedAt).First())
            .OrderBy(item => item.BudgetScope)
            .ThenBy(item => item.BudgetEntityName)
            .ToListAsync(cancellationToken);
    }

    private static IResult InvalidEnterpriseId() => Results.Problem(
        statusCode: 400,
        title: "A non-empty enterpriseId is required");

    private static string ResolveActor(System.Security.Claims.ClaimsPrincipal user) =>
        user.FindFirst("oid")?.Value
        ?? user.FindFirst("sub")?.Value
        ?? "development-user";
}
