[CmdletBinding()]
param(
    [string]$ProjectPath = "src\LoggingDemo.Functions\LoggingDemo.Functions.csproj",
    [string]$TestProjectPath = "tests\LoggingDemo.Tests\LoggingDemo.Tests.csproj",
    [string]$OutputPath = "artifacts\LoggingDemo.Functions.zip"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$workspaceRoot = Split-Path -Parent $PSScriptRoot

function Resolve-WorkspacePath {
    param([Parameter(Mandatory)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot $Path))
}

$resolvedProjectPath = Resolve-WorkspacePath $ProjectPath
$resolvedTestProjectPath = Resolve-WorkspacePath $TestProjectPath
$resolvedOutputPath = Resolve-WorkspacePath $OutputPath
$publishPath = Join-Path (Split-Path -Parent $resolvedOutputPath) "publish"

foreach ($requiredPath in @($resolvedProjectPath, $resolvedTestProjectPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required project file was not found: $requiredPath"
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK is required but dotnet was not found on PATH."
}

$artifactDirectory = Split-Path -Parent $resolvedOutputPath
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null

if (Test-Path -LiteralPath $publishPath) {
    Remove-Item -LiteralPath $publishPath -Recurse -Force
}

if (Test-Path -LiteralPath $resolvedOutputPath) {
    Remove-Item -LiteralPath $resolvedOutputPath -Force
}

dotnet restore $resolvedTestProjectPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

dotnet test $resolvedTestProjectPath --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet test failed with exit code $LASTEXITCODE."
}

dotnet publish $resolvedProjectPath --configuration Release --no-restore --output $publishPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

foreach ($requiredArtifact in @("host.json", "worker.config.json", "functions.metadata")) {
    $artifactPath = Join-Path $publishPath $requiredArtifact
    if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
        throw "Published Function package is missing $requiredArtifact."
    }
}

Compress-Archive -Path (Join-Path $publishPath "*") -DestinationPath $resolvedOutputPath -CompressionLevel Optimal
Write-Output $resolvedOutputPath
