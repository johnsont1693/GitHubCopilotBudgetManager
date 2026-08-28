using System.Text.Json;
using BudgetManager.Application.Messaging;

namespace BudgetManager.Infrastructure.Tests.Messaging;

public sealed class WorkflowEventSerializerTests
{
    [Fact]
    public void Serialize_creates_schema_v1_envelope_and_normalizes_recipients()
    {
        var enterpriseId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

        var json = WorkflowEventSerializer.Serialize(
            enterpriseId,
            "classification.health.changed.v1",
            "health:one:yellow",
            "Health changed",
            new { summary = "A user needs coaching." },
            occurredAt,
            "https://dashboard.example/",
            new WorkflowEventRecipients(
                [" user@contoso.com ", "USER@contoso.com"],
                [],
                ["admin@contoso.com"]));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("classification.health.changed.v1", root.GetProperty("eventType").GetString());
        Assert.Equal(enterpriseId, root.GetProperty("enterpriseId").GetGuid());
        Assert.Equal("https://dashboard.example/", root.GetProperty("dashboardUrl").GetString());
        Assert.Equal(
            "user@contoso.com",
            Assert.Single(root.GetProperty("recipients").GetProperty("userPrincipalNames").EnumerateArray()).GetString());
        Assert.Equal("A user needs coaching.", root.GetProperty("payload").GetProperty("summary").GetString());
    }
}
