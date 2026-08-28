using BudgetManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Api;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        string provider,
        bool seedDemoData,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BudgetManagerDbContext>();
        if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }
        else
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        if (seedDemoData)
        {
            await DevelopmentDataSeeder.SeedAsync(dbContext, cancellationToken);
        }
    }
}
