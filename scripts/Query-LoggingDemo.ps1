[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResourceGroupName,
    [Parameter(Mandatory)][string]$WorkspaceName,
    [Parameter(Mandatory)][string]$CorrelationId,
    [ValidateSet("timeline", "retries", "categories", "failures")]
    [string]$Query = "timeline",
    [ValidateRange(1, 24)][int]$Hours = 2
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$queryFiles = @{
    timeline   = "01-layered-timeline.kql"
    retries    = "02-retries-and-poison.kql"
    categories = "03-host-worker-application.kql"
    failures   = "04-dependencies-and-exceptions.kql"
}

$queryPath = Join-Path (Join-Path (Split-Path -Parent $PSScriptRoot) "queries") $queryFiles[$Query]
if (-not (Test-Path -LiteralPath $queryPath -PathType Leaf)) {
    throw "Query file was not found: $queryPath"
}

$workspaceId = az monitor log-analytics workspace show `
    --resource-group $ResourceGroupName `
    --workspace-name $WorkspaceName `
    --query customerId `
    --output tsv `
    --only-show-errors

if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($workspaceId)) {
    throw "Could not resolve the Log Analytics workspace customer ID."
}

$queryText = (Get-Content -LiteralPath $queryPath -Raw).Replace("{{CORRELATION_ID}}", $CorrelationId)
az monitor log-analytics query `
    --workspace $workspaceId `
    --analytics-query $queryText `
    --timespan "PT${Hours}H" `
    --output table `
    --only-show-errors

if ($LASTEXITCODE -ne 0) {
    throw "Log Analytics query failed with exit code $LASTEXITCODE."
}

