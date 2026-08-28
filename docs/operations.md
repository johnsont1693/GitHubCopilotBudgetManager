# Operations and recovery

## Scheduled jobs

Default UTC schedules are documented in [infra/README.md](../infra/README.md). Run migration manually before enabling schedules. Observe every Container Apps Job execution and Service Bus dead-letter queue.

## Health and alerts

- `/healthz`: process liveness.
- `/readyz`: database connectivity.
- Alert on missed/stale ingestions, repeated GitHub 403/429/5xx, identity coverage, failed classifications/forecasts, pending/expired approvals, conflicts, outbox failures, Service Bus dead letters, retention failures, and connector authorization.

The API exports OpenTelemetry request, dependency, exception, metric, and log signals to
Application Insights when `APPLICATIONINSIGHTS_CONNECTION_STRING` is configured. Worker
commands emit structured events to Container Apps console logs and Log Analytics. Event IDs
1001–1003 cover budget drift, apply, and failure; 2001 covers outbox publication failure;
3001–3008 cover Worker lifecycle, ingestion, synchronization, outcomes, and command failure.
Alert on failed Container Apps Job executions rather than assuming that a completed process
was successful: the Worker now exits nonzero when any budget write or outbox publication in
its batch fails.

## Safety previews and evidence

Enterprise administrators can run non-mutating checks from the Operations dashboard or API:

- `POST /api/v1/operations/retention-preview?enterpriseId={id}` returns deletion-candidate counts and always invokes retention in dry-run mode.
- `POST /api/v1/budget-baselines/reconciliation-preview?enterpriseId={id}` evaluates every configured baseline without applying a reset or creating an approval request.
- `GET /api/v1/operations/safety-history?enterpriseId={id}` returns the persisted retention and reconciliation evidence in reverse chronological order.

Every retention evaluation records its effective durations, candidate counts, dry-run state, and legal-hold state as `retention.previewed`. Baseline previews record every per-budget action and reason as `budget.baseline.reconciliation.previewed`. Review the returned counts and the durable history before enabling scheduled apply jobs. Legal hold blocks retention deletion even when a non-dry-run worker command is issued.

## Recovery targets

Target RPO is 15 minutes and RTO is four hours for the documented single-region profile. Azure SQL PITR/geo restore and Blob versioning/soft delete are the state recovery mechanisms. API and workers are stateless images and can be redeployed from release artifacts. Outbox rows can be replayed after restore; GitHub reports can be re-requested within their documented history window.

Quarterly staging drill:

1. Restore SQL to a new database.
2. Point a non-production deployment at the restored database.
3. Verify migrations, audit continuity, Blob access, and Key Vault references.
4. Replay pending outbox rows into a non-production queue.
5. Re-ingest a finalized report day and verify idempotency.
6. Run API/dashboard smoke tests with [Test-Deployment.ps1](../scripts/Test-Deployment.ps1).
7. Record measured RPO/RTO and remediation.

Never test GitHub write recovery against a production budget without an approved change window.