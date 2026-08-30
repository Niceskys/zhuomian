[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sampler = Join-Path $PSScriptRoot 'collect-process-samples.ps1'
if (-not (Test-Path -LiteralPath $sampler -PathType Leaf)) {
    throw "Process sampler not found: $sampler"
}

function Assert-Fails {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Action,

        [Parameter(Mandatory)]
        [string]$ExpectedMessagePattern
    )

    $failed = $false

    try {
        & $Action
    }
    catch {
        $failed = $true
        if ($_.Exception.Message -notmatch $ExpectedMessagePattern) {
            throw "Sampler failed for an unexpected reason. Expected '$ExpectedMessagePattern', got '$($_.Exception.Message)'."
        }
    }

    if (-not $failed) {
        throw "Expected sampler failure matching '$ExpectedMessagePattern', but the action succeeded."
    }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("zhuomian-process-sampler-{0}" -f [guid]::NewGuid())
$sampleDirectory = Join-Path $tempRoot 'samples'
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

try {
    $pwshPath = (Get-Process -Id $PID).Path
    $result = & $sampler `
        -ExecutablePath $pwshPath `
        -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 10') `
        -OutputDirectory $sampleDirectory `
        -WarmupSeconds 0 `
        -MeasurementSeconds 1 `
        -SampleIntervalMilliseconds 200 `
        -Repetitions 2

    if ($result.Repetitions -ne 2 -or $result.RawResultFiles.Count -ne 2 -or $result.SampleCounts.Count -ne 2) {
        throw "Sampler result summary did not report both repetitions."
    }

    foreach ($rawResultFile in $result.RawResultFiles) {
        if ([IO.Path]::IsPathRooted($rawResultFile)) {
            throw "Sampler returned an absolute raw-result path."
        }

        $rawPath = Join-Path $sampleDirectory $rawResultFile
        if (-not (Test-Path -LiteralPath $rawPath -PathType Leaf)) {
            throw "Sampler raw-result file is missing: $rawResultFile"
        }

        $samples = @(Import-Csv -LiteralPath $rawPath)
        if ($samples.Count -lt 2) {
            throw "Sampler smoke test expected at least two rows per repetition."
        }

        $previousElapsed = -1.0
        foreach ($sample in $samples) {
            $timestamp = [DateTimeOffset]::MinValue
            if (-not [DateTimeOffset]::TryParseExact(
                    $sample.timestampUtc,
                    'O',
                    [Globalization.CultureInfo]::InvariantCulture,
                    [Globalization.DateTimeStyles]::None,
                    [ref]$timestamp)) {
                throw "Sampler timestamp is not round-trip UTC ISO-8601."
            }

            if ($timestamp.Offset -ne [TimeSpan]::Zero) {
                throw "Sampler timestamp is not UTC."
            }

            $elapsed = [double]::Parse($sample.elapsedMilliseconds, [Globalization.CultureInfo]::InvariantCulture)
            $cpu = [double]::Parse($sample.cpuPercent, [Globalization.CultureInfo]::InvariantCulture)
            $privateBytes = [long]::Parse($sample.privateBytes, [Globalization.CultureInfo]::InvariantCulture)
            $workingSetBytes = [long]::Parse($sample.workingSetBytes, [Globalization.CultureInfo]::InvariantCulture)
            $handleCount = [int]::Parse($sample.handleCount, [Globalization.CultureInfo]::InvariantCulture)
            $threadCount = [int]::Parse($sample.threadCount, [Globalization.CultureInfo]::InvariantCulture)

            if ($elapsed -le $previousElapsed) {
                throw "Sampler elapsed time must increase monotonically."
            }

            if ($cpu -lt 0 -or [double]::IsNaN($cpu) -or [double]::IsInfinity($cpu)) {
                throw "Sampler CPU value must be finite and non-negative."
            }

            if ($privateBytes -le 0 -or $workingSetBytes -le 0 -or $handleCount -le 0 -or $threadCount -le 0) {
                throw "Sampler process resource values must be positive during the smoke test."
            }

            $previousElapsed = $elapsed
        }

        $rawText = Get-Content -LiteralPath $rawPath -Raw
        if (-not [string]::IsNullOrWhiteSpace($HOME) -and $rawText.IndexOf($HOME, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Sampler raw output must not persist the runner home path."
        }
    }

    foreach ($processId in $result.LaunchedProcessIds) {
        if ($null -ne (Get-Process -Id $processId -ErrorAction SilentlyContinue)) {
            throw "Sampler left an owned process running after measurement."
        }
    }

    $nonEmptyDirectory = Join-Path $tempRoot 'non-empty'
    New-Item -ItemType Directory -Path $nonEmptyDirectory -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $nonEmptyDirectory 'existing.txt') -Value 'sentinel' -Encoding utf8

    Assert-Fails -ExpectedMessagePattern 'must be empty' -Action {
        & $sampler `
            -ExecutablePath $pwshPath `
            -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 10') `
            -OutputDirectory $nonEmptyDirectory `
            -WarmupSeconds 0 `
            -MeasurementSeconds 1 `
            -SampleIntervalMilliseconds 200 `
            -Repetitions 1 | Out-Null
    }

    $earlyExitDirectory = Join-Path $tempRoot 'early-exit'
    Assert-Fails -ExpectedMessagePattern 'exited' -Action {
        & $sampler `
            -ExecutablePath $pwshPath `
            -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-Command', 'exit 0') `
            -OutputDirectory $earlyExitDirectory `
            -WarmupSeconds 0 `
            -MeasurementSeconds 1 `
            -SampleIntervalMilliseconds 200 `
            -Repetitions 1 | Out-Null
    }

    Write-Host 'Process sampler smoke test passed: two short repetitions plus non-empty-output and early-exit rejection.'
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
