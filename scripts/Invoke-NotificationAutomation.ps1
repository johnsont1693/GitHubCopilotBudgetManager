#Requires -Version 7.2

<#
.SYNOPSIS
Plans and dispatches reusable Copilot health notification events.

.DESCRIPTION
Provides a small entry point around the BudgetManager.Worker notification pipeline.
Planning writes deduplicated notification and transactional outbox records. Dispatching
publishes pending outbox records to Service Bus. An authorized Logic Apps or Power
Automate workflow performs the final Outlook delivery.

The pipeline can run without the dashboard, GitHub budget write permission, or Microsoft
Graph. Graph-backed identity mappings are optional when owner or admin recipients exist.

.PARAMETER Action
Plan creates notification records from existing classification signals. Dispatch publishes
pending outbox records. PlanAndDispatch performs both. ClassifyPlanAndDispatch first
recomputes classifications from existing policies and metrics.

.PARAMETER WorkerProject
Path to BudgetManager.Worker.csproj. Override this when the script is copied elsewhere.

.PARAMETER Configuration
The .NET build configuration. Defaults to Release.

.PARAMETER NoBuild
Runs an existing Worker build without compiling it first.

.EXAMPLE
./scripts/Invoke-NotificationAutomation.ps1 -Action PlanAndDispatch -WhatIf

Shows the two-stage operation without connecting to SQL or Service Bus.

.EXAMPLE
./scripts/Invoke-NotificationAutomation.ps1 -Action Plan

Creates deduplicated notification and outbox records from existing classifications.

.EXAMPLE
./scripts/Invoke-NotificationAutomation.ps1 -Action ClassifyPlanAndDispatch

Recomputes classifications, plans notifications, and dispatches the outbox. The external
workflow must already be authorized for Outlook delivery.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Plan', 'Dispatch', 'PlanAndDispatch', 'ClassifyPlanAndDispatch')]
    [string] $Action,

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

function Invoke-NotificationWorker {
    param(
        [Parameter(Mandatory)][string] $Command,
        [Parameter(Mandatory)][bool] $SkipBuild
    )

    $dotnet = Get-DotNetHost
    $dotnetArguments = @(
        'run',
        '--project', $resolvedWorkerProject,
        '--configuration', $Configuration
    )
    if ($SkipBuild) {
        $dotnetArguments += '--no-build'
    }

    $dotnetArguments += @('--', $Command)
    Write-Verbose "dotnet $($dotnetArguments -join ' ')"
    & $dotnet @dotnetArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Notification worker command '$Command' exited with code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $WorkerProject -PathType Leaf)) {
    throw "Worker project was not found: $WorkerProject"
}

$resolvedWorkerProject = (Resolve-Path -LiteralPath $WorkerProject).Path
$commands = switch ($Action) {
    'Plan' { @('plan-notifications') }
    'Dispatch' { @('dispatch-outbox') }
    'PlanAndDispatch' { @('plan-notifications', 'dispatch-outbox') }
    'ClassifyPlanAndDispatch' { @('classify', 'plan-notifications', 'dispatch-outbox') }
}

if (-not $PSCmdlet.ShouldProcess(($commands -join ', '), 'Run notification pipeline')) {
    return
}

$required = @('BUDGET_MANAGER_SQL_CONNECTION_STRING')
if ($commands -contains 'classify' -or $commands -contains 'plan-notifications') {
    $required += 'GITHUB_ENTERPRISE_ID'
}

if ($commands -contains 'dispatch-outbox') {
    $required += @('SERVICE_BUS_NAMESPACE', 'SERVICE_BUS_QUEUE_NAME')
}

Assert-RequiredEnvironmentVariables ($required | Select-Object -Unique)
if ($required -contains 'GITHUB_ENTERPRISE_ID') {
    $enterpriseId = [Guid]::Empty
    if (-not [Guid]::TryParse($env:GITHUB_ENTERPRISE_ID, [ref] $enterpriseId) -or $enterpriseId -eq [Guid]::Empty) {
        throw 'GITHUB_ENTERPRISE_ID must be a non-empty GUID.'
    }
}

for ($index = 0; $index -lt $commands.Count; $index++) {
    Invoke-NotificationWorker $commands[$index] ($NoBuild.IsPresent -or $index -gt 0)
}