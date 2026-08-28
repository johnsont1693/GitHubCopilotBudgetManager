using System.Text.Json;

namespace BudgetManager.Api.Contracts;

public sealed record CreatePolicyRequest(
    Guid EnterpriseId,
    string Name,
    string Mode,
    string Governance,
    string ScopeKind,
    string ScopeExternalId,
    JsonElement Definition);

public sealed record RetentionSettingsRequest(
    Guid EnterpriseId,
    int RawReportDays,
    int UserMetricDays,
    int AggregateMetricDays,
    int NotificationDays,
    int AuditDays,
    bool LegalHold);

public sealed record BudgetBaselineRequest(
    Guid EnterpriseId,
    string BudgetId,
    long BaselineAmount,
    string Mode);
