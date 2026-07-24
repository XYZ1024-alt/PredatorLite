[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Baseline,
    [Parameter(Mandatory)]
    [string]$Candidate,
    [ValidateRange(0, 1000)]
    [double]$P50PercentThreshold = 10,
    [ValidateRange(0, 10000)]
    [double]$P50AbsoluteThresholdMs = 25,
    [ValidateRange(0, 1000)]
    [double]$P95PercentThreshold = 15,
    [ValidateRange(0, 10000)]
    [double]$P95AbsoluteThresholdMs = 40
)

$ErrorActionPreference = "Stop"
$baselinePath = [System.IO.Path]::GetFullPath($Baseline)
$candidatePath = [System.IO.Path]::GetFullPath($Candidate)
foreach ($path in @($baselinePath, $candidatePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Startup measurement was not found: $path"
    }
}

$baselineData = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
$candidateData = Get-Content -LiteralPath $candidatePath -Raw | ConvertFrom-Json
if ($baselineData.schemaVersion -ne 2 -or $candidateData.schemaVersion -ne 2) {
    throw "Unsupported startup measurement schema."
}
if ($baselineData.scope -ne $candidateData.scope -or
    $baselineData.milestone -ne $candidateData.milestone) {
    throw "Startup measurements must use the same scope and milestone."
}
if ($baselineData.machineName -ne $candidateData.machineName) {
    throw "Startup regression gates require measurements from the same fixed machine."
}
if ($baselineData.osVersion -ne $candidateData.osVersion -or
    $baselineData.appRuntimeVersion -ne $candidateData.appRuntimeVersion) {
    throw "Startup measurements must use the same Windows and .NET runtime versions."
}
if ($baselineData.iterations -ne $candidateData.iterations -or
    $baselineData.warmupIterations -ne $candidateData.warmupIterations) {
    throw "Startup measurements must use the same sample and warmup counts."
}

$baselineP50 = [double]$baselineData.summary.launchToMilestoneMs.p50
$baselineP95 = [double]$baselineData.summary.launchToMilestoneMs.p95
$candidateP50 = [double]$candidateData.summary.launchToMilestoneMs.p50
$candidateP95 = [double]$candidateData.summary.launchToMilestoneMs.p95
if ($baselineP50 -le 0 -or $baselineP95 -le 0) {
    throw "Baseline percentiles must be positive."
}

$p50Delta = $candidateP50 - $baselineP50
$p95Delta = $candidateP95 - $baselineP95
$p50Percent = 100 * $p50Delta / $baselineP50
$p95Percent = 100 * $p95Delta / $baselineP95
$p50Failed = $p50Delta -gt $P50AbsoluteThresholdMs -and $p50Percent -gt $P50PercentThreshold
$p95Failed = $p95Delta -gt $P95AbsoluteThresholdMs -and $p95Percent -gt $P95PercentThreshold

Write-Host ("p50: baseline={0:N2} ms candidate={1:N2} ms delta={2:+0.00;-0.00;0.00} ms ({3:+0.00;-0.00;0.00}%)" -f `
    $baselineP50, $candidateP50, $p50Delta, $p50Percent)
Write-Host ("p95: baseline={0:N2} ms candidate={1:N2} ms delta={2:+0.00;-0.00;0.00} ms ({3:+0.00;-0.00;0.00}%)" -f `
    $baselineP95, $candidateP95, $p95Delta, $p95Percent)

if ($p50Failed -or $p95Failed) {
    $failures = @()
    if ($p50Failed) {
        $failures += "p50 exceeded both $P50PercentThreshold% and $P50AbsoluteThresholdMs ms"
    }
    if ($p95Failed) {
        $failures += "p95 exceeded both $P95PercentThreshold% and $P95AbsoluteThresholdMs ms"
    }
    throw "Startup regression gate failed: $($failures -join '; ')."
}

Write-Host "Startup regression gate passed."
