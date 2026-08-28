# Automation scripts

These scripts are supported capability-level entry points over `BudgetManager.Worker`. They
are intentionally thin: financial guardrails, approval concurrency, recipient privacy,
deduplication, and transactional outbox behavior remain in the tested .NET services.

| Script | Actions | External write |
| --- | --- | --- |
| `Invoke-BudgetAutomation.ps1` | `Sync`, `ExecuteApproved` | Only `ExecuteApproved`; only for an already-approved request |
| `Invoke-NotificationAutomation.ps1` | `Plan`, `Dispatch`, `PlanAndDispatch`, `ClassifyPlanAndDispatch` | Dispatch publishes to Service Bus; the external flow sends email |
| `Invoke-BudgetBaselineReconciliation.ps1` | Starts the deployed reconciliation job | Depends on selected job mode; use `-DryRun` first |
| `Test-OnboardingConfiguration.ps1` | Validates full-deployment onboarding variables | None |
| `Test-Deployment.ps1` | Runs deployment smoke checks | None |

Inspect help without configuring cloud credentials:

```powershell
Get-Help ./scripts/Invoke-BudgetAutomation.ps1 -Full
Get-Help ./scripts/Invoke-NotificationAutomation.ps1 -Full
./scripts/Invoke-BudgetAutomation.ps1 -Action Sync -WhatIf
./scripts/Invoke-NotificationAutomation.ps1 -Action PlanAndDispatch -WhatIf
```

The scripts validate only variables required by their selected action. They never accept or
print secrets as command arguments. Use process-scoped environment variables for local
testing and the workload or scheduler's secret integration in production.

The Worker rejects budget execution unless `BUDGET_WRITES_ENABLED=true`. The budget wrapper
sets that value only for an explicit `ExecuteApproved` invocation, after `ShouldProcess` and
single-versus-batch validation, then restores the caller's previous environment value.

Each script resolves .NET in this order: `DOTNET_HOST_PATH`, the user-local `~/.dotnet`
installation, then `dotnet` on `PATH`. This supports workstations where the repository SDK
is installed per user while an older machine-wide SDK remains on `PATH`.

Read [How to automate budget increases](../docs/how-to-automate-budget-increases.md) or
[How to send notification email](../docs/how-to-send-notification-emails.md) before running
a write or enabling a schedule. The component and extraction boundaries are in
[Modular automation](../docs/modular-automation.md).