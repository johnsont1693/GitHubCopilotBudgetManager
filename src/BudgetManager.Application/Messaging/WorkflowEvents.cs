using System.Text.Json;

namespace BudgetManager.Application.Messaging;

public sealed record WorkflowEventRecipients(
    IReadOnlyList<string> UserPrincipalNames,
    IReadOnlyList<string> OwnerPrincipalNames,
    IReadOnlyList<string> AdminPrincipalNames)
{
    public static WorkflowEventRecipients Empty { get; } = new([], [], []);
}

public sealed record WorkflowEventOptions(
    string? DashboardUrl,
    IReadOnlyList<string> AdminPrincipalNames,
    bool PublishBudgetLifecycleEvents = true)
{
    public static WorkflowEventOptions Empty { get; } = new(null, []);
}

public static class WorkflowEventSerializer
{
    public static string Serialize(
        Guid enterpriseId,
        string eventType,
        string eventFingerprint,
        string subject,
        object payload,
        DateTimeOffset occurredAt,
        string? dashboardUrl = null,
        WorkflowEventRecipients? recipients = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(enterpriseId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(payload);
        recipients ??= WorkflowEventRecipients.Empty;
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            eventType,
            eventFingerprint,
            enterpriseId,
            occurredAt,
            subject,
            dashboardUrl,
            recipients = new
            {
                userPrincipalNames = Normalize(recipients.UserPrincipalNames),
                ownerPrincipalNames = Normalize(recipients.OwnerPrincipalNames),
                adminPrincipalNames = Normalize(recipients.AdminPrincipalNames),
            },
            payload,
        });
    }

    private static string[] Normalize(IReadOnlyList<string> principalNames) => principalNames
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Select(item => item.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
