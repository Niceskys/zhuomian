[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$selector = Join-Path $PSScriptRoot 'select-performance-runs.ps1'
if (-not (Test-Path -LiteralPath $selector -PathType Leaf)) {
    throw "Performance run selector not found: $selector"
}

function New-Metric {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Unit,
        [Parameter(Mandatory)][double]$Value
    )

    return [ordered]@{
        name = $Name
        unit = $Unit
        average = $Value
        p95 = $Value + 1
        p99 = $Value + 2
        max = $Value + 3
    }
}

function New-Summary {
    param([Parameter(Mandatory)][double[]]$Values)

    $runs = for ($index = 0; $index -lt $Values.Count; $index++) {
        [ordered]@{
            run = $index + 1
            rawResultFile = 'run-{0:D2}.csv' -f ($index + 1)
            sampleCount = 20
            metrics = @(
                (New-Metric -Name 'cpuPercent' -Unit 'percent' -Value $Values[$index]),
                (New-Metric -Name 'privateBytes' -Unit 'bytes' -Value ($Values[$index] * 100))
            )
        }
    }

    return [ordered]@{
        summarySchemaVersion = 1
        source = 'zhuomian-process-sampler'
        percentileMethod = 'nearest-rank'
        runCount = $Values.Count
        runs = @($runs)
    }
}

function Write-Summary {
    param(
        [Parameter(Mandatory)]$Summary,
        [Parameter(Mandatory)][string]$Name
    )

    $path = Join-Path $tempRoot $Name
    $Summary | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -Encoding utf8
    return $path
}

function Assert-Selection {
    param(
        [Parameter(Mandatory)]$Selection,
        [Parameter(Mandatory)][int]$MedianRun,
        [Parameter(Mandatory)][int]$WorstRun,
        [Parameter(Mandatory)][int[]]$RankedRuns,
        [Parameter(Mandatory)][string]$Context
    )

    if ($Selection.medianRun -ne $MedianRun -or $Selection.worstRun -ne $WorstRun) {
        throw "$Context selected median/worst $($Selection.medianRun)/$($Selection.worstRun), expected $MedianRun/$WorstRun."
    }

    $actualRanking = @($Selection.rankedRuns | ForEach-Object run)
    if (($actualRanking -join ',') -ne ($RankedRuns -join ',')) {
        throw "$Context ranking '$($actualRanking -join ',')' did not match '$($RankedRuns -join ',')'."
    }
}

function Assert-Fails {
    param(
        [Parameter(Mandatory)][scriptblock]$Action,
        [Parameter(Mandatory)][string]$ExpectedMessagePattern
    )

    $failed = $false
    try {
        & $Action
    }
    catch {
        $failed = $true
        if ($_.Exception.Message -notmatch $ExpectedMessagePattern) {
            throw "Run selector failed for an unexpected reason. Expected '$ExpectedMessagePattern', got '$($_.Exception.Message)'."
        }
    }

    if (-not $failed) {
        throw "Expected run-selector failure matching '$ExpectedMessagePattern', but the action succeeded."
    }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("zhuomian-run-selection-{0}" -f [guid]::NewGuid())
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

try {
    $fiveRunPath = Write-Summary -Summary (New-Summary -Values @(30, 10, 20, 40, 50)) -Name 'five-runs.json'
    $higher = & $selector `
        -SummaryPath $fiveRunPath `
        -OutputPath (Join-Path $tempRoot 'higher.json') `
        -MetricName 'cpuPercent' `
        -Statistic 'average' `
        -Direction 'higher-is-worse'
    Assert-Selection -Selection $higher -MedianRun 1 -WorstRun 5 -RankedRuns @(2, 3, 1, 4, 5) -Context 'higher-is-worse'
    if ($higher.selector.medianMethod -ne 'nearest-rank-50' -or $higher.selector.tieBreak -ne 'lowest-run-number') {
        throw 'Run selector did not persist its median/tie policy.'
    }

    $lower = & $selector `
        -SummaryPath $fiveRunPath `
        -OutputPath (Join-Path $tempRoot 'lower.json') `
        -MetricName 'cpuPercent' `
        -Statistic 'average' `
        -Direction 'lower-is-worse'
    Assert-Selection -Selection $lower -MedianRun 1 -WorstRun 2 -RankedRuns @(5, 4, 1, 3, 2) -Context 'lower-is-worse'

    $evenPath = Write-Summary -Summary (New-Summary -Values @(40, 10, 30, 20)) -Name 'even-runs.json'
    $even = & $selector `
        -SummaryPath $evenPath `
        -OutputPath (Join-Path $tempRoot 'even.json') `
        -MetricName 'cpuPercent' `
        -Statistic 'average' `
        -Direction 'higher-is-worse'
    Assert-Selection -Selection $even -MedianRun 4 -WorstRun 1 -RankedRuns @(2, 4, 3, 1) -Context 'even-nearest-rank'

    $tiePath = Write-Summary -Summary (New-Summary -Values @(10, 20, 20)) -Name 'ties.json'
    $ties = & $selector `
        -SummaryPath $tiePath `
        -OutputPath (Join-Path $tempRoot 'ties-output.json') `
        -MetricName 'cpuPercent' `
        -Statistic 'average' `
        -Direction 'higher-is-worse'
    Assert-Selection -Selection $ties -MedianRun 2 -WorstRun 2 -RankedRuns @(1, 2, 3) -Context 'tie-break'

    $missingMetric = New-Summary -Values @(1, 2, 3)
    $missingMetric.runs[1].metrics = @($missingMetric.runs[1].metrics | Where-Object name -ne 'cpuPercent')
    $missingPath = Write-Summary -Summary $missingMetric -Name 'missing-metric.json'
    Assert-Fails -ExpectedMessagePattern 'exactly one metric' -Action {
        & $selector -SummaryPath $missingPath -OutputPath (Join-Path $tempRoot 'missing-output.json') -MetricName 'cpuPercent' -Statistic 'average' -Direction 'higher-is-worse' | Out-Null
    }

    $duplicateMetric = New-Summary -Values @(1, 2, 3)
    $duplicateMetric.runs[0].metrics = @($duplicateMetric.runs[0].metrics) + @($duplicateMetric.runs[0].metrics[0])
    $duplicatePath = Write-Summary -Summary $duplicateMetric -Name 'duplicate-metric.json'
    Assert-Fails -ExpectedMessagePattern 'exactly one metric' -Action {
        & $selector -SummaryPath $duplicatePath -OutputPath (Join-Path $tempRoot 'duplicate-output.json') -MetricName 'cpuPercent' -Statistic 'average' -Direction 'higher-is-worse' | Out-Null
    }

    $gap = New-Summary -Values @(1, 2, 3)
    $gap.runs[1].run = 3
    $gapPath = Write-Summary -Summary $gap -Name 'run-gap.json'
    Assert-Fails -ExpectedMessagePattern 'contiguous and ordered' -Action {
        & $selector -SummaryPath $gapPath -OutputPath (Join-Path $tempRoot 'gap-output.json') -MetricName 'cpuPercent' -Statistic 'average' -Direction 'higher-is-worse' | Out-Null
    }

    $unitMismatch = New-Summary -Values @(1, 2, 3)
    $unitMismatch.runs[2].metrics[0].unit = 'milliseconds'
    $unitPath = Write-Summary -Summary $unitMismatch -Name 'unit-mismatch.json'
    Assert-Fails -ExpectedMessagePattern 'same unit' -Action {
        & $selector -SummaryPath $unitPath -OutputPath (Join-Path $tempRoot 'unit-output.json') -MetricName 'cpuPercent' -Statistic 'average' -Direction 'higher-is-worse' | Out-Null
    }

    $nonFinite = New-Summary -Values @(1, 2, 3)
    $nonFinitePath = Write-Summary -Summary $nonFinite -Name 'non-finite.json'
    $nonFiniteJson = Get-Content -LiteralPath $nonFinitePath -Raw
    $nonFiniteJson = $nonFiniteJson -replace '"average": 3([.,]0+)?,', '"average": 1e309,'
    Set-Content -LiteralPath $nonFinitePath -Value $nonFiniteJson -Encoding utf8
    Assert-Fails -ExpectedMessagePattern 'finite number' -Action {
        & $selector -SummaryPath $nonFinitePath -OutputPath (Join-Path $tempRoot 'non-finite-output.json') -MetricName 'cpuPercent' -Statistic 'average' -Direction 'higher-is-worse' | Out-Null
    }

    Write-Host 'Performance run-selector self-test passed: explicit selector semantics, both directions, nearest-rank median, deterministic ties, and malformed-summary rejection.'
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
