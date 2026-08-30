[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$summarizer = Join-Path $PSScriptRoot 'summarize-process-samples.ps1'
if (-not (Test-Path -LiteralPath $summarizer -PathType Leaf)) {
    throw "Process sample summarizer not found: $summarizer"
}

function Write-RunFixture {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][int]$Offset,
        [int]$SampleCount = 20
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('timestampUtc,elapsedMilliseconds,cpuPercent,privateBytes,workingSetBytes,handleCount,threadCount')

    for ($index = 1; $index -le $SampleCount; $index++) {
        $timestamp = [DateTimeOffset]::new(2026, 8, 30, 0, 0, 0, [TimeSpan]::Zero).AddMilliseconds($index * 100).ToString('O', [Globalization.CultureInfo]::InvariantCulture)
        $value = $index + $Offset
        $lines.Add("$timestamp,$($index * 100),$value,$($value * 100),$($value * 200),$($value + 2),$($value + 1)")
    }

    $lines | Set-Content -LiteralPath $Path -Encoding utf8
}

function Assert-Close {
    param(
        [Parameter(Mandatory)][double]$Actual,
        [Parameter(Mandatory)][double]$Expected,
        [double]$Tolerance = 0.000001,
        [Parameter(Mandatory)][string]$Context
    )

    if ([Math]::Abs($Actual - $Expected) -gt $Tolerance) {
        throw "$Context expected $Expected, got $Actual."
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
            throw "Summarizer failed for an unexpected reason. Expected '$ExpectedMessagePattern', got '$($_.Exception.Message)'."
        }
    }

    if (-not $failed) {
        throw "Expected summarizer failure matching '$ExpectedMessagePattern', but the action succeeded."
    }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("zhuomian-process-summary-{0}" -f [guid]::NewGuid())
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

try {
    $validDirectory = Join-Path $tempRoot 'valid'
    New-Item -ItemType Directory -Path $validDirectory -Force | Out-Null
    Write-RunFixture -Path (Join-Path $validDirectory 'run-01.csv') -Offset 0
    Write-RunFixture -Path (Join-Path $validDirectory 'run-02.csv') -Offset 10

    $summaryPath = Join-Path $tempRoot 'summary.json'
    $summary = & $summarizer -InputDirectory $validDirectory -OutputPath $summaryPath

    if (-not (Test-Path -LiteralPath $summaryPath -PathType Leaf)) {
        throw 'Summarizer did not write summary.json.'
    }

    if ($summary.summarySchemaVersion -ne 1 -or $summary.percentileMethod -ne 'nearest-rank' -or $summary.runCount -ne 2) {
        throw 'Summarizer metadata is incorrect.'
    }

    if ($summary.runs.Count -ne 2 -or $summary.runs[0].sampleCount -ne 20 -or $summary.runs[1].sampleCount -ne 20) {
        throw 'Summarizer did not preserve both run/sample counts.'
    }

    $cpuRun1 = @($summary.runs[0].metrics | Where-Object name -eq 'cpuPercent')[0]
    Assert-Close -Actual $cpuRun1.average -Expected 10.5 -Context 'run-01 cpu average'
    Assert-Close -Actual $cpuRun1.p95 -Expected 19 -Context 'run-01 cpu p95'
    Assert-Close -Actual $cpuRun1.p99 -Expected 20 -Context 'run-01 cpu p99'
    Assert-Close -Actual $cpuRun1.max -Expected 20 -Context 'run-01 cpu max'

    $privateRun2 = @($summary.runs[1].metrics | Where-Object name -eq 'privateBytes')[0]
    Assert-Close -Actual $privateRun2.average -Expected 2050 -Context 'run-02 privateBytes average'
    Assert-Close -Actual $privateRun2.p95 -Expected 2900 -Context 'run-02 privateBytes p95'
    Assert-Close -Actual $privateRun2.p99 -Expected 3000 -Context 'run-02 privateBytes p99'
    Assert-Close -Actual $privateRun2.max -Expected 3000 -Context 'run-02 privateBytes max'

    $persisted = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    if ($persisted.runCount -ne 2 -or $persisted.runs[0].rawResultFile -ne 'run-01.csv') {
        throw 'Persisted summary does not match the in-memory summary.'
    }

    $hundredDirectory = Join-Path $tempRoot 'hundred-runs'
    New-Item -ItemType Directory -Path $hundredDirectory -Force | Out-Null
    for ($run = 1; $run -le 100; $run++) {
        Write-RunFixture -Path (Join-Path $hundredDirectory ('run-{0:D2}.csv' -f $run)) -Offset $run -SampleCount 1
    }

    $hundredSummary = & $summarizer -InputDirectory $hundredDirectory -OutputPath (Join-Path $tempRoot 'hundred-summary.json')
    if ($hundredSummary.runCount -ne 100 -or $hundredSummary.runs[99].run -ne 100 -or $hundredSummary.runs[99].rawResultFile -ne 'run-100.csv') {
        throw 'Summarizer did not preserve numeric ordering through run-100.csv.'
    }

    $gapDirectory = Join-Path $tempRoot 'gap'
    New-Item -ItemType Directory -Path $gapDirectory -Force | Out-Null
    Write-RunFixture -Path (Join-Path $gapDirectory 'run-01.csv') -Offset 0
    Write-RunFixture -Path (Join-Path $gapDirectory 'run-03.csv') -Offset 0
    Assert-Fails -ExpectedMessagePattern 'contiguous' -Action {
        & $summarizer -InputDirectory $gapDirectory -OutputPath (Join-Path $tempRoot 'gap-summary.json') | Out-Null
    }

    $headerDirectory = Join-Path $tempRoot 'bad-header'
    New-Item -ItemType Directory -Path $headerDirectory -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $headerDirectory 'run-01.csv') -Value "timestampUtc,wrongColumn`n2026-08-30T00:00:00.0000000+00:00,1" -Encoding utf8
    Assert-Fails -ExpectedMessagePattern 'unexpected CSV header' -Action {
        & $summarizer -InputDirectory $headerDirectory -OutputPath (Join-Path $tempRoot 'header-summary.json') | Out-Null
    }

    $elapsedDirectory = Join-Path $tempRoot 'bad-elapsed'
    New-Item -ItemType Directory -Path $elapsedDirectory -Force | Out-Null
    @(
        'timestampUtc,elapsedMilliseconds,cpuPercent,privateBytes,workingSetBytes,handleCount,threadCount',
        '2026-08-30T00:00:00.1000000+00:00,100,1,100,200,3,2',
        '2026-08-30T00:00:00.2000000+00:00,100,2,200,400,4,3'
    ) | Set-Content -LiteralPath (Join-Path $elapsedDirectory 'run-01.csv') -Encoding utf8
    Assert-Fails -ExpectedMessagePattern 'increase strictly' -Action {
        & $summarizer -InputDirectory $elapsedDirectory -OutputPath (Join-Path $tempRoot 'elapsed-summary.json') | Out-Null
    }

    $nonFiniteDirectory = Join-Path $tempRoot 'non-finite'
    New-Item -ItemType Directory -Path $nonFiniteDirectory -Force | Out-Null
    @(
        'timestampUtc,elapsedMilliseconds,cpuPercent,privateBytes,workingSetBytes,handleCount,threadCount',
        '2026-08-30T00:00:00.1000000+00:00,100,NaN,100,200,3,2'
    ) | Set-Content -LiteralPath (Join-Path $nonFiniteDirectory 'run-01.csv') -Encoding utf8
    Assert-Fails -ExpectedMessagePattern 'finite number' -Action {
        & $summarizer -InputDirectory $nonFiniteDirectory -OutputPath (Join-Path $tempRoot 'non-finite-summary.json') | Out-Null
    }

    $fractionalResourceDirectory = Join-Path $tempRoot 'fractional-resource'
    New-Item -ItemType Directory -Path $fractionalResourceDirectory -Force | Out-Null
    @(
        'timestampUtc,elapsedMilliseconds,cpuPercent,privateBytes,workingSetBytes,handleCount,threadCount',
        '2026-08-30T00:00:00.1000000+00:00,100,1,100,200,3.5,2'
    ) | Set-Content -LiteralPath (Join-Path $fractionalResourceDirectory 'run-01.csv') -Encoding utf8
    Assert-Fails -ExpectedMessagePattern 'non-negative integer' -Action {
        & $summarizer -InputDirectory $fractionalResourceDirectory -OutputPath (Join-Path $tempRoot 'fractional-summary.json') | Out-Null
    }

    Write-Host 'Process sample summarizer self-test passed: deterministic per-run statistics, run-100 ordering, and gap/header/timing/non-finite/non-integer rejection.'
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
