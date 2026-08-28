using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddForecastSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ForecastSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    AsOfDay = table.Column<DateOnly>(type: "date", nullable: false),
                    BudgetAmount = table.Column<long>(type: "bigint", nullable: false),
                    MonthToDateSpend = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    ProjectedSpend = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    UtilizationPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    IsUsable = table.Column<bool>(type: "bit", nullable: false),
                    ComponentsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InputsFingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForecastSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ForecastSnapshots_EnterpriseId_AsOfDay",
                table: "ForecastSnapshots",
                columns: new[] { "EnterpriseId", "AsOfDay" });

            migrationBuilder.CreateIndex(
                name: "IX_ForecastSnapshots_EnterpriseId_BudgetId_InputsFingerprint",
                table: "ForecastSnapshots",
                columns: new[] { "EnterpriseId", "BudgetId", "InputsFingerprint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ForecastSnapshots");
        }
    }
}
