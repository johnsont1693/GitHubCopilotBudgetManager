using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialObservability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingReportExports",
                columns: table => new
                {
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DownloadUrlCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Actor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FirstObservedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastObservedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingReportExports", x => new { x.EnterpriseId, x.ReportId });
                });

            migrationBuilder.CreateTable(
                name: "BudgetUserStateSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UserLogin = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ConsumedAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    TargetAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    OverrideBudgetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetUserStateSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingReportExports_EnterpriseId_Status_LastObservedAt",
                table: "BillingReportExports",
                columns: new[] { "EnterpriseId", "Status", "LastObservedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetUserStateSnapshots_EnterpriseId_BudgetId_UserLogin_ObservedAt",
                table: "BudgetUserStateSnapshots",
                columns: new[] { "EnterpriseId", "BudgetId", "UserLogin", "ObservedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetUserStateSnapshots_EnterpriseId_UserLogin_ObservedAt",
                table: "BudgetUserStateSnapshots",
                columns: new[] { "EnterpriseId", "UserLogin", "ObservedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingReportExports");

            migrationBuilder.DropTable(
                name: "BudgetUserStateSnapshots");
        }
    }
}
