# Contributing

## Before you begin

- Do not use real enterprise names, GitHub logins, email addresses, billing records, or tokens in issues, tests, fixtures, or pull requests. Use the fictional `contoso.com` identities already used throughout the samples, and reserved `.example` hostnames for URLs.
- Keep GitHub REST API behavior behind typed adapters and add a sanitized contract fixture for every new response shape.
- Preserve `Unknown` when data quality is insufficient. A new metric must document its source, unit, direction, supported scopes, aggregation, freshness, and limitations.
- Financial writes must remain idempotent, auditable, conflict-aware, and disabled unless explicitly configured.

## Development workflow

1. Create a focused branch from `main`.
2. Run `dotnet restore GitHubCopilotBudgetManager.slnx`.
3. Run `dotnet tool restore` to install the pinned `dotnet-ef` local tool from [.config/dotnet-tools.json](.config/dotnet-tools.json).
4. Run `dotnet format GitHubCopilotBudgetManager.slnx --verify-no-changes --no-restore`.
5. Run `dotnet build GitHubCopilotBudgetManager.slnx --configuration Release --no-restore`.
6. Run `dotnet test GitHubCopilotBudgetManager.slnx --configuration Release --no-build`.
7. If you changed the EF Core model, confirm the migrations are current:

   ```powershell
   dotnet tool run dotnet-ef migrations has-pending-model-changes --no-build `
     --project src/BudgetManager.Infrastructure/BudgetManager.Infrastructure.csproj `
     --startup-project src/BudgetManager.Infrastructure/BudgetManager.Infrastructure.csproj `
     --context BudgetManagerDbContext
   ```

8. Include tests that demonstrate both the requested behavior and its failure or unknown-data path.

Use clear commit messages and keep unrelated refactors out of feature pull requests.

## Changelog

Add an entry to [CHANGELOG.md](CHANGELOG.md) under `Unreleased` for any behavioral,
configuration, contract, or security change. Purely internal refactors do not need one.

## Continuous integration

Pull requests must pass formatting, build, tests, the EF migration model check, dashboard
and JSON validation, PowerShell parsing, Bicep compilation, and container builds. Every one
of those checks runs without secrets, network access, or a GitHub or Azure subscription:
GitHub API behavior is covered by sanitized response fixtures, and persistence tests use
in-memory SQLite.

CI retains Cobertura coverage reports as a 14-day workflow artifact. Coverage is evidence
for review, not a substitute for scenario assertions; no percentage threshold is enforced
until a stable baseline is established. API tests must preserve the production
authentication boundary, and financial-state tests must cover competing decisions or
execution claims when concurrency behavior changes.

CodeQL is optional. Code scanning is free on public repositories but requires GitHub
Advanced Security on private ones, so the job skips itself when GHAS is unavailable instead
of failing the run. Set the `ENABLE_CODEQL` repository variable to `true` to force it on
(including the weekly scheduled scan) or `false` to turn it off. Do not make CodeQL a
required status check unless you have enabled it.

GitHub Actions are pinned to commit SHAs; Dependabot proposes the updates, so do not
repin an action to a floating tag.

After the first push, protect `main`: require a pull request and at least one independent
review, dismiss stale approvals, block force pushes and deletion, and require the `.NET`,
`Dashboard and contracts`, `Azure IaC`, and `Containers` checks after their first successful
run establishes the exact check names. Do not require CodeQL unless it is enabled for that
repository. Enable private vulnerability reporting and secret scanning/push protection when
the repository plan provides them.

## Pull requests

Describe the behavioral change, security or privacy implications, validation performed, and any GitHub or Azure API assumptions. UI changes must include desktop and mobile evidence plus keyboard and accessibility validation.