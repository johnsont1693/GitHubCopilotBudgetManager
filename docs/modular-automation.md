# Modular automation

The repository can be adopted as a full application or as two smaller automation
capabilities. The dashboard is not required by either capability. The scripts in
[`scripts`](../scripts/README.md) are thin entry points over the same tested application
services used by the deployed jobs; they do not duplicate financial or privacy logic.

## Choose a capability

| Component | Budget increases | Notification email |
| --- | --- | --- |
| .NET Worker | Required for synchronization and execution | Required for planning and dispatch |
| SQL schema | Required | Required |
| GitHub App | Required with budget read/write permission | Not required when classifications already exist |
| Administration API | Required for the built-in proposal and approval flow | Not required |
| Dashboard | Optional | Optional |
| Service Bus | Optional | Required by the built-in dispatcher |
| Logic Apps or Power Automate | Optional, only for lifecycle messages | Required for final Outlook delivery |
| Microsoft Graph | Not required | Optional for direct user routing |
| Copilot report ingestion | Not required | Required only when this system must create classifications from GitHub metrics |
| Blob Storage | Not required | Required only by the built-in report ingestion path |

Both capabilities use the shared SQL model because it provides idempotency, concurrency,
audit history, and the transactional outbox. They do not require the full Azure topology.
A customer may host the Worker and API in any environment that supports .NET 10 and the
configured SQL and identity providers.

## Supported entry points

### Budget automation

- [`BudgetIncreaseGuardrailEvaluator`](../src/BudgetManager.Domain/Budgets/BudgetIncreaseGuardrails.cs)
  is pure domain logic with no network or persistence dependency.
- [`BudgetChangeProposalService` and `BudgetWriteExecutor`](../src/BudgetManager.Application/Budgets/BudgetChangeServices.cs)
  define the proposal and execution use cases behind interfaces.
- [`EfBudgetChangeRepository`](../src/BudgetManager.Infrastructure/Persistence/EfBudgetChangeRepository.cs)
  provides approval concurrency, immutable audit, and optional lifecycle events.
- [`Invoke-BudgetAutomation.ps1`](../scripts/Invoke-BudgetAutomation.ps1) exposes read-only
  synchronization and approved-request execution.
- The API supplies the built-in authenticated proposal and approval surface. A custom host
  may call the application services directly instead.

### Notification automation

- [`HealthNotificationPlanner`](../src/BudgetManager.Application/Notifications/HealthNotificationPlanner.cs)
  plans notifications behind `IHealthNotificationStore`.
- [`EfHealthNotificationStore`](../src/BudgetManager.Infrastructure/Persistence/EfHealthNotificationStore.cs)
  resolves recipients and atomically writes deduplicated delivery and outbox records.
- [`OutboxDispatcher`](../src/BudgetManager.Application/Messaging/OutboxDispatch.cs) publishes
  pending records behind `IIntegrationEventPublisher`.
- [`Invoke-NotificationAutomation.ps1`](../scripts/Invoke-NotificationAutomation.ps1)
  exposes classification, planning, and dispatch as separate or composed operations.
- [`deploy/power-automate`](../deploy/power-automate/README.md) contains the external event
  schema and presentation templates. The final sender remains tenant-owned.

## Deployment shapes

### Scripts with the repository

This is the simplest supported extraction. Keep the following files and directories:

```text
global.json
Directory.Build.props
Directory.Packages.props
src/BudgetManager.Domain/
src/BudgetManager.Application/
src/BudgetManager.Infrastructure/
src/BudgetManager.Worker/
scripts/Invoke-BudgetAutomation.ps1          # budget capability
scripts/Invoke-NotificationAutomation.ps1    # notification capability
```

The shared projects contain more than one capability today. Keeping their project boundary
avoids copying individual source files without transitive contracts, package versions, or
EF mappings. Consumers embedding the logic in another host should reference the owning
types listed above and implement their interfaces rather than importing API or dashboard
code.

### Existing Azure deployment

The Bicep deployment creates a separate Container Apps Job for every Worker command. A
customer may enable only the schedules needed for its selected capability. The default job
definitions are documented in [`infra/README.md`](../infra/README.md#schedules).

### Custom host

The application layer has no dependency on ASP.NET Core, Azure Service Bus, Logic Apps,
Power Automate, or the dashboard. A custom host can provide:

- `IBudgetChangeRepository` and `IGitHubBudgetClient` for budget automation.
- `IHealthNotificationStore`, `IOutboxStore`, and `IIntegrationEventPublisher` for
  notification automation.

Keep the domain guardrails, approval lifecycle, drift check, recipient privacy rules, and
event fingerprint behavior intact when replacing adapters.

## Optional integrations

`BUDGET_WRITES_ENABLED` defaults to `false`. Direct `execute-approved` Worker calls are
rejected unless it is explicitly `true`; the budget wrapper enables it only for the selected
execution process. In the Azure deployment, `false` also keeps the executor job manual.

Set `PUBLISH_BUDGET_LIFECYCLE_EVENTS=false` in both API and Worker hosts for a budget-only
installation. Budget requests and audit events are still written; budget lifecycle outbox
records are not. The default is `true` for backward compatibility. The API also accepts
the structured setting `WorkflowEvents:PublishBudgetLifecycleEvents`.

Set `NOTIFICATION_CHANNELS=Outlook` on the `plan-notifications` Worker to create an
email-only delivery plan. The default is `Teams;Outlook`. Outlook remains mandatory because
individual user notifications must not be sent to shared Teams channels.

`DASHBOARD_URL` is optional for notification planning. When omitted, the workflow event has
no dashboard link. `GRAPH_GITHUB_LOGIN_PROPERTY` and `sync-identities` are also optional if
owner metadata or `NOTIFICATION_ADMIN_RECIPIENTS` provide the intended recipients.

## Safety contract

- `sync-budgets` is read-only.
- A budget proposal must pass every financial guardrail before it is persisted.
- Built-in API guardrails are server-owned configuration; proposal callers cannot supply or
  widen their own limits. The API also derives current amount, forecast, monthly cumulative
  increase, cooldown state, freshness, and fingerprints from persistence. Base production
  settings fail closed.
- The built-in API creates manual proposals only; they remain pending until an
  `EnterpriseAdmin` approves them. Automatic mode is reserved for separately governed custom
  hosts using server-owned policy inputs.
- Budget execution is disabled by default independently of GitHub App permissions.
- `execute-approved` never creates or approves requests. It re-reads the GitHub budget and
  refuses the write when the authoritative amount changed.
- `plan-notifications` never sends email. It writes deduplicated records transactionally.
- `dispatch-outbox` publishes events but does not own Microsoft 365 credentials.
- The tenant-owned Logic Apps or Power Automate flow performs Outlook delivery.

## Validation

Run the capability tests without deploying cloud resources:

```powershell
dotnet test tests/BudgetManager.Domain.Tests/BudgetManager.Domain.Tests.csproj `
  --configuration Release
dotnet test tests/BudgetManager.Infrastructure.Tests/BudgetManager.Infrastructure.Tests.csproj `
  --configuration Release `
  --filter 'FullyQualifiedName~Budget|FullyQualifiedName~Notification|FullyQualifiedName~Outbox'
```

Inspect every Worker command without credentials:

```powershell
dotnet run --project src/BudgetManager.Worker -- --help
Get-Help ./scripts/Invoke-BudgetAutomation.ps1 -Full
Get-Help ./scripts/Invoke-NotificationAutomation.ps1 -Full
```

Continue with [automating budget increases](how-to-automate-budget-increases.md) or
[sending notification email](how-to-send-notification-emails.md).