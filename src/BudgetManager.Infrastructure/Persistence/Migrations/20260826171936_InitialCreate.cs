using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetChangeRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActorObjectId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CallbackNonceHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDecisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BudgetBaselines",
                columns: table => new
                {
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    BaselineAmount = table.Column<long>(type: "bigint", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LastToolWrittenAmount = table.Column<long>(type: "bigint", nullable: true),
                    LastReconciledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetBaselines", x => new { x.EnterpriseId, x.BudgetId });
                });

            migrationBuilder.CreateTable(
                name: "BudgetChangeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpectedCurrentAmount = table.Column<long>(type: "bigint", nullable: false),
                    ProposedAmount = table.Column<long>(type: "bigint", nullable: false),
                    ForecastAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    DataFingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AutomaticMode = table.Column<bool>(type: "bit", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetChangeRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BudgetSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    BudgetType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BudgetScope = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BudgetEntityName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    UserLogin = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BudgetAmount = table.Column<long>(type: "bigint", nullable: false),
                    ConsumedAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    ProductSku = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PreventFurtherUsage = table.Column<bool>(type: "bit", nullable: false),
                    AlertingJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsEffective = table.Column<bool>(type: "bit", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClassificationSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ScopeExternalId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyVersion = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Score = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    ReasonsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InputsFingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassificationSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ScopeExternalId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MetricKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MetricValue = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    Availability = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AvailabilityDetail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MetricDay = table.Column<DateOnly>(type: "date", nullable: false),
                    IngestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyMetrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Enterprises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Enterprises", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EntityMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentScopeKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ParentExternalId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MemberScopeKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MemberExternalId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ObservedOn = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntityMemberships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdentityMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GitHubLogin = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntraObjectId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserPrincipalName = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Department = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    EntraCostCenterCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    GitHubCostCenterId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngestionManifests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReportStartDay = table.Column<DateOnly>(type: "date", nullable: false),
                    ReportEndDay = table.Column<DateOnly>(type: "date", nullable: false),
                    BlobName = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    SourceUrlHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ContentSha256 = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RecordCount = table.Column<int>(type: "int", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ErrorDetail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionManifests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManagedEntities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ParentScopeKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ParentExternalId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedEntities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventFingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RecipientKey = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    TemplateVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventFingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Governance = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ScopeKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ScopeExternalId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RetentionPolicies",
                columns: table => new
                {
                    EnterpriseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RawReportDays = table.Column<int>(type: "int", nullable: false),
                    UserMetricDays = table.Column<int>(type: "int", nullable: false),
                    AggregateMetricDays = table.Column<int>(type: "int", nullable: false),
                    NotificationDays = table.Column<int>(type: "int", nullable: false),
                    AuditDays = table.Column<int>(type: "int", nullable: false),
                    LegalHold = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionPolicies", x => x.EnterpriseId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisions_BudgetChangeRequestId",
                table: "ApprovalDecisions",
                column: "BudgetChangeRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisions_CallbackNonceHash",
                table: "ApprovalDecisions",
                column: "CallbackNonceHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_EnterpriseId_EventType_OccurredAt",
                table: "AuditEvents",
                columns: new[] { "EnterpriseId", "EventType", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_EnterpriseId_OccurredAt",
                table: "AuditEvents",
                columns: new[] { "EnterpriseId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetChangeRequests_EnterpriseId_BudgetId_DataFingerprint",
                table: "BudgetChangeRequests",
                columns: new[] { "EnterpriseId", "BudgetId", "DataFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetChangeRequests_EnterpriseId_Status_CreatedAt",
                table: "BudgetChangeRequests",
                columns: new[] { "EnterpriseId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetSnapshots_EnterpriseId_BudgetId_ObservedAt",
                table: "BudgetSnapshots",
                columns: new[] { "EnterpriseId", "BudgetId", "ObservedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetSnapshots_EnterpriseId_IsEffective_ObservedAt",
                table: "BudgetSnapshots",
                columns: new[] { "EnterpriseId", "IsEffective", "ObservedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationSnapshots_EnterpriseId_ScopeKind_ScopeExternalId_EvaluatedAt",
                table: "ClassificationSnapshots",
                columns: new[] { "EnterpriseId", "ScopeKind", "ScopeExternalId", "EvaluatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationSnapshots_PolicyId_InputsFingerprint",
                table: "ClassificationSnapshots",
                columns: new[] { "PolicyId", "InputsFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyMetrics_EnterpriseId_MetricDay_MetricKey",
                table: "DailyMetrics",
                columns: new[] { "EnterpriseId", "MetricDay", "MetricKey" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyMetrics_EnterpriseId_ScopeKind_ScopeExternalId_MetricKey_MetricDay",
                table: "DailyMetrics",
                columns: new[] { "EnterpriseId", "ScopeKind", "ScopeExternalId", "MetricKey", "MetricDay" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Enterprises_Slug",
                table: "Enterprises",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntityMemberships_EnterpriseId_ParentScopeKind_ParentExternalId_MemberScopeKind_MemberExternalId_ObservedOn",
                table: "EntityMemberships",
                columns: new[] { "EnterpriseId", "ParentScopeKind", "ParentExternalId", "MemberScopeKind", "MemberExternalId", "ObservedOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdentityMappings_EnterpriseId_EntraObjectId",
                table: "IdentityMappings",
                columns: new[] { "EnterpriseId", "EntraObjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_IdentityMappings_EnterpriseId_GitHubLogin",
                table: "IdentityMappings",
                columns: new[] { "EnterpriseId", "GitHubLogin" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngestionManifests_EnterpriseId_ReportType_ReportStartDay_ReportEndDay_ContentSha256",
                table: "IngestionManifests",
                columns: new[] { "EnterpriseId", "ReportType", "ReportStartDay", "ReportEndDay", "ContentSha256" },
                unique: true,
                filter: "[ContentSha256] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IngestionManifests_EnterpriseId_Status_StartedAt",
                table: "IngestionManifests",
                columns: new[] { "EnterpriseId", "Status", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ManagedEntities_EnterpriseId_ParentScopeKind_ParentExternalId",
                table: "ManagedEntities",
                columns: new[] { "EnterpriseId", "ParentScopeKind", "ParentExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_ManagedEntities_EnterpriseId_ScopeKind_ExternalId",
                table: "ManagedEntities",
                columns: new[] { "EnterpriseId", "ScopeKind", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_EnterpriseId_EventFingerprint_Channel_RecipientKey",
                table: "NotificationDeliveries",
                columns: new[] { "EnterpriseId", "EventFingerprint", "Channel", "RecipientKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_EnterpriseId_EventFingerprint",
                table: "OutboxMessages",
                columns: new[] { "EnterpriseId", "EventFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_OccurredAt",
                table: "OutboxMessages",
                columns: new[] { "Status", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Policies_EnterpriseId_Name_Version",
                table: "Policies",
                columns: new[] { "EnterpriseId", "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Policies_EnterpriseId_ScopeKind_ScopeExternalId_IsActive",
                table: "Policies",
                columns: new[] { "EnterpriseId", "ScopeKind", "ScopeExternalId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalDecisions");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "BudgetBaselines");

            migrationBuilder.DropTable(
                name: "BudgetChangeRequests");

            migrationBuilder.DropTable(
                name: "BudgetSnapshots");

            migrationBuilder.DropTable(
                name: "ClassificationSnapshots");

            migrationBuilder.DropTable(
                name: "DailyMetrics");

            migrationBuilder.DropTable(
                name: "Enterprises");

            migrationBuilder.DropTable(
                name: "EntityMemberships");

            migrationBuilder.DropTable(
                name: "IdentityMappings");

            migrationBuilder.DropTable(
                name: "IngestionManifests");

            migrationBuilder.DropTable(
                name: "ManagedEntities");

            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "Policies");

            migrationBuilder.DropTable(
                name: "RetentionPolicies");
        }
    }
}
