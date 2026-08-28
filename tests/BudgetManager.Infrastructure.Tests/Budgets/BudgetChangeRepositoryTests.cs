using System.Text.Json;
using BudgetManager.Application.Budgets;
using BudgetManager.Application.Messaging;
using BudgetManager.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Tests.Budgets;

public sealed class BudgetChangeRepositoryTests
{
    [Fact]
    public async Task CreateAsync_puts_persisted_concurrency_token_in_approval_event()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BudgetManagerDbContext>().UseSqlite(connection).Options;
        await using var context = new BudgetManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var enterpriseId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
        context.Enterprises.Add(new EnterpriseRecord
        {
            Id = enterpriseId,
            Slug = "acme",
            DisplayName = "Acme",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await context.SaveChangesAsync();
        var repository = new EfBudgetChangeRepository(
            context,
            new WorkflowEventOptions("https://dashboard.example/", ["admin@contoso.com"]));

        var requestId = await repository.CreateAsync(new PendingBudgetChange(
            enterpriseId,
            "budget-1",
            1_000,
            1_250,
            1_200m,
            "data-fingerprint",
            "{}",
            false,
            now,
            now.AddDays(2)));

        var request = await context.BudgetChangeRequests.SingleAsync(item => item.Id == requestId);
        var outbox = await context.OutboxMessages.SingleAsync();
        using var document = JsonDocument.Parse(outbox.PayloadJson);
        Assert.Equal(
            request.ConcurrencyToken,
            document.RootElement.GetProperty("payload").GetProperty("concurrencyToken").GetGuid());
    }

    [Fact]
    public async Task CreateAsync_can_preserve_audit_without_publishing_budget_events()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BudgetManagerDbContext>().UseSqlite(connection).Options;
        await using var context = new BudgetManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var enterpriseId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
        context.Enterprises.Add(new EnterpriseRecord
        {
            Id = enterpriseId,
            Slug = "acme",
            DisplayName = "Acme",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await context.SaveChangesAsync();
        var repository = new EfBudgetChangeRepository(
            context,
            new WorkflowEventOptions(null, [], false));

        await repository.CreateAsync(new PendingBudgetChange(
            enterpriseId,
            "budget-1",
            1_000,
            1_250,
            1_200m,
            "data-fingerprint",
            "{}",
            false,
            now,
            now.AddDays(2)));

        Assert.Single(context.BudgetChangeRequests);
        Assert.Single(context.AuditEvents);
        Assert.Empty(context.OutboxMessages);
    }

    [Fact]
    public async Task Approval_concurrency_token_allows_only_one_decision()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BudgetManagerDbContext>().UseSqlite(connection).Options;
        await using var context = new BudgetManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var enterpriseId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
        context.Enterprises.Add(new EnterpriseRecord
        {
            Id = enterpriseId,
            Slug = "acme",
            DisplayName = "Acme",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await context.SaveChangesAsync();
        var repository = new EfBudgetChangeRepository(context, new WorkflowEventOptions(null, [], false));
        var requestId = await repository.CreateAsync(new PendingBudgetChange(
            enterpriseId,
            "budget-1",
            1_000,
            1_250,
            1_200m,
            "decision-fingerprint",
            "{}",
            false,
            now,
            now.AddDays(2)));
        var originalToken = await context.BudgetChangeRequests
            .Where(item => item.Id == requestId)
            .Select(item => item.ConcurrencyToken)
            .SingleAsync();

        var approved = await repository.ApproveAsync(
            requestId,
            originalToken,
            "approver-object-id",
            now.AddMinutes(1));
        var rejected = await repository.RejectAsync(
            requestId,
            originalToken,
            "approver-object-id",
            "late competing decision",
            now.AddMinutes(1));

        Assert.True(approved);
        Assert.False(rejected);
        Assert.Single(context.ApprovalDecisions);
        Assert.Equal(
            BudgetChangeStatus.Approved,
            (await context.BudgetChangeRequests.SingleAsync(item => item.Id == requestId)).Status);
    }

    [Fact]
    public async Task Execution_claim_allows_only_one_worker_to_transition_an_approved_request()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BudgetManagerDbContext>().UseSqlite(connection).Options;
        await using var context = new BudgetManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var enterpriseId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
        context.Enterprises.Add(new EnterpriseRecord
        {
            Id = enterpriseId,
            Slug = "acme",
            DisplayName = "Acme",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await context.SaveChangesAsync();
        var repository = new EfBudgetChangeRepository(context, new WorkflowEventOptions(null, [], false));
        var requestId = await repository.CreateAsync(new PendingBudgetChange(
            enterpriseId,
            "budget-1",
            1_000,
            1_250,
            1_200m,
            "execution-fingerprint",
            "{}",
            true,
            now,
            now.AddDays(2)));
        var candidate = await repository.GetExecutionCandidateAsync(requestId);

        var firstClaim = await repository.TryBeginExecutionAsync(
            requestId,
            Assert.IsType<BudgetChangeExecutionCandidate>(candidate).ConcurrencyToken);
        var secondClaim = await repository.TryBeginExecutionAsync(
            requestId,
            candidate.ConcurrencyToken);

        Assert.True(firstClaim);
        Assert.False(secondClaim);
    }

    [Fact]
    public async Task Exact_proposal_retry_returns_existing_request_without_duplicate_evidence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BudgetManagerDbContext>().UseSqlite(connection).Options;
        await using var context = new BudgetManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var enterpriseId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
        context.Enterprises.Add(new EnterpriseRecord
        {
            Id = enterpriseId,
            Slug = "acme",
            DisplayName = "Acme",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await context.SaveChangesAsync();
        var repository = new EfBudgetChangeRepository(
            context,
            new WorkflowEventOptions("https://dashboard.example/", ["admin@contoso.com"]));
        var proposal = new PendingBudgetChange(
            enterpriseId,
            "budget-1",
            1_000,
            1_100,
            1_050m,
            "idempotent-fingerprint",
            "{}",
            false,
            now,
            now.AddDays(2));

        var firstId = await repository.CreateAsync(proposal);
        var retryId = await repository.CreateAsync(proposal);

        Assert.Equal(firstId, retryId);
        Assert.Single(context.BudgetChangeRequests);
        Assert.Single(context.AuditEvents);
        Assert.Single(context.OutboxMessages);
    }
}
