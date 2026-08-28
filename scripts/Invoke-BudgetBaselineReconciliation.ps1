[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ResourceGroup,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $JobName,

    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'

$argsOverride = @('reconcile-baselines')
if ($DryRun) {
    $argsOverride += '--dry-run'
}

if ($PSCmdlet.ShouldProcess("$ResourceGroup/$JobName", "Start baseline reconciliation")) {
    az containerapp job start `
        --resource-group $ResourceGroup `
        --name $JobName `
        --args $argsOverride `
        --output none

    az containerapp job execution list `
        --resource-group $ResourceGroup `
        --name $JobName `
        --output table
}