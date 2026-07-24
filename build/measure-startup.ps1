[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Executable,
    [ValidateSet("Tray", "Critical", "Deferred")]
    [string]$Scope = "Tray",
    [ValidateRange(1, 1000)]
    [int]$Iterations = 15,
    [ValidateRange(0, 100)]
    [int]$WarmupIterations = 2,
    [ValidateRange(1, 300)]
    [int]$TimeoutSeconds = 30,
    [string]$OutputPath,
    [switch]$AllowHardwareInitialization
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$executablePath = [System.IO.Path]::GetFullPath($Executable)
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "PredatorLite executable was not found: $executablePath"
}
if ($Scope -ne "Tray" -and -not $AllowHardwareInitialization) {
    throw "Critical and Deferred measurements run hardware initialization. Pass -AllowHardwareInitialization only on the validated PHN16-71 / BIOS V1.20 test machine."
}

$targetMilestone = switch ($Scope) {
    "Tray" { "tray-ready" }
    "Critical" { "critical-ready" }
    "Deferred" { "deferred-ready" }
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $leaf = [System.IO.Path]::GetFileNameWithoutExtension($executablePath)
    $OutputPath = Join-Path $repositoryRoot "artifacts\performance\startup-$leaf-$($Scope.ToLowerInvariant()).json"
}
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $outputFullPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

function Get-Percentile {
    param(
        [Parameter(Mandatory)]
        [double[]]$Values,
        [Parameter(Mandatory)]
        [double]$Percentile
    )

    $sorted = @($Values | Sort-Object)
    $index = [Math]::Max(0, [Math]::Ceiling(($Percentile / 100) * $sorted.Count) - 1)
    return [double]$sorted[$index]
}

function Assert-NoRunningInstance {
    $running = @(Get-Process -Name "PredatorLite" -ErrorAction SilentlyContinue)
    if ($running.Count -ne 0) {
        throw "Close every running PredatorLite instance before measuring startup."
    }
}

function Invoke-StartupSample {
    param(
        [Parameter(Mandatory)]
        [int]$Index,
        [Parameter(Mandatory)]
        [bool]$IsWarmup
    )

    if ($Scope -ne "Tray") {
        Assert-NoRunningInstance
    }
    $pipeName = "PredatorLite.Startup.$([guid]::NewGuid().ToString('N'))"
    $pipe = [System.IO.Pipes.NamedPipeServerStream]::new(
        $pipeName,
        [System.IO.Pipes.PipeDirection]::In,
        1,
        [System.IO.Pipes.PipeTransmissionMode]::Byte,
        [System.IO.Pipes.PipeOptions]::Asynchronous)
    $reader = $null
    $process = $null
    try {
        $connectionTask = $pipe.WaitForConnectionAsync()
        $startTimestamp = [System.Diagnostics.Stopwatch]::GetTimestamp()
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $executablePath
        $startInfo.WorkingDirectory = Split-Path -Parent $executablePath
        $startInfo.UseShellExecute = $false
        $startInfo.ArgumentList.Add("--background")
        $startInfo.ArgumentList.Add("--startup-pipe=$pipeName")
        if ($Scope -eq "Tray") {
            $startInfo.ArgumentList.Add("--startup-tray-only")
        }

        $process = [System.Diagnostics.Process]::Start($startInfo)
        if ($null -eq $process) {
            throw "PredatorLite did not start."
        }

        $timeoutMilliseconds = $TimeoutSeconds * 1000
        if (-not $connectionTask.Wait($timeoutMilliseconds)) {
            throw "Startup telemetry pipe did not connect within $TimeoutSeconds seconds."
        }

        $reader = [System.IO.StreamReader]::new($pipe)
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        $processStartTimestamp = $null
        $targetTimestamp = $null
        $frequency = $null
        $appRuntimeVersion = $null
        do {
            $remaining = [int][Math]::Max(1, ($deadline - [DateTime]::UtcNow).TotalMilliseconds)
            $readTask = $reader.ReadLineAsync()
            if (-not $readTask.Wait($remaining)) {
                throw "Startup milestone '$targetMilestone' did not arrive within $TimeoutSeconds seconds."
            }

            $line = $readTask.Result
            if ($null -eq $line) {
                throw "Startup telemetry pipe closed before '$targetMilestone'."
            }

            $parts = $line.Split("`t")
            if ($parts.Count -ne 5) {
                throw "Startup telemetry returned an invalid message: $line"
            }

            $name = $parts[0]
            Write-Verbose "Startup milestone: $name"
            $timestamp = [long]::Parse($parts[1], [Globalization.CultureInfo]::InvariantCulture)
            $messageFrequency = [long]::Parse($parts[2], [Globalization.CultureInfo]::InvariantCulture)
            if ($null -eq $frequency) {
                $frequency = $messageFrequency
            }
            elseif ($frequency -ne $messageFrequency) {
                throw "Startup telemetry frequency changed during a sample."
            }

            $messageRuntimeVersion = $parts[4]
            if ($null -eq $appRuntimeVersion) {
                $appRuntimeVersion = $messageRuntimeVersion
            }
            elseif ($appRuntimeVersion -ne $messageRuntimeVersion) {
                throw "App runtime version changed during a sample."
            }

            if ($name -eq "process-start") {
                $processStartTimestamp = $timestamp
            }
            if ($name -eq $targetMilestone) {
                $targetTimestamp = $timestamp
            }
        } while ($null -eq $targetTimestamp)

        if ($null -eq $processStartTimestamp) {
            throw "Startup telemetry did not report process-start."
        }

        $launchMilliseconds = 1000.0 * ($targetTimestamp - $startTimestamp) / $frequency
        $entryMilliseconds = 1000.0 * ($targetTimestamp - $processStartTimestamp) / $frequency
        $kind = if ($IsWarmup) { "warmup" } else { "sample" }
        Write-Host ("{0} {1}: launch={2:N2} ms, entry={3:N2} ms" -f $kind, $Index, $launchMilliseconds, $entryMilliseconds)
        return [pscustomobject]@{
            iteration = $Index
            launchToMilestoneMs = [Math]::Round($launchMilliseconds, 3)
            entryToMilestoneMs = [Math]::Round($entryMilliseconds, 3)
            appRuntimeVersion = $appRuntimeVersion
        }
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
        else {
            $pipe.Dispose()
        }
        if ($null -ne $process) {
            try {
                if (-not $process.HasExited) {
                    $process.Kill($true)
                }
                $process.WaitForExit(5000) | Out-Null
            }
            finally {
                $process.Dispose()
            }
        }
        Start-Sleep -Milliseconds 250
    }
}

if ($Scope -ne "Tray") {
    Assert-NoRunningInstance
}
for ($index = 1; $index -le $WarmupIterations; $index++) {
    Invoke-StartupSample -Index $index -IsWarmup $true | Out-Null
}

$samples = @()
for ($index = 1; $index -le $Iterations; $index++) {
    $samples += Invoke-StartupSample -Index $index -IsWarmup $false
}

$launchValues = [double[]]@($samples | ForEach-Object { $_.launchToMilestoneMs })
$entryValues = [double[]]@($samples | ForEach-Object { $_.entryToMilestoneMs })
$hash = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash.ToLowerInvariant()
$managedAssemblyPath = [System.IO.Path]::ChangeExtension($executablePath, ".dll")
$managedAssemblyHash = if (Test-Path -LiteralPath $managedAssemblyPath -PathType Leaf) {
    (Get-FileHash -LiteralPath $managedAssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
}
else {
    $null
}
$result = [ordered]@{
    schemaVersion = 2
    capturedUtc = [DateTimeOffset]::UtcNow.ToString("O")
    scope = $Scope
    milestone = $targetMilestone
    executable = $executablePath
    executableSha256 = $hash
    managedAssemblySha256 = $managedAssemblyHash
    layoutBytes = (Get-ChildItem -LiteralPath (Split-Path -Parent $executablePath) -Recurse -File |
        Measure-Object -Property Length -Sum).Sum
    osVersion = [Environment]::OSVersion.VersionString
    appRuntimeVersion = [string]$samples[0].appRuntimeVersion
    machineName = [Environment]::MachineName
    iterations = $Iterations
    warmupIterations = $WarmupIterations
    summary = [ordered]@{
        launchToMilestoneMs = [ordered]@{
            min = [Math]::Round(($launchValues | Measure-Object -Minimum).Minimum, 3)
            mean = [Math]::Round(($launchValues | Measure-Object -Average).Average, 3)
            p50 = [Math]::Round((Get-Percentile -Values $launchValues -Percentile 50), 3)
            p95 = [Math]::Round((Get-Percentile -Values $launchValues -Percentile 95), 3)
            max = [Math]::Round(($launchValues | Measure-Object -Maximum).Maximum, 3)
        }
        entryToMilestoneMs = [ordered]@{
            min = [Math]::Round(($entryValues | Measure-Object -Minimum).Minimum, 3)
            mean = [Math]::Round(($entryValues | Measure-Object -Average).Average, 3)
            p50 = [Math]::Round((Get-Percentile -Values $entryValues -Percentile 50), 3)
            p95 = [Math]::Round((Get-Percentile -Values $entryValues -Percentile 95), 3)
            max = [Math]::Round(($entryValues | Measure-Object -Maximum).Maximum, 3)
        }
    }
    samples = $samples
}

[System.IO.File]::WriteAllText(
    $outputFullPath,
    ($result | ConvertTo-Json -Depth 8) + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))
Write-Host "Startup measurements written to $outputFullPath"
