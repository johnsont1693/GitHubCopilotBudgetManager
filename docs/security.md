# Security and threat model

The primary assets are GitHub App credentials, Entra identities/roles, individual usage data, budget values, approvals, audit evidence, and outbound notification content.

Primary threats and controls:

- **Credential theft:** private key in Key Vault, managed identity, no secrets in source or workflow state, short-lived GitHub installation tokens, and secret scanning when the hosting plan provides it.
- **Privilege escalation:** JWT issuer/audience/lifetime validation, explicit app roles, admin-only individual data and financial commands, API authorization independent of dashboard routing, and audit actors derived from validated `oid`/`sub` claims rather than request bodies.
- **Forged/replayed approval:** request concurrency token, one decision row, nonce hash, expiration, status transition checks, and unique database constraints.
- **Concurrent/manual budget drift:** re-fetch GitHub immediately before `PATCH`; mismatch becomes Conflict and never overwrites the administrator's change.
- **Duplicate events:** SQL outbox unique fingerprints, Service Bus message IDs/duplicate detection/sessions, idempotent command state.
- **Signed URL abuse/SSRF:** HTTPS-only signed links, configurable host suffix allowlist, continuation-host validation, bounded downloads.
- **Data exfiltration:** private endpoints for data services, HTTPS/TLS, role-scoped APIs, no individual metrics in shared channel templates, CSP and no third-party dashboard scripts.
- **Injection:** structured JSON/parsers, EF parameterization, escaped dashboard rendering, no spreadsheet formulas emitted by current exports.
- **Audit tampering:** append-only application events, restricted SQL identity, Azure diagnostics, backup retention, correlation IDs.
- **Schema ownership:** API schema initialization defaults off outside the explicit Development override; production migrations run under the separate migration identity.
- **Unsafe automation:** read-only onboarding, a default-off application write switch, deployment-owned financial caps that callers cannot widen, freshness, cooldown, fingerprint, health gate, admin approval by default, worker-only writes, and no automatic decreases.
- **Runaway administrative clients:** per-identity fixed-window limiting on policy, proposal, decision, retention, baseline, and preview mutations; rejected bursts return HTTP `429`.

Before release, enable GitHub private vulnerability reporting and branch protection. This project provides controls and evidence but does not claim SOC 2, ISO, GDPR, or other certification.