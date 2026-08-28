# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Because each customer deploys an isolated instance, "breaking" means any change that
requires operator action: a SQL migration that is not backward compatible, a removed or
renamed configuration key, a changed API contract, a changed workflow event payload, or a
new required Azure or Microsoft Entra permission.

## Unreleased

Pre-release development toward `0.1.0`. No versioned release has been published, and no
compatibility guarantees apply yet.

### Added

- Weighted and precedence classification with an explicit `Unknown` status for missing,
  suppressed, stale, or future-dated required data.
- Budget increase guardrails covering per-change amount and percentage, cumulative monthly
  increases, forecast headroom, stale data, duplicate windows, and cooldowns.
- Typed GitHub Enterprise clients for Copilot reports, cost centers, billing exports,
  budgets, and user states, with GitHub App RS256 authentication and installation-token
  caching.
- Azure SQL persistence with EF Core migrations, immutable snapshots, approvals, an
  append-only audit trail, a transactional outbox, and identity mappings.
- Report ingestion with signed-host allowlisting, bounded downloads, Blob archival, NDJSON
  normalization, and correction-window replacement.
- Explainable burn-rate forecasting, guarded budget proposals, drift-aware GitHub writes,
  and monthly baseline reconciliation.
- Microsoft Entra protected administration API and an operational dashboard with PKCE
  sign-in, evidence views, deterministic CSV exports, delivery status, and retention and
  reconciliation safety history.
- Standalone capability adoption: self-describing worker commands, `Invoke-BudgetAutomation.ps1`,
  `Invoke-NotificationAutomation.ps1`, and per-capability runbooks.
- Optional integration switches so a single capability can run without the full topology:
  `BUDGET_WRITES_ENABLED`, `PUBLISH_BUDGET_LIFECYCLE_EVENTS`, `NOTIFICATION_CHANNELS`, and an
  optional `DASHBOARD_URL`.
- Azure Commercial deployment through `azd` and Bicep with managed identities, private
  endpoints, backups, monitoring, OIDC deployment, and scheduled Container Apps Jobs.
- Azure Monitor OpenTelemetry for API requests, dependencies, exceptions, metrics, and
  logs, plus structured Worker lifecycle and financial-operation events.
- Production authentication integration tests and explicit approval/execution concurrency
  regression tests.
- Native GitHub Actions coverage artifacts and Docker base-image Dependabot updates.
- Claim-derived administrative audit identities, append-only retention/baseline setting
  events, and per-identity rate limiting for mutating API routes.
- Manual-only proposals in the built-in API and dashboard; automatic approval remains an
  explicit extension point for separately governed custom hosts.
- Deployment-owned API guardrails with fail-closed production defaults; proposal callers
  can no longer supply or widen their own financial thresholds.
- Minimal proposal input: the built-in API derives current budget, forecast, cumulative
  monthly increases, cooldown, freshness, fingerprints, and audit evidence from persisted
  authoritative snapshots.
- Idempotent proposal creation returns the existing request for an exact fingerprint retry,
  including concurrent unique-index races, without duplicate audit or outbox evidence.

### Security

- All GitHub Actions are pinned to immutable commit SHAs.
- The GitHub App private key is passed to Azure through a permission-restricted parameters
  file rather than a process command-line argument.
- Budget writes require a separate default-off application switch, and the Azure executor
  remains manual until that switch is enabled.
- Production API schema initialization defaults off and SQL command logs default to warning
  to preserve migration-role separation and reduce telemetry exposure.
- Production dashboard scripts comply with the deployed Content Security Policy; dynamic
  styles are explicitly scoped, and the customer enterprise name comes from persisted data.
