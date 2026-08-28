# Known limitations

- Azure Commercial and GitHub Enterprise Cloud only. GHES, Azure Government, China, and other sovereign profiles are not validated.
- One enterprise per deployment; no shared SaaS control plane.
- Metric reports can lag two to three days and depend on GitHub policy/client telemetry coverage.
- The curated v1 metric catalog has no arbitrary external plugin SDK.
- Budget and notification types are separated behind application interfaces, but their
  assemblies are not published as independent NuGet packages. Script-only adopters keep the
  shared Domain, Application, Infrastructure, and Worker projects plus the SQL schema.
- The built-in notification planner consumes Budget Manager classification snapshots. A
  different alert source requires a custom `IHealthNotificationStore` implementation.
- Manager routing is not enabled; routing uses configured recipients, mapped users, accountable owners, and enterprise admins.
- Teams and Outlook connectors require tenant-specific interactive authorization. Logic Apps is disabled until that setup is complete.
- Static Web Apps backend linking and tenant app registrations are post-deploy governance operations.
- SQL contained users/grants require an Entra SQL administrator with private network access.
- Active/passive multi-region failover is not provisioned; recovery uses geo backups and redeployment.
- The private-endpoint profile forces Premium Azure Container Registry and Service Bus tiers
  and can have a materially higher fixed cost than the public-endpoint profile. Pricing must
  be reviewed per customer, region, retention choice, and workload.
- API telemetry is exported to Application Insights. Worker commands currently emit
  structured logs to Log Analytics but not OpenTelemetry spans or custom metrics.
- Repository metrics cover documented PR activity and should not be interpreted as causal developer performance.