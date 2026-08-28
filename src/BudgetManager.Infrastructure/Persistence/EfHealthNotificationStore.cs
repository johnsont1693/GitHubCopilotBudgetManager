using System.Text.Json;
using BudgetManager.Application.Messaging;
using BudgetManager.Application.Notifications;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Persistence;

public sealed class EfHealthNotificationStore : IHealthNotificationStore
{
    private readonly BudgetManagerDbContext dbContext;
    private readonly string[] deliveryChannels;

    public EfHealthNotificationStore(
        BudgetManagerDbContext dbContext,
        NotificationDeliveryOptions? deliveryOptions = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        this.dbContext = dbContext;
        deliveryChannels = (deliveryOptions ?? NotificationDeliveryOptions.Default).Channels
            .Select(item => item.Trim())
            .Select(item => item.Equals("Teams", StringComparison.OrdinalIgnoreCase)
                ? "Teams"
                : item.Equals("Outlook", StringComparison.OrdinalIgnoreCase)
                    ? "Outlook"
                    : item)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (deliveryChannels.Length == 0
            || deliveryChannels.Any(item => item is not ("Teams" or "Outlook"))
            || !deliveryChannels.Contains("Outlook", StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Notification channels must be Outlook, or Teams and Outlook.",
                nameof(deliveryOptions));
        }
    }

    public async Task<IReadOnlyList<HealthNotificationSignal>> GetSignalsAsync(
        Guid enterpriseId,
        CancellationToken cancellationToken = default)
    {
        var snapshots = await dbContext.ClassificationSnapshots
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId)
            .ToListAsync(cancellationToken);
        return snapshots
            .GroupBy(item => new { item.ScopeKind, item.ScopeExternalId })
            .Select(group => group.MaxBy(item => item.EvaluatedAt)!)
            .Where(item => item.Status is "yellow" or "red" or "unknown")
            .Select(item => new HealthNotificationSignal(
                item.Id,
                item.ScopeKind.ToString(),
                item.ScopeExternalId,
                item.Status,
                item.Score,
                item.EvaluatedAt))
            .ToArray();
    }

    public async Task<NotificationRecipients> ResolveRecipientsAsync(
        Guid enterpriseId,
        HealthNotificationSignal signal,
        CancellationToken cancellationToken = default)
    {
        var users = new List<string>();
        if (signal.ScopeKind.Equals("User", StringComparison.OrdinalIgnoreCase))
        {
            var userPrincipalName = await dbContext.IdentityMappings
                .AsNoTracking()
                .Where(item => item.EnterpriseId == enterpriseId
                    && item.GitHubLogin == signal.ScopeExternalId
                    && item.Status == IdentityMappingStatus.Matched)
                .Select(item => item.UserPrincipalName)
                .SingleOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(userPrincipalName))
            {
                users.Add(userPrincipalName);
            }
        }

        var scopeKind = Enum.Parse<ScopeKind>(signal.ScopeKind, true);
        var metadataJson = await dbContext.ManagedEntities
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId
                && item.ScopeKind == scopeKind
                && item.ExternalId == signal.ScopeExternalId)
            .Select(item => item.MetadataJson)
            .SingleOrDefaultAsync(cancellationToken);
        var owners = ParseOwnerPrincipalNames(metadataJson);
        return new NotificationRecipients(users.AsReadOnly(), owners);
    }

    public async Task<bool> SaveAsync(
        Guid enterpriseId,
        PlannedHealthNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (await dbContext.OutboxMessages.AnyAsync(item =>
                item.EnterpriseId == enterpriseId
                && item.EventFingerprint == notification.EventFingerprint,
                cancellationToken))
        {
            return false;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.OutboxMessages.Add(new OutboxMessageRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            EventType = "classification.health.changed.v1",
            EventFingerprint = notification.EventFingerprint,
            PayloadJson = WorkflowEventSerializer.Serialize(
                enterpriseId,
                "classification.health.changed.v1",
                notification.EventFingerprint,
                notification.Subject,
                new
                {
                    summary = notification.Summary,
                    scopeKind = notification.Signal.ScopeKind,
                    scopeExternalId = notification.Signal.ScopeExternalId,
                    status = notification.Signal.Status,
                    score = notification.Signal.Score,
                },
                notification.PlannedAt,
                notification.DashboardUrl,
                new WorkflowEventRecipients(
                    notification.Recipients.UserPrincipalNames,
                    notification.Recipients.OwnerPrincipalNames,
                    notification.AdminPrincipalNames)),
            Status = OutboxStatus.Pending,
            OccurredAt = notification.PlannedAt,
        });
        var allRecipients = notification.Recipients.UserPrincipalNames
            .Concat(notification.Recipients.OwnerPrincipalNames)
            .Concat(notification.AdminPrincipalNames)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string> channels = notification.Signal.ScopeKind.Equals(
            "User",
            StringComparison.OrdinalIgnoreCase)
            ? ["Outlook"]
            : deliveryChannels;
        foreach (var recipient in allRecipients)
        {
            foreach (var channel in channels)
            {
                dbContext.NotificationDeliveries.Add(new NotificationDeliveryRecord
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = enterpriseId,
                    EventFingerprint = notification.EventFingerprint,
                    Channel = channel,
                    RecipientKey = recipient,
                    TemplateVersion = "health-v1",
                    Status = DeliveryStatus.Pending,
                    CreatedAt = notification.PlannedAt,
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static string[] ParseOwnerPrincipalNames(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return [];
        }

        using var document = JsonDocument.Parse(metadataJson);
        if (!document.RootElement.TryGetProperty("ownerPrincipalNames", out var owners)
            || owners.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return owners.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            .Select(item => item.GetString()!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
