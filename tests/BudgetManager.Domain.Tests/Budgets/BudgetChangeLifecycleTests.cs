using BudgetManager.Domain.Budgets;

namespace BudgetManager.Domain.Tests.Budgets;

public sealed class BudgetChangeLifecycleTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Approved_request_can_execute_and_be_applied_once()
    {
        var request = CreateRequest();
        var initialToken = request.ConcurrencyToken;

        request.Approve("admin-object-id", CreatedAt.AddHours(1));
        request.BeginExecution();
        request.MarkApplied();

        Assert.Equal(BudgetChangeLifecycleStatus.Applied, request.Status);
        Assert.Equal("admin-object-id", request.DecisionActor);
        Assert.NotEqual(initialToken, request.ConcurrencyToken);
        Assert.Throws<InvalidOperationException>(() => request.MarkApplied());
    }

    [Fact]
    public void Expired_request_cannot_be_approved()
    {
        var request = CreateRequest();

        Assert.Throws<InvalidOperationException>(
            () => request.Approve("admin-object-id", CreatedAt.AddDays(3)));

        Assert.Equal(BudgetChangeLifecycleStatus.Expired, request.Status);
    }

    private static BudgetChangeLifecycle CreateRequest() => new(
        Guid.NewGuid(),
        "budget-1",
        1_000,
        1_250,
        "fingerprint",
        false,
        CreatedAt,
        CreatedAt.AddDays(2));
}
