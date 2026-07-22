[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "publish\win-x64"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "src\PredatorLite.App\PredatorLite.App.csproj"
$destination = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))

dotnet publish $project `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained false `
    --output $destination `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "PredatorLite published to $destination"
