# Support

This project is a community-supported reference implementation, not an official GitHub
product or a service with an SLA. It is customer-deployed, so each installation is owned
and operated by the organization that deploys it. Maintainers cannot access your Azure
subscription, GitHub enterprise, SQL database, or telemetry.

## Before opening an issue

1. Read [Known limitations](docs/known-limitations.md). Several common questions are
   unsupported scenarios by design, including GHES, sovereign clouds, and multi-enterprise
   deployments.
2. Check [Operations and recovery](docs/operations.md) for scheduled job, alerting, and
   recovery behavior.
3. Reproduce the problem with the smallest possible surface. The Worker prints a full
   command reference without any cloud credentials:

   ```powershell
   dotnet run --project src/BudgetManager.Worker -- --help
   ```

4. Confirm the failure is not a configuration gap by running the onboarding and deployment
   checks in [scripts/README.md](scripts/README.md).

## Where to ask

| Topic | Channel |
| --- | --- |
| Suspected security vulnerability | **Security** tab → *Report a vulnerability*. Never a public issue. See [SECURITY.md](SECURITY.md). |
| Defect or unexpected behavior | GitHub Issues → *Bug report* |
| New metric, capability, or integration | GitHub Issues → *Feature request* |
| Deployment and onboarding questions | GitHub Discussions if enabled, otherwise a *Bug report* labeled `question` |
| Conduct concerns | See [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) |

## Never include real data

This project handles enterprise billing and per-user Copilot telemetry. Do not paste real
enterprise names, GitHub logins, email addresses, principal names, billing records, budget
amounts, tokens, private keys, or connection strings into an issue, discussion, or pull
request. Redact them, or use the fictional `contoso.com` identities described in
[CONTRIBUTING.md](CONTRIBUTING.md).

## Response expectations

Issues are triaged on a best-effort basis. There is no service level agreement and no
commercial support contract. Security reports follow the acknowledgement timeline in
[SECURITY.md](SECURITY.md).
