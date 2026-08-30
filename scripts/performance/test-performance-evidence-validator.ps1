[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$validator = Join-Path $PSScriptRoot 'validate-performance-evidence.ps1'
if (-not (Test-Path -LiteralPath $validator -PathType Leaf)) {
    throw "Validator script not found: $validator"
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("zhuomian-perf-contract-{0}" -f [guid]::NewGuid())
$rawDirectory = Join-Path $tempRoot 'raw'
New-Item -ItemType Directory -Path $rawDirectory -Force | Out-Null
Set-Content -LiteralPath (Join-Path $rawDirectory 'run1.csv') -Value "timestamp,value`n0,1" -Encoding utf8

function New-ValidEvidence {
    return [ordered]@{
        schemaVersion = 1
        commitSha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
        scenarioId = 'S1'
        collectedAtUtc = '2026-08-30T00:00:00Z'
        machineTier = 'CI'
        eligibleForThresholdCalibration = $false
        environment = [ordered]@{
            windowsBuild = '10.0.26100'
            windowsAppSdkVersion = 'not-applicable-contract-fixture'
            gpuDriver = 'contract-fixture'
            cpu = 'contract-fixture'
            memoryGb = 16
            gpu = 'contract-fixture'
            displays = @(
                [ordered]@{
                    dpi = 96
                    refreshHz = 60
                }
            )
        }
        build = [ordered]@{
            configuration = 'Release'
            architecture = 'x64'
            packagingMode = 'unpackaged'
        }
        protocol = [ordered]@{
            warmupSeconds = 60
            measurementSeconds = 300
            repetitions = 3
            condition = 'warm'
            deviationReason = $null
        }
        collectionCommand = './scripts/performance/example-collector.ps1 -Scenario S1'
        rawResultFiles = @('raw/run1.csv')
        metrics = @(
            [ordered]@{
                name = 'cpuPercent'
                unit = 'percent'
                average = 0.5
                p95 = 0.7
                p99 = 0.9
                max = 1.2
            }
        )
        framePresentation = [ordered]@{
            measured = $false
            notApplicableReason = 'Contract fixture does not present frames.'
        }
        runSelection = [ordered]@{
            selector = [ordered]@{
                metricName = 'cpuPercent'
                unit = 'percent'
                statistic = 'average'
                direction = 'higher-is-worse'
                medianMethod = 'nearest-rank-50'
                tieBreak = 'lowest-run-number'
            }
            medianRun = 2
            medianValue = 0.5
            worstRun = 3
            worstValue = 0.8
        }
    }
}

function Write-Evidence {
    param(
        [Parameter(Mandatory)]$Evidence,
        [Parameter(Mandatory)][string]$Name
    )

    $path = Join-Path $tempRoot $Name
    $Evidence | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -Encoding utf8
    return $path
}

function Assert-Passes {
    param(
        [Parameter(Mandatory)]$Evidence,
        [Parameter(Mandatory)][string]$Name
    )

    $path = Write-Evidence -Evidence $Evidence -Name $Name
    & $validator -Path $path | Out-Null
}

function Assert-Fails {
    param(
        [Parameter(Mandatory)]$Evidence,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ExpectedMessagePattern
    )

    $path = Write-Evidence -Evidence $Evidence -Name $Name
    $failed = $false

    try {
        & $validator -Path $path | Out-Null
    }
    catch {
        $failed = $true
        if ($_.Exception.Message -notmatch $ExpectedMessagePattern) {
            throw "Validator failed for an unexpected reason. Expected '$ExpectedMessagePattern', got '$($_.Exception.Message)'."
        }
    }

    if (-not $failed) {
        throw "Expected validator failure for $Name, but validation succeeded."
    }
}

try {
    Assert-Passes -Evidence (New-ValidEvidence) -Name 'valid.json'

    $nonUtcTimestamp = New-ValidEvidence
    $nonUtcTimestamp.collectedAtUtc = '2026-08-30T08:00:00+08:00'
    Assert-Fails -Evidence $nonUtcTimestamp -Name 'non-utc-timestamp.json' -ExpectedMessagePattern 'must use UTC offset'

    $nonIsoTimestamp = New-ValidEvidence
    $nonIsoTimestamp.collectedAtUtc = '2026/08/30 00:00:00Z'
    Assert-Fails -Evidence $nonIsoTimestamp -Name 'non-iso-timestamp.json' -ExpectedMessagePattern 'extended ISO-8601'

    $ciCalibration = New-ValidEvidence
    $ciCalibration.eligibleForThresholdCalibration = $true
    Assert-Fails -Evidence $ciCalibration -Name 'ci-calibration.json' -ExpectedMessagePattern 'Only Baseline or Enhanced'

    $undocumentedDeviation = New-ValidEvidence
    $undocumentedDeviation.protocol.measurementSeconds = 30
    Assert-Fails -Evidence $undocumentedDeviation -Name 'undocumented-deviation.json' -ExpectedMessagePattern 'requires evidence.protocol.deviationReason'

    $privateRawPath = New-ValidEvidence
    $privateRawPath.rawResultFiles = @('C:\Users\example\run.csv')
    Assert-Fails -Evidence $privateRawPath -Name 'private-raw-path.json' -ExpectedMessagePattern 'evidence-relative paths'

    $invalidPercentiles = New-ValidEvidence
    $invalidPercentiles.metrics[0].p95 = 10
    $invalidPercentiles.metrics[0].p99 = 9
    Assert-Fails -Evidence $invalidPercentiles -Name 'invalid-percentiles.json' -ExpectedMessagePattern 'p95 <= p99 <= max'

    $invalidDroppedFrames = New-ValidEvidence
    $invalidDroppedFrames.framePresentation = [ordered]@{
        measured = $true
        droppedFrameRatio = 1.2
    }
    Assert-Fails -Evidence $invalidDroppedFrames -Name 'invalid-dropped-frames.json' -ExpectedMessagePattern 'between 0 and 1'

    $invalidDirection = New-ValidEvidence
    $invalidDirection.runSelection.selector.direction = 'sideways'
    Assert-Fails -Evidence $invalidDirection -Name 'invalid-selection-direction.json' -ExpectedMessagePattern 'higher-is-worse or lower-is-worse'

    $missingSelectionMetric = New-ValidEvidence
    $missingSelectionMetric.runSelection.selector.metricName = 'missingMetric'
    Assert-Fails -Evidence $missingSelectionMetric -Name 'missing-selection-metric.json' -ExpectedMessagePattern 'exactly one run-selection metric'

    $medianMetricMismatch = New-ValidEvidence
    $medianMetricMismatch.runSelection.medianValue = 0.6
    Assert-Fails -Evidence $medianMetricMismatch -Name 'median-metric-mismatch.json' -ExpectedMessagePattern 'metrics must describe medianRun'

    $invalidWorstDirection = New-ValidEvidence
    $invalidWorstDirection.runSelection.worstValue = 0.4
    Assert-Fails -Evidence $invalidWorstDirection -Name 'invalid-worst-direction.json' -ExpectedMessagePattern 'worstValue must be >= medianValue'

    Write-Host 'Performance evidence validator self-test passed: 1 valid fixture and 11 invalid fixtures.'
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
