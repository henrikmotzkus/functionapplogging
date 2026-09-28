[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResourceGroupName,
    [Parameter(Mandatory)][string]$FunctionAppName,
    [ValidateSet("all", "success", "validation", "transient", "poison", "dependencyFailure")]
    [string]$Scenario = "all"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI is required but az was not found on PATH."
}

az account show --only-show-errors | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Azure CLI is not authenticated. Run az login first."
}

$subscriptionId = az account show `
    --query id `
    --output tsv `
    --only-show-errors

if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($subscriptionId)) {
    throw "Could not resolve the active Azure subscription."
}

$functionAppUrl = "https://management.azure.com/subscriptions/$subscriptionId" +
    "/resourceGroups/$ResourceGroupName/providers/Microsoft.Web/sites/$FunctionAppName" +
    "?api-version=2024-04-01"

$hostName = az rest `
    --method get `
    --url $functionAppUrl `
    --query properties.defaultHostName `
    --output tsv `
    --only-show-errors

if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($hostName)) {
    throw "Could not resolve the Function App hostname."
}

$functionKey = az functionapp keys list `
    --resource-group $ResourceGroupName `
    --name $FunctionAppName `
    --query "functionKeys.default" `
    --output tsv `
    --only-show-errors

if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($functionKey)) {
    throw "Could not retrieve the default Function key."
}

$scenarios = if ($Scenario -eq "all") {
    @("success", "validation", "transient", "poison", "dependencyFailure")
}
else {
    @($Scenario)
}

$uri = "https://$hostName/api/work-items"
$results = foreach ($currentScenario in $scenarios) {
    $correlationId = [guid]::NewGuid().ToString("D")
    $body = @{
        scenario      = $currentScenario
        correlationId = $correlationId
        payload       = if ($currentScenario -eq "validation") { "" } else { "customer-demo" }
    } | ConvertTo-Json

    try {
        $response = Invoke-RestMethod `
            -Method Post `
            -Uri $uri `
            -Headers @{ "x-functions-key" = $functionKey } `
            -ContentType "application/json" `
            -Body $body

        [pscustomobject]@{
            Scenario      = $currentScenario
            CorrelationId = $correlationId
            Outcome       = "Accepted"
            Response      = ($response | ConvertTo-Json -Compress -Depth 5)
        }
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($currentScenario -eq "validation" -and $statusCode -eq 400) {
            [pscustomobject]@{
                Scenario      = $currentScenario
                CorrelationId = $correlationId
                Outcome       = "Expected HTTP 400"
                Response      = $_.ErrorDetails.Message
            }
            continue
        }

        throw
    }
}

$results | Format-Table -AutoSize
Write-Host ""
Write-Host "Telemetry can take several minutes to appear. Use a CorrelationId above with Query-LoggingDemo.ps1."
