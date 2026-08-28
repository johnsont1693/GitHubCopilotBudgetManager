# Permissions

## GitHub App

Install the App on the enterprise. Start read-only.

- View Enterprise Copilot Metrics.
- Read enterprise billing budgets, usage report exports, and cost centers.
- Write enterprise billing budgets only after capability checks, staging validation, guardrail review, explicit administrator approval, and setting `BUDGET_WRITES_ENABLED=true` on the executor.

Use installation tokens. The private key belongs in Key Vault. Inspect `X-Accepted-GitHub-Permissions` because billing permission names can evolve.

## Microsoft Entra

API app roles:

- `EnterpriseAdmin`: individual data, policies, mappings, retention, baselines, approvals, and automation controls.
- `Operator`: aggregate operational data and policy simulations.
- `Auditor`: aggregate financial and audit reads.

User-level classifications and identity mappings are denied unless the caller has `EnterpriseAdmin`.

Dashboard SPA: delegated API scope through authorization code with PKCE. Register only approved HTTPS redirect URIs.

Worker managed identity: Microsoft Graph application permission `User.Read.All` for the selected login attribute, department, and `employeeOrgData.costCenter`.

## Azure

Managed identities authenticate to ACR, Azure SQL, Blob Storage, Service Bus, Key Vault, Graph, and Application Insights. The runtime SQL identity gets data read/write only; the separate migration identity gets schema permissions. Local/key authentication is disabled where supported.