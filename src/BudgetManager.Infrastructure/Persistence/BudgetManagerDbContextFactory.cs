using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BudgetManager.Infrastructure.Persistence;

public sealed class BudgetManagerDbContextFactory : IDesignTimeDbContextFactory<BudgetManagerDbContext>
{
    public BudgetManagerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BudgetManagerDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=GitHubCopilotBudgetManager;Trusted_Connection=True")
            .Options;

        return new BudgetManagerDbContext(options);
    }
}
