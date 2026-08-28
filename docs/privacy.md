# Privacy and retention

The deployment is customer-owned and single-enterprise. It stores GitHub login, optional Entra object ID/UPN, department, cost-center mapping, per-user usage observations, classification evidence, delivery records, and financial/audit state.

Retention is a required customer decision. Separate durations govern raw reports, user metrics, aggregate metrics/classifications, notifications, and audit events. Legal hold prevents deletion. `apply-retention --dry-run` and the admin-only Operations preview report candidate counts and persist the evaluated configuration before any deletion.

Recommended operating practice:

- Keep raw signed reports only long enough to reprocess and investigate schema changes.
- Keep individual observations shorter than aggregates.
- Restrict individual data to `EnterpriseAdmin` and private direct notifications.
- Route user-level notifications only to Outlook recipients; persisted user delivery rows never target Teams channels.
- Use aggregate views for operators and auditors.
- Treat GitHub billing export links as ephemeral secrets. The application persists report type, period, status, actor, file count, and observation time, but not signed download URLs.
- Restrict identity-mapping exports and user-level classification evidence to `EnterpriseAdmin`. Audit and classification CSVs are formula-safe and SHA-256 hashed for integrity verification.
- Treat telemetry as customer data. API OpenTelemetry includes request/dependency metadata;
	Worker logs include command, request, budget, outcome, and aggregate-count fields. The
	application does not log tokens, connection strings, notification payloads, recipients,
	or user-level metrics. Restrict Log Analytics/Application Insights access and set their
	retention to the customer's approved period.
- Document the customer's lawful basis, employee notice, works-council obligations, data-subject export/deletion procedure, and exceptions for immutable financial/audit evidence.
- Remove or pseudonymize mappings when an account leaves, subject to required audit retention.

Azure SQL point-in-time/LTR backups age out independently of live-table retention. Follow Microsoft guidance for privacy obligations in backups and document the customer's restore-and-redelete process.