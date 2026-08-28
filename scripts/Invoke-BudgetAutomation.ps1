#Requires -Version 7.2

<#
.SYNOPSIS
Runs the reusable GitHub budget synchronization or approved-request executor.

.DESCRIPTION
Provides a small entry point around the BudgetManager.Worker budget commands. Sync is
read-only. ExecuteApproved only processes requests that the application already marked
Approved and re-reads GitHub before applying an amount-only PATCH. Selecting
ExecuteApproved explicitly enables the Worker's default-off write switch for this process
only; the previous environment setting is restored afterward.

Budget lifecycle events are disabled by default so a budget-only installation does not
need Service Bus. Pass -PublishLifecycleEvents when a configured outbox dispatcher and
workflow should receive requested, approved, applied, conflict, and failure events.

.PARAMETER Action
Sync reads GitHub budgets into SQL. ExecuteApproved applies one approved request or an
explicitly selected batch of up to 100 approved requests.

.PARAMETER RequestId
The single approved request to execute. Use this or -AllApproved, never both.

.PARAMETER AllApproved
Allows ExecuteApproved to process up to 100 approved requests. This switch is required
when RequestId is omitted so a broad write cannot happen accidentally.

.PARAMETER PublishLifecycleEvents
Creates budget lifecycle outbox records. Omit this for a budget-only installation.

.PARAMETER WorkerProject
Path to BudgetManager.Worker.csproj. Override this when the script is copied elsewhere.

.PARAMETER Configuration
The .NET build configuration. Defaults to Release.

.PARAMETER NoBuild
Runs an existing Worker build without compiling it first.

.EXAMPLE
./scripts/Invoke-BudgetAutomation.ps1 -Action Sync -WhatIf

Shows the read-only synchronization action without connecting to SQL or GitHub.

.EXAMPLE
./scripts/Invoke-BudgetAutomation.ps1 -Action Sync

Synchronizes current GitHub budget state after validating the required environment.

.EXAMPLE
./scripts/Invoke-BudgetAutomation.ps1 -Action ExecuteApproved -RequestId 01234567-89ab-cdef-0123-456789abcdef

Executes exactly one request that is already approved.

.EXAMPLE
./scripts/Invoke-BudgetAutomation.ps1 -Action ExecuteApproved -AllApproved -PublishLifecycleEvents

Executes up to 100 approved requests and creates workflow events for the configured
outbox dispatcher.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Sync', 'ExecuteApproved')]
    [string] $Action,

    [Guid] $RequestId = [Guid]::Empty,

    [switch] $AllApproved,

    [switch] $PublishLifecycleEvents,

    [ValidateNotNullOrEmpty()]
    [string] $WorkerProject = (Join-Path $PSScriptRoot '..\src\BudgetManager.Worker\BudgetManager.Worker.csproj'),

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-RequiredEnvironmentVariables {
    param([Parameter(Mandatory)][string[]] $Names)

    $missing = $Names | Where-Object {
        [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_, 'Process'))
    }
    if ($missing) {
        throw "Missing required environment variables: $($missing -join ', ')"
    }
}

function Get-DotNetHost {
    $fileName = if ($IsWindows) { 'dotnet.exe' } else { 'dotnet' }
    $candidates = @(
        $env:DOTNET_HOST_PATH,
        (Join-Path $HOME ".dotnet/$fileName")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return (Get-Command dotnet -ErrorAction Stop).Source
}

function Invoke-BudgetWorker {
    param([Parameter(Mandatory)][string[]] $WorkerArguments)

    $dotnet = Get-DotNetHost
    $dotnetArguments = @(
        'run',
        '--project', $resolvedWorkerProject,
        '--configuration', $Configuration
    )
    if ($NoBuild) {
        $dotnetArguments += '--no-build'
    }

    $dotnetArguments += '--'
    $dotnetArguments += $WorkerArguments
    Write-Verbose "dotnet $($dotnetArguments -join ' ')"
    & $dotnet @dotnetArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Budget worker exited with code $LASTEXITCODE."
    }
}

$hasRequestId = $RequestId -ne [Guid]::Empty
if ($Action -eq 'Sync' -and ($hasRequestId -or $AllApproved)) {
    throw 'RequestId and AllApproved apply only to ExecuteApproved.'
}

if ($Action -eq 'ExecuteApproved' -and ($hasRequestId -eq $AllApproved.IsPresent)) {
    throw 'ExecuteApproved requires exactly one of -RequestId or -AllApproved.'
}

if (-not (Test-Path -LiteralPath $WorkerProject -PathType Leaf)) {
    throw "Worker project was not found: $WorkerProject"
}

$resolvedWorkerProject = (Resolve-Path -LiteralPath $WorkerProject).Path
$workerArguments = if ($Action -eq 'Sync') {
    @('sync-budgets')
}
elseif ($hasRequestId) {
    @('execute-approved', $RequestId.ToString('D'))
}
else {
    @('execute-approved')
}
$target = if ($Action -eq 'Sync') {
    'GitHub budget snapshots'
}
elseif ($hasRequestId) {
    "approved budget request $RequestId"
}
else {
    'up to 100 approved budget requests'
}

if (-not $PSCmdlet.ShouldProcess($target, $Action)) {
    return
}

Assert-RequiredEnvironmentVariables @(
    'BUDGET_MANAGER_SQL_CONNECTION_STRING',
    'GITHUB_ENTERPRISE_ID',
    'GITHUB_ENTERPRISE_SLUG',
    'GITHUB_APP_ISSUER',
    'GITHUB_APP_INSTALLATION_ID',
    'GITHUB_APP_PRIVATE_KEY'
)

$enterpriseId = [Guid]::Empty
if (-not [Guid]::TryParse($env:GITHUB_ENTERPRISE_ID, [ref] $enterpriseId) -or $enterpriseId -eq [Guid]::Empty) {
    throw 'GITHUB_ENTERPRISE_ID must be a non-empty GUID.'
}

$installationId = 0L
if (-not [long]::TryParse($env:GITHUB_APP_INSTALLATION_ID, [ref] $installationId) -or $installationId -le 0) {
    throw 'GITHUB_APP_INSTALLATION_ID must be a positive integer.'
}

$eventVariable = 'PUBLISH_BUDGET_LIFECYCLE_EVENTS'
$writeVariable = 'BUDGET_WRITES_ENABLED'
$previousEventSetting = [Environment]::GetEnvironmentVariable($eventVariable, 'Process')
$previousWriteSetting = [Environment]::GetEnvironmentVariable($writeVariable, 'Process')
try {
    [Environment]::SetEnvironmentVariable(
        $eventVariable,
        $PublishLifecycleEvents.IsPresent.ToString().ToLowerInvariant(),
        'Process')
    [Environment]::SetEnvironmentVariable(
        $writeVariable,
        ($Action -eq 'ExecuteApproved').ToString().ToLowerInvariant(),
        'Process')
    Invoke-BudgetWorker $workerArguments
}
finally {
    [Environment]::SetEnvironmentVariable($eventVariable, $previousEventSetting, 'Process')
    [Environment]::SetEnvironmentVariable($writeVariable, $previousWriteSetting, 'Process')
}