[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^https://')]
    [string] $ApiUrl,

    [Parameter(Mandatory)]
    [ValidatePattern('^https://')]
    [string] $DashboardUrl,

    [string] $AccessToken
)

$ErrorActionPreference = 'Stop'
$headers = if ($AccessToken) { @{ Authorization = "Bearer $AccessToken" } } else { @{} }

$health = Invoke-RestMethod -Uri "$ApiUrl/healthz"
$ready = Invoke-RestMethod -Uri "$ApiUrl/readyz"
$dashboard = Invoke-WebRequest -Uri $DashboardUrl -UseBasicParsing

if ($health.status -ne 'healthy') { throw 'API liveness check failed.' }
if ($ready.status -ne 'ready') { throw 'API readiness check failed.' }
if ($dashboard.StatusCode -ne 200 -or -not $dashboard.Content.Contains('Copilot Budget Manager')) {
    throw 'Dashboard content check failed.'
}

if ($AccessToken) {
    Invoke-RestMethod -Uri "$ApiUrl/api/v1" -Headers $headers | Out-Null
}

[pscustomobject]@{
    ApiLiveness = $health.status
    ApiReadiness = $ready.status
    DashboardStatus = $dashboard.StatusCode
    AuthenticatedProbe = [bool] $AccessToken
}