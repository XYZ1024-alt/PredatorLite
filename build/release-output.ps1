function Get-PredatorLiteOutputLockPath {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    return Join-Path $RepositoryRoot "obj\predatorlite-release-output.lock"
}

function Enter-PredatorLiteOutputLock {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,
        [int]$TimeoutSeconds = 5
    )

    $lockPath = Get-PredatorLiteOutputLockPath -RepositoryRoot $RepositoryRoot
    $lockDirectory = Split-Path -Parent $lockPath
    if (Test-Path -LiteralPath $lockDirectory) {
        $lockDirectoryItem = Get-Item -LiteralPath $lockDirectory -Force
        if (-not $lockDirectoryItem.PSIsContainer -or
            ($lockDirectoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Release-output lock directory is not a normal repository directory: $lockDirectory"
        }
    }
    else {
        New-Item -ItemType Directory -Path $lockDirectory -Force | Out-Null
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try {
            return [System.IO.File]::Open(
                $lockPath,
                [System.IO.FileMode]::OpenOrCreate,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None)
        }
        catch [System.IO.IOException] {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "Another PredatorLite publish or installer build is already running."
            }
            Start-Sleep -Milliseconds 200
        }
    } while ($true)
}

function Assert-PredatorLiteOutputLock {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory)]
        [System.IO.FileStream]$OutputLock
    )

    $expectedPath = [System.IO.Path]::GetFullPath(
        (Get-PredatorLiteOutputLockPath -RepositoryRoot $RepositoryRoot))
    if ($OutputLock.SafeFileHandle.IsClosed -or
        -not $OutputLock.CanWrite -or
        -not $OutputLock.Name.Equals($expectedPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The supplied output lock is not the active PredatorLite release-output lock."
    }
}

function Assert-SafeRepositoryOutputPath {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory)]
        [string]$Destination,
        [Parameter(Mandatory)]
        [string[]]$AllowedRelativeRoots
    )

    $trimChars = [char[]]@('\', '/')
    $repositoryPath = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd($trimChars)
    $repositoryPrefix = $repositoryPath + [System.IO.Path]::DirectorySeparatorChar
    $destinationPath = [System.IO.Path]::GetFullPath($Destination)
    if (-not $destinationPath.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Output path must remain inside the repository: $destinationPath"
    }

    $allowed = $false
    foreach ($relativeRoot in $AllowedRelativeRoots) {
        $allowedRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryPath $relativeRoot)).TrimEnd($trimChars)
        $allowedPrefix = $allowedRoot + [System.IO.Path]::DirectorySeparatorChar
        if ($destinationPath.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $allowed = $true
            break
        }
    }
    if (-not $allowed) {
        throw "Output path must resolve beneath one of: $($AllowedRelativeRoots -join ', ')"
    }

    $relativeDestination = $destinationPath.Substring($repositoryPrefix.Length)
    $currentPath = $repositoryPath
    foreach ($segment in ($relativeDestination -split '[\\/]' | Where-Object { $_.Length -gt 0 })) {
        $currentPath = Join-Path $currentPath $segment
        if (-not (Test-Path -LiteralPath $currentPath)) {
            break
        }

        $item = Get-Item -LiteralPath $currentPath -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Output path traverses a reparse point: $currentPath"
        }
        if (-not $item.PSIsContainer) {
            throw "Output path component is not a directory: $currentPath"
        }
    }

    return $destinationPath
}

function Remove-DirectoryWithRetry {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [int]$MaximumAttempts = 20
    )

    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        if (-not (Test-Path -LiteralPath $Path)) {
            return
        }

        $item = Get-Item -LiteralPath $Path -Force
        if (-not $item.PSIsContainer -or
            ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to recursively remove an unexpected output path: $Path"
        }
        $nestedReparsePoint = Get-ChildItem -LiteralPath $Path -Force -Recurse -ErrorAction Stop |
            Where-Object { ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 } |
            Select-Object -First 1
        if ($nestedReparsePoint) {
            throw "Refusing to recursively remove an output containing a reparse point: $($nestedReparsePoint.FullName)"
        }

        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -eq $MaximumAttempts) {
                throw "Failed to remove $Path after $MaximumAttempts attempts: $($_.Exception.Message)"
            }
            Start-Sleep -Milliseconds 250
        }
    }
}
