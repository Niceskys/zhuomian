[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assembler = Join-Path $PSScriptRoot 'assemble-performance-evidence.ps1'
$summarizer = Join-Path $PSScriptRoot 'summarize-process-samples.ps1'
$selector = Join-Path $PSScriptRoot 'select-performance-runs.ps1'
foreach ($scriptPath in @($assembler, $summarizer, $selector)) {
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw "Required performance script not found: $scriptPath"
    }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("zhuomian-perf-assembly-{0}" -f [guid]::NewGuid())
$rawDirectory = Join-Path $tempRoot 'raw'
New-Item -ItemType Directory -Path $rawDirectory -Force | Out-Null

function Write-JsonFile {
    param(
        [Parameter(Mandatory)]$Value,
        [Parameter(Mandatory)][string]$Path
    )

    $Value | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Write-SyntheticRun {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][int]$CpuBase
    )

    $culture = [Globalization.CultureInfo]::InvariantCulture
    $rows = [System.Collections.Generic.List[string]]::new()
    $rows.Add('timestampUtc,elapsedMilliseconds,cpuPercent,privateBytes,workingSetBytes,handleCount,threadCount')
    for ($index = 1; $index -le 3; $index++) {
        $timestamp = [DateTimeOffset]::UtcNow.AddSeconds($index).ToString('O', $culture)
        $rows.Add([string]::Format(
                $culture,
                '{0},{1},{2:F1},{3},{4},{5},{6}',
                $timestamp,
                $index,
                $CpuBase + $index - 1,
                1000 + ($CpuBase * 10) + $index,
                2000 + ($CpuBase * 10) + $index,
                10 + $CpuBase + $index,
                20 + $CpuBase + $index))
    }

    $rows | Set-Content -LiteralPath $Path -Encoding utf8
}

function Assert-Fails {
    param(
        [Parameter(Mandatory)][hashtable]$Arguments,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ExpectedMessagePattern
    )

    $failed = $false
    try {
        & $assembler @Arguments | Out-Null
    }
    catch {
        $failed = $true
        if ($_.Exception.Message -notmatch $ExpectedMessagePattern) {
            throw "Assembler failed for an unexpected reason in $Name. Expected '$ExpectedMessagePattern', got '$($_.Exception.Message)'."
        }
    }

    if (-not $failed) {
        throw "Expected assembler failure for $Name, but assembly succeeded."
    }
}

try {
    Write-SyntheticRun -Path (Join-Path $rawDirectory 'run-01.csv') -CpuBase 1
    Write-SyntheticRun -Path (Join-Path $rawDirectory 'run-02.csv') -CpuBase 5

    $summaryPath = Join-Path $rawDirectory 'summary.json'
    $selectionPath = Join-Path $rawDirectory 'selection.json'
    $metadataPath = Join-Path $rawDirectory 'metadata.json'
    $outputPath = Join-Path $rawDirectory 'evidence.json'

    & $summarizer -InputDirectory $rawDirectory -OutputPath $summaryPath | Out-Null
    & $selector `
        -SummaryPath $summaryPath `
        -OutputPath $selectionPath `
        -MetricName 'cpuPercent' `
        -Statistic 'average' `
        -Direction 'higher-is-worse' | Out-Null

    $metadata = [ordered]@{
        commitSha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
        scenarioId = 'S1'
        collectedAtUtc = '2026-09-01T00:00:00Z'
        machineTier = 'CI'
        eligibleForThresholdCalibration = $false
        environment = [ordered]@{
            windowsBuild = '10.0.26100'
            windowsAppSdkVersion = 'synthetic-fixture'
            gpuDriver = 'synthetic-fixture'
            cpu = 'synthetic-fixture'
            memoryGb = 16
            gpu = 'synthetic-fixture'
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
            warmupSeconds = 0
            measurementSeconds = 1
            repetitions = 2
            condition = 'synthetic'
            deviationReason = 'Synthetic fixture uses shortened timing.'
        }
        collectionCommand = './scripts/performance/test-performance-evidence-assembler.ps1'
        framePresentation = [ordered]@{
            measured = $false
            notApplicableReason = 'Synthetic process-resource fixture does not present frames.'
        }
    }
    Write-JsonFile -Value $metadata -Path $metadataPath

    $assemblyArguments = @{
        SummaryPath = $summaryPath
        SelectionPath = $selectionPath
        MetadataPath = $metadataPath
        OutputPath = $outputPath
    }
    & $assembler @assemblyArguments | Out-Null

    if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
        throw 'Assembler did not write evidence.json.'
    }

    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    $selection = Get-Content -LiteralPath $selectionPath -Raw | ConvertFrom-Json
    $evidence = Get-Content -LiteralPath $outputPath -Raw | ConvertFrom-Json
    $medianSummaryRun = @($summary.runs | Where-Object run -eq $selection.medianRun)[0]
    $medianCpu = @($medianSummaryRun.metrics | Where-Object name -eq 'cpuPercent')[0]

    if ($evidence.schemaVersion -ne 1 -or $evidence.rawResultFiles.Count -ne 2) {
        throw 'Assembled evidence did not preserve the canonical schema or all raw files.'
    }

    if ($evidence.rawResultFiles[0] -ne 'run-01.csv' -or $evidence.rawResultFiles[1] -ne 'run-02.csv') {
        throw 'Assembled raw-result paths were not derived from the summary runs.'
    }

    if ($evidence.runSelection.medianRun -ne $selection.medianRun -or
        $evidence.runSelection.worstRun -ne $selection.worstRun -or
        $evidence.runSelection.medianValue -ne $selection.medianValue -or
        $evidence.runSelection.worstValue -ne $selection.worstValue) {
        throw 'Assembled run-selection metadata was not copied from the selection artifact.'
    }

    if ($evidence.metrics[0].average -ne $medianCpu.average -or
        $evidence.metrics[0].p95 -ne $medianCpu.p95 -or
        $evidence.metrics[0].p99 -ne $medianCpu.p99 -or
        $evidence.metrics[0].max -ne $medianCpu.max) {
        throw 'Assembled metrics were not copied from the selected median summary run.'
    }

    $badSelection = Get-Content -LiteralPath $selectionPath -Raw | ConvertFrom-Json
    $badSelection.sourceSummaryFile = 'different-summary.json'
    $badSelectionPath = Join-Path $rawDirectory 'bad-selection-source.json'
    Write-JsonFile -Value $badSelection -Path $badSelectionPath
    Assert-Fails `
        -Arguments @{
            SummaryPath = $summaryPath
            SelectionPath = $badSelectionPath
            MetadataPath = $metadataPath
            OutputPath = (Join-Path $rawDirectory 'bad-source.json')
        } `
        -Name 'summary source linkage' `
        -ExpectedMessagePattern 'sourceSummaryFile must match'

    $badMetadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    $badMetadata.protocol.repetitions = 3
    $badMetadataPath = Join-Path $rawDirectory 'bad-metadata-repetitions.json'
    Write-JsonFile -Value $badMetadata -Path $badMetadataPath
    Assert-Fails `
        -Arguments @{
            SummaryPath = $summaryPath
            SelectionPath = $selectionPath
            MetadataPath = $badMetadataPath
            OutputPath = (Join-Path $rawDirectory 'bad-repetitions.json')
        } `
        -Name 'protocol repetition linkage' `
        -ExpectedMessagePattern 'repetitions must match'

    $badSummary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    $badSummary.runs[0].rawResultFile = 'missing.csv'
    $badSummaryPath = Join-Path $rawDirectory 'bad-summary-raw.json'
    Write-JsonFile -Value $badSummary -Path $badSummaryPath
    $selectionForBadSummary = Get-Content -LiteralPath $selectionPath -Raw | ConvertFrom-Json
    $selectionForBadSummary.sourceSummaryFile = [IO.Path]::GetFileName($badSummaryPath)
    $selectionForBadSummaryPath = Join-Path $rawDirectory 'selection-for-bad-summary.json'
    Write-JsonFile -Value $selectionForBadSummary -Path $selectionForBadSummaryPath
    Assert-Fails `
        -Arguments @{
            SummaryPath = $badSummaryPath
            SelectionPath = $selectionForBadSummaryPath
            MetadataPath = $metadataPath
            OutputPath = (Join-Path $rawDirectory 'bad-raw-file.json')
        } `
        -Name 'raw-result existence' `
        -ExpectedMessagePattern 'Raw performance result file is missing'

    $badMedian = Get-Content -LiteralPath $selectionPath -Raw | ConvertFrom-Json
    $badMedian.medianValue = $badMedian.medianValue + 1
    $badMedianPath = Join-Path $rawDirectory 'bad-median-value.json'
    Write-JsonFile -Value $badMedian -Path $badMedianPath
    Assert-Fails `
        -Arguments @{
            SummaryPath = $summaryPath
            SelectionPath = $badMedianPath
            MetadataPath = $metadataPath
            OutputPath = (Join-Path $rawDirectory 'bad-median.json')
        } `
        -Name 'median metric linkage' `
        -ExpectedMessagePattern 'deterministic selection'

    Assert-Fails -Arguments $assemblyArguments -Name 'output overwrite' -ExpectedMessagePattern 'output already exists'

    $runTwoCpu = @($summary.runs[1].metrics | Where-Object name -eq 'cpuPercent')[0]
    $forgedMedian = Get-Content -LiteralPath $selectionPath -Raw | ConvertFrom-Json
    $forgedMedian.medianRun = 2
    $forgedMedian.medianValue = $runTwoCpu.average
    $forgedMedianPath = Join-Path $rawDirectory 'forged-median.json'
    Write-JsonFile -Value $forgedMedian -Path $forgedMedianPath
    Assert-Fails `
        -Arguments @{
            SummaryPath = $summaryPath
            SelectionPath = $forgedMedianPath
            MetadataPath = $metadataPath
            OutputPath = (Join-Path $rawDirectory 'forged-median-output.json')
        } `
        -Name 'forged in-range median selection' `
        -ExpectedMessagePattern 'deterministic selection'

    $forgedWorst = Get-Content -LiteralPath $selectionPath -Raw | ConvertFrom-Json
    $forgedWorst.worstRun = 1
    $forgedWorst.worstValue = $medianCpu.average
    $forgedWorstPath = Join-Path $rawDirectory 'forged-worst.json'
    Write-JsonFile -Value $forgedWorst -Path $forgedWorstPath
    Assert-Fails `
        -Arguments @{
            SummaryPath = $summaryPath
            SelectionPath = $forgedWorstPath
            MetadataPath = $metadataPath
            OutputPath = (Join-Path $rawDirectory 'forged-worst-output.json')
        } `
        -Name 'forged worst selection' `
        -ExpectedMessagePattern 'deterministic selection'

    $substitutedRawPath = Join-Path $rawDirectory 'run-01.csv'
    $substitutedRaw = Get-Content -LiteralPath $substitutedRawPath -Raw
    $substitutedRaw = $substitutedRaw -replace ',1\.0,', ',99.0,'
    Set-Content -LiteralPath $substitutedRawPath -Value $substitutedRaw -Encoding utf8
    Assert-Fails `
        -Arguments @{
            SummaryPath = $summaryPath
            SelectionPath = $selectionPath
            MetadataPath = $metadataPath
            OutputPath = (Join-Path $rawDirectory 'substituted-raw-output.json')
        } `
        -Name 'raw-content substitution' `
        -ExpectedMessagePattern 'does not match the current raw performance result files'

    Write-Host 'Performance evidence assembler self-test passed: raw-content and deterministic-selection linkage, canonical validation, provenance rejection and overwrite protection.'
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
