using Azure.Messaging.ServiceBus;
using BudgetManager.Application.Messaging;

namespace BudgetManager.Infrastructure.Messaging;

public sealed class ServiceBusIntegrationEventPublisher(ServiceBusSender sender)
    : IIntegrationEventPublisher
{
    public Task PublishAsync(
        OutboxEnvelope message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var serviceBusMessage = new ServiceBusMessage(message.PayloadJson)
        {
            MessageId = message.Id.ToString("N"),
            SessionId = message.EnterpriseId.ToString("N"),
            Subject = message.EventType,
            ContentType = "application/json",
            CorrelationId = message.EventFingerprint,
        };
        serviceBusMessage.ApplicationProperties["eventType"] = message.EventType;
        serviceBusMessage.ApplicationProperties["eventFingerprint"] = message.EventFingerprint;
        serviceBusMessage.ApplicationProperties["occurredAt"] = message.OccurredAt.ToString("O");
        return sender.SendMessageAsync(serviceBusMessage, cancellationToken);
    }
}
