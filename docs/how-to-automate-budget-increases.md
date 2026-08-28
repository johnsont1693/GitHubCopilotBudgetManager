# How to automate budget increases

This guide runs only the budget capability. It does not require the dashboard, report
ingestion, Blob Storage, Microsoft Graph, Service Bus, or a notification workflow.

The built-in flow deliberately separates proposal, approval, and execution:

```text
sync-budgets -> propose -> approve -> execute-approved -> verify
     read          SQL       SQL          GitHub PATCH       read
```

`execute-approved` cannot bypass approval. Automatic mode is an explicit proposal setting,
not a Worker switch.

## 1. Prerequisites

- PowerShell 7.2 or later.
- .NET SDK selected by [`global.json`](../global.json).
- SQL Server or Azure SQL with the Budget Manager schema.
- A GitHub App installed on the enterprise.
- Budget read permission for synchronization and budget write permission for execution.
- An authenticated `EnterpriseAdmin` caller if using the built-in API for proposals.

Start with budget read permission only. Add write permission after read-only synchronization,
guardrail review, staging validation, and an approved change have all succeeded.

## 2. Configure the Worker process

Set these variables in the process, scheduler, or secret-backed workload environment:

```powershell
$env:BUDGET_MANAGER_SQL_CONNECTION_STRING = '<SQL connection string>'
$env:GITHUB_ENTERPRISE_ID = '<internal non-empty enterprise GUID>'
$env:GITHUB_ENTERPRISE_SLUG = '<github-enterprise-slug>'
$env:GITHUB_APP_ISSUER = '<github-app-id>'
$env:GITHUB_APP_INSTALLATION_ID = '<positive-installation-id>'
$env:GITHUB_APP_PRIVATE_KEY = Get-Content '<path-to-private-key.pem>' -Raw
$env:BUDGET_WRITES_ENABLED = 'false'
$env:PUBLISH_BUDGET_LIFECYCLE_EVENTS = 'false'
```

`GITHUB_ENTERPRISE_ID` is this application's stable GUID, not the GitHub slug. Store the
private key in a secret manager in production. The provider also accepts PEM text whose
newlines are encoded as `\n`.

Set `PUBLISH_BUDGET_LIFECYCLE_EVENTS=true` only when a configured outbox dispatcher and
workflow should receive budget lifecycle events. The same value must be set on the API host
that creates and approves requests.

`BUDGET_WRITES_ENABLED` is an independent application kill switch. The Worker rejects
`execute-approved` before loading SQL or GitHub credentials unless it is `true`. The wrapper
sets it to `true` only for an explicitly selected `ExecuteApproved` invocation and restores
the previous process value afterward.

Apply migrations once:

```powershell
dotnet run --project src/BudgetManager.Worker -- migrate
```

## 3. Synchronize authoritative budgets

Preview the wrapper invocation without connecting:

```powershell
./scripts/Invoke-BudgetAutomation.ps1 -Action Sync -WhatIf
```

Run the read-only synchronization:

```powershell
./scripts/Invoke-BudgetAutomation.ps1 -Action Sync
```

Verify that the latest budget snapshots match GitHub before enabling writes. The full Azure
deployment schedules the same `sync-budgets` command; the script is not a second
implementation.

## 4. Create a guarded proposal

The dashboard is optional. Use `POST /api/v1/budget-change-requests` directly or call
`BudgetChangeProposalService` from a custom host. The production API requires an access
token with the `EnterpriseAdmin` role.

```powershell
$apiBase = 'https://<api-host>'
$accessToken = '<Entra access token>'
$headers = @{ Authorization = "Bearer $accessToken" }
$enterpriseId = [Guid]$env:GITHUB_ENTERPRISE_ID

$body = @{
    enterpriseId = $enterpriseId
    proposal = @{
        budgetId = '<github-budget-id>'
        proposedAmount = 28000
    }
    automaticMode = $false
} | ConvertTo-Json -Depth 3

$created = Invoke-RestMethod `
    -Method Post `
    -Uri "$apiBase/api/v1/budget-change-requests" `
    -Headers $headers `
    -ContentType 'application/json' `
    -Body $body
$requestId = [Guid]$created.requestId
```

The proposal caller owns only the requested amount. The API loads the latest authoritative
budget snapshot, latest usable forecast, current-month applied increase total, latest
tool-applied request, timestamps, and fingerprints from SQL. It refuses the request with
`409 Conflict` if the budget is not synchronized, the forecast is missing/unusable, or the
forecast amount no longer matches the budget. The persisted evidence identifies both source
snapshots. The server fingerprint includes those snapshots, the proposed amount, cumulative
increase state, and prior applied fingerprint: exact retries are deduplicated while a
different proposed amount remains a distinct review request.

The API also reads deployment-owned `BudgetGuardrails` configuration. The Azure template
requires the customer to set:

- `BUDGET_MAXIMUM_INCREASE_AMOUNT`
- `BUDGET_MAXIMUM_INCREASE_PERCENT`
- `BUDGET_MAXIMUM_CUMULATIVE_MONTHLY_INCREASE`
- `BUDGET_FORECAST_HEADROOM_PERCENT`
- `BUDGET_COOLDOWN_HOURS`
- `BUDGET_MAXIMUM_DATA_AGE_HOURS`

`BUDGET_APPROVAL_LIFETIME_HOURS` optionally changes the default 48-hour approval expiry and
is limited to 1–168 hours.

Base production settings are fail-closed at zero increase. Document the approved values and
their owner in change control; do not treat the Development values as recommendations.

The API returns `422 Unprocessable Entity` with all guardrail violations when the proposal
is unsafe. Do not retry by widening limits automatically. Review the forecast, input age,
cooldown, cumulative increase, and fingerprint first.

## 5. Approve the request

Manual mode is the default. Read the current concurrency token, present the evidence to an
administrator, then submit the decision:

```powershell
$requests = Invoke-RestMethod `
    -Uri "$apiBase/api/v1/budget-change-requests?enterpriseId=$enterpriseId&page=1&pageSize=100" `
    -Headers $headers
$request = $requests.items | Where-Object { [Guid]$_.id -eq $requestId } | Select-Object -First 1
if (-not $request) { throw "Request $requestId was not found." }

$decision = @{ expectedConcurrencyToken = $request.concurrencyToken } | ConvertTo-Json
Invoke-RestMethod `
    -Method Post `
    -Uri "$apiBase/api/v1/budget-change-requests/$requestId/approve" `
    -Headers $headers `
    -ContentType 'application/json' `
    -Body $decision
```

A `409 Conflict` means the request expired, changed, or was decided concurrently. Reload it;
do not resubmit the stale token.

The built-in API rejects `automaticMode=true`; every customer-facing proposal requires a
separate approval transition. The application service retains automatic mode as an extension
point for a custom host, but enabling it requires separate governance, server-owned policy
inputs, narrow workload identity, conservative limits, and staged rollout. The Worker still
performs the independent write gate and drift check.

## 6. Execute an approved request

Start with `-WhatIf`, then execute exactly one request:

```powershell
./scripts/Invoke-BudgetAutomation.ps1 `
    -Action ExecuteApproved `
    -RequestId $requestId `
    -WhatIf

./scripts/Invoke-BudgetAutomation.ps1 `
    -Action ExecuteApproved `
    -RequestId $requestId
```

Batch execution requires the explicit `-AllApproved` switch:

```powershell
./scripts/Invoke-BudgetAutomation.ps1 -Action ExecuteApproved -AllApproved
```

Each invocation processes at most 100 approved requests. The executor acquires the request
with an optimistic concurrency token, re-reads the GitHub budget, compares the current
amount with `expectedCurrentAmount`, and sends an amount-only PATCH. Drift produces
`Conflict`; transient HTTP or timeout failures produce `Failed` and never appear as applied.

## 7. Schedule it

Schedule synchronization separately from execution. Example commands for a generic
PowerShell-capable scheduler are:

```text
pwsh -File scripts/Invoke-BudgetAutomation.ps1 -Action Sync -NoBuild
pwsh -File scripts/Invoke-BudgetAutomation.ps1 -Action ExecuteApproved -AllApproved -NoBuild
```

Inject configuration through the scheduler's secret mechanism. Do not place the SQL
connection string or GitHub private key in command arguments or source control.

For the Bicep deployment, use the independently scheduled `sync-budgets` and
`execute-approved` Container Apps Jobs. With the default `BUDGET_WRITES_ENABLED=false`, the
executor is manual and rejects execution. Enabling the value changes it to a scheduled job.
The environment switch, job trigger, GitHub App write permission, approval state, and drift
check are intentionally separate layers of control.

## 8. Verify and troubleshoot

Verify all three records after a change:

- The GitHub budget amount is the proposed amount.
- The request status is `Applied` and has an execution timestamp.
- An append-only `budget.change.applied` audit event exists.

| Symptom | Check |
| --- | --- |
| Missing environment variable | Run `Get-Help ./scripts/Invoke-BudgetAutomation.ps1 -Full`; set only process or workload secrets. |
| GitHub `401` | Check App ID, installation ID, private key formatting, and installation scope. |
| GitHub `403` | Inspect `X-Accepted-GitHub-Permissions`; restore read-only if the permission is uncertain. |
| Budget execution is disabled | Use the wrapper for an explicit execution, or set `BUDGET_WRITES_ENABLED=true` through an approved deployment change. |
| Request is not executable | Confirm it is `Approved`, unexpired, and has not already been claimed. |
| `github-budget-drift` | Synchronize again and create a new proposal from the authoritative amount. |
| Pending outbox grows in budget-only mode | Set `PUBLISH_BUDGET_LIFECYCLE_EVENTS=false` on both API and Worker processes. |

See [Permissions](permissions.md), [Operations](operations.md), and
[Modular automation](modular-automation.md) for the full safety and ownership model.