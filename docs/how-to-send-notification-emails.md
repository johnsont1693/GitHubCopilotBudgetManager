# How to send notification email

This guide runs the health-notification capability without the dashboard or GitHub budget
write access. It creates Outlook delivery records from yellow, red, or unknown classifications,
publishes a privacy-minimized event, and lets a tenant-owned Logic Apps or Power Automate
flow send the email.

```text
classify (optional) -> plan-notifications -> dispatch-outbox -> Outlook flow
       SQL                 SQL transaction       Service Bus     tenant connector
```

Planning and sending are intentionally separate. A database transaction cannot directly
send an email.

## 1. Prerequisites

- PowerShell 7.2 or later and the .NET SDK selected by [`global.json`](../global.json).
- SQL Server or Azure SQL with the Budget Manager schema.
- At least one latest `yellow`, `red`, or `unknown` classification signal.
- At least one recipient source: matched user identity, entity owner metadata, or
  `NOTIFICATION_ADMIN_RECIPIENTS`.
- Azure Service Bus for the built-in outbox dispatcher.
- An authorized Outlook connector in Logic Apps or Power Automate.

GitHub, Blob Storage, and report ingestion are needed only when this repository must ingest
the source metrics. Microsoft Graph is needed only to populate direct user identity
mappings. Admin-only and owner-only routing can operate without Graph.

## 2. Configure planning and dispatch

Set the shared and planning variables:

```powershell
$env:BUDGET_MANAGER_SQL_CONNECTION_STRING = '<SQL connection string>'
$env:GITHUB_ENTERPRISE_ID = '<internal non-empty enterprise GUID>'
$env:NOTIFICATION_ADMIN_RECIPIENTS = 'copilot-admins@contoso.com;finops@contoso.com'
$env:NOTIFICATION_CHANNELS = 'Outlook'
$env:DASHBOARD_URL = 'https://<optional-dashboard-or-runbook>' # Optional
```

Set the dispatcher variables:

```powershell
$env:SERVICE_BUS_NAMESPACE = '<namespace>.servicebus.windows.net'
$env:SERVICE_BUS_QUEUE_NAME = '<queue-name>'
```

The dispatcher uses `DefaultAzureCredential`; grant its identity Service Bus Data Sender on
the queue or namespace. The external Logic Apps or Power Automate connection needs receive
access. Keep sender and receiver identities separate.

`NOTIFICATION_CHANNELS=Outlook` is the email-only mode. Omitting it preserves the default
`Teams;Outlook` plan. Outlook cannot be removed because individual user notifications are
privacy-restricted to direct email.

Apply migrations once:

```powershell
dotnet run --project src/BudgetManager.Worker -- migrate
```

## 3. Prepare classification signals

`plan-notifications` reads the latest classification for each entity and selects `yellow`,
`red`, and `unknown`. It ignores an older poor status when a newer status is green.

If policies, metrics, and current classifications already exist, skip directly to planning.
If policies and metrics exist but classifications need refreshing, use the composed action
in step 4. If neither exists, either run the repository's ingestion and classification
pipeline or implement `IHealthNotificationStore` in a custom host for another signal source.

Recipient precedence is additive:

- A user signal uses a matched GitHub-to-Entra identity when available.
- Any managed entity may supply `ownerPrincipalNames` in its metadata.
- `NOTIFICATION_ADMIN_RECIPIENTS` supplies semicolon-separated fallback recipients.
- A signal with no recipient from any source is counted as `SkippedNoRecipient`.

## 4. Plan without sending

Preview the operation without credentials:

```powershell
./scripts/Invoke-NotificationAutomation.ps1 -Action Plan -WhatIf
```

Create deduplicated delivery and outbox records:

```powershell
./scripts/Invoke-NotificationAutomation.ps1 -Action Plan
```

Re-running planning against the same snapshot and status does not create a second event.
The fingerprint is based on the classification snapshot and status.

## 5. Build an email-only Power Automate flow

Create the flow inside a Power Platform Solution so connection references and environment
variables can be promoted safely.

1. Add a Service Bus trigger or approved custom connector for the workflow-events queue.
2. Parse the message body using
   [`deploy/power-automate/event.schema.json`](../deploy/power-automate/event.schema.json).
3. Reject messages whose `schemaVersion` is not `1`.
4. Continue only for `classification.health.changed.v1` when this flow is email-only.
5. Combine and deduplicate `userPrincipalNames`, `ownerPrincipalNames`, and
   `adminPrincipalNames` from `recipients`.
6. For each recipient, use **Send an email (V2)** with `subject`, `payload.summary`, and the
   optional `dashboardUrl`.
7. Record failures and configure an alert. Do not log access tokens or detailed individual
   metrics.

The event contains status, score, scope, summary, and recipient routing. It does not contain
raw individual activity. Do not enrich a shared message with user-level evidence. See
[`deploy/power-automate/README.md`](../deploy/power-automate/README.md) for the complete event
contract and optional adaptive cards.

## 6. Dispatch and send

After the flow is authorized and enabled, publish pending events:

```powershell
./scripts/Invoke-NotificationAutomation.ps1 -Action Dispatch
```

For routine operation, plan and dispatch together:

```powershell
./scripts/Invoke-NotificationAutomation.ps1 -Action PlanAndDispatch
```

To recompute classifications from existing policies and metrics first:

```powershell
./scripts/Invoke-NotificationAutomation.ps1 -Action ClassifyPlanAndDispatch
```

Each dispatcher run claims up to 100 pending outbox records. Service Bus delivery is at
least once, so the flow should treat `eventFingerprint` as its idempotency key.

## 7. Schedule it

Run classification after the metric correction window, planning after classification, and
dispatch frequently enough for the expected delivery objective. A generic scheduled command
is:

```text
pwsh -File scripts/Invoke-NotificationAutomation.ps1 -Action PlanAndDispatch -NoBuild
```

The full Azure deployment uses separate `classify`, `plan-notifications`, and
`dispatch-outbox` Container Apps Jobs. Separate jobs make pausing delivery possible without
stopping classification or losing planned events.

## 8. Validate and troubleshoot

Validate with a non-production recipient before enabling the production schedule:

1. Confirm planning created one pending Outlook delivery and one pending outbox record.
2. Run dispatch and confirm the outbox record becomes processed.
3. Confirm the Service Bus message has `schemaVersion=1` and the expected fingerprint.
4. Confirm the flow sent exactly one email to the test recipient.
5. Confirm individual details were not posted to a shared Teams channel.

| Symptom | Check |
| --- | --- |
| `Evaluated=0` | No qualifying latest classifications exist; run `classify` or inspect policy/metric freshness. |
| `SkippedNoRecipient` increases | Configure admin recipients, owner metadata, or a matched identity mapping. |
| Duplicate flow runs | Deduplicate on `eventFingerprint`; Service Bus is at least once. |
| Outbox stays pending | Check dispatcher SQL access, Service Bus Data Sender, namespace, and queue name. |
| Outbox is processed but no email arrives | Check the receiver connection, flow run history, connector authorization, and mailbox policy. |
| Teams rows appear in email-only mode | Set `NOTIFICATION_CHANNELS=Outlook` on the planning process, not only the dispatcher. |
| Link is absent | Set optional `DASHBOARD_URL`; delivery does not require it. |

See [Privacy and retention](privacy.md), [Operations](operations.md), and
[Modular automation](modular-automation.md) before production rollout.