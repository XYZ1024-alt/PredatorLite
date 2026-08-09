[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "release-output.ps1")

function Assert-Equal {
    param(
        [Parameter(Mandatory)]
        [object]$Expected,
        [Parameter(Mandatory)]
        [object]$Actual,
        [Parameter(Mandatory)]
        [string]$Description
    )

    if ($Expected -ne $Actual) {
        throw "$Description expected '$Expected' but received '$Actual'."
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Action,
        [Parameter(Mandatory)]
        [string]$MessagePattern,
        [Parameter(Mandatory)]
        [string]$Description
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notlike $MessagePattern) {
            throw "$Description threw an unexpected message: $($_.Exception.Message)"
        }
        return
    }

    throw "$Description did not throw."
}

$stable = Resolve-PredatorLiteReleaseVersion -BaseVersion "1.2.3" -Channel Stable
Assert-Equal -Expected "1.2.3" -Actual $stable.ReleaseVersion -Description "Stable release version"
Assert-Equal -Expected "1.2.3.0" -Actual $stable.AssemblyVersion -Description "Stable assembly version"
Assert-Equal -Expected "1.2.3.65535" -Actual $stable.FileVersion -Description "Stable file version"
Assert-Equal -Expected $false -Actual $stable.IsDraft -Description "Stable draft state"
Assert-Equal -Expected $false -Actual $stable.IsPrerelease -Description "Stable prerelease state"

$beta = Resolve-PredatorLiteReleaseVersion -BaseVersion "1.2.3" -Channel Beta -Iteration "4"
Assert-Equal -Expected "1.2.3-beta.4" -Actual $beta.ReleaseVersion -Description "Beta release version"
Assert-Equal -Expected "1.2.3.4" -Actual $beta.FileVersion -Description "Beta file version"
Assert-Equal -Expected $true -Actual $beta.IsDraft -Description "Beta draft state"
Assert-Equal -Expected $true -Actual $beta.IsPrerelease -Description "Beta prerelease state"

$rc = Resolve-PredatorLiteReleaseVersion -BaseVersion "1.2.3" -Channel RC -Iteration "7"
Assert-Equal -Expected "1.2.3-rc.7" -Actual $rc.ReleaseVersion -Description "RC release version"
Assert-Equal -Expected "1.2.3.10007" -Actual $rc.FileVersion -Description "RC file version"
Assert-Equal -Expected $false -Actual $rc.IsDraft -Description "RC draft state"
Assert-Equal -Expected $true -Actual $rc.IsPrerelease -Description "RC prerelease state"

Assert-Throws `
    -Action { Resolve-PredatorLiteReleaseVersion -BaseVersion "1.2" -Channel Stable } `
    -MessagePattern "Base Version must use three numeric components*" `
    -Description "Invalid base version"
Assert-Throws `
    -Action { Resolve-PredatorLiteReleaseVersion -BaseVersion "1.2.3" -Channel Stable -Iteration "1" } `
    -MessagePattern "Stable releases must not specify an iteration*" `
    -Description "Stable iteration"
Assert-Throws `
    -Action { Resolve-PredatorLiteReleaseVersion -BaseVersion "1.2.3" -Channel Beta } `
    -MessagePattern "Beta releases require an iteration*" `
    -Description "Missing beta iteration"
Assert-Throws `
    -Action { Resolve-PredatorLiteReleaseVersion -BaseVersion "1.2.3" -Channel RC -Iteration "10000" } `
    -MessagePattern "RC releases require an iteration*" `
    -Description "Oversized RC iteration"

Write-Host "Release version policy passed."
