[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$required = @(
    'AZURE_SUBSCRIPTION_ID',
    'AZURE_TENANT_ID',
    'GITHUB_ENTERPRISE_ID',
    'GITHUB_ENTERPRISE_SLUG',
    'GITHUB_APP_ISSUER',
    'GITHUB_APP_INSTALLATION_ID',
    'API_AUDIENCE',
    'DASHBOARD_CLIENT_ID',
    'DASHBOARD_API_SCOPE',
    'GRAPH_GITHUB_LOGIN_PROPERTY',
    'RAW_REPORT_RETENTION_DAYS'
)

$missing = $required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) }
if ($missing) {
    throw "Missing required environment variables: $($missing -join ', ')"
}

if (-not [Guid]::TryParse($env:GITHUB_ENTERPRISE_ID, [ref]([Guid]::Empty))) {
    throw 'GITHUB_ENTERPRISE_ID must be a GUID.'
}

if ([int]$env:RAW_REPORT_RETENTION_DAYS -lt 7) {
    throw 'RAW_REPORT_RETENTION_DAYS must be at least 7.'
}

Write-Output 'Onboarding environment configuration is structurally valid.'