<!-- Keep unrelated refactors out of feature pull requests. -->

## What changed

<!-- Describe the behavioral change, not the file list. -->

## Why

<!-- Link the issue, or state the problem this solves. -->

Closes #

## Validation

<!-- Replace with the commands you actually ran and their outcome. -->

- [ ] `dotnet restore GitHubCopilotBudgetManager.slnx`
- [ ] `dotnet tool restore`
- [ ] `dotnet format GitHubCopilotBudgetManager.slnx --verify-no-changes --no-restore`
- [ ] `dotnet build GitHubCopilotBudgetManager.slnx --configuration Release --no-restore`
- [ ] `dotnet test GitHubCopilotBudgetManager.slnx --configuration Release --no-build`
- [ ] Infrastructure changed: `az bicep build --file infra/main.bicep --stdout`
- [ ] Dashboard changed: syntax-check every file in `src/dashboard`, plus desktop and mobile evidence

## Safety and privacy

- [ ] No real enterprise names, GitHub logins, email addresses, billing records, or tokens
      appear in code, tests, fixtures, or this description.
- [ ] Financial writes remain idempotent, auditable, conflict-aware, and disabled unless
      explicitly configured.
- [ ] `Unknown` is preserved where data quality is insufficient.
- [ ] Individual user metrics are not exposed to shared channels.
- [ ] New GitHub or Azure API assumptions are documented below.

## API and contract impact

<!-- Note new/changed endpoints, event payloads, SQL migrations, or configuration keys.
     State "None" if nothing changed. A new response shape needs a sanitized fixture. -->

None

## Screenshots

<!-- Required for UI changes: desktop and mobile, plus keyboard and accessibility validation. -->
