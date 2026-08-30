[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InputDirectory,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$culture = [Globalization.CultureInfo]::InvariantCulture
$expectedHeader = 'timestampUtc,elapsedMilliseconds,cpuPercent,privateBytes,workingSetBytes,handleCount,threadCount'

function Parse-FiniteDouble {
    param(
        [Parameter(Mandatory)][string]$Value,
        [Parameter(Mandatory)][string]$Context,
        [double]$Minimum = 0
    )

    $parsed = 0.0
    if (-not [double]::TryParse(
            $Value,
            [Globalization.NumberStyles]::Float,
            $culture,
            [ref]$parsed)) {
        throw "$Context must be numeric."
    }

    if ([double]::IsNaN($parsed) -or [double]::IsInfinity($parsed) -or $parsed -lt $Minimum) {
        throw "$Context must be a finite number >= $Minimum."
    }

    return $parsed
}

function Parse-NonNegativeInteger {
    param(
        [Parameter(Mandatory)][string]$Value,
        [Parameter(Mandatory)][string]$Context
    )

    $parsed = 0L
    if (-not [long]::TryParse(
            $Value,
            [Globalization.NumberStyles]::Integer,
            $culture,
            [ref]$parsed) -or $parsed -lt 0) {
        throw "$Context must be a non-negative integer."
    }

    return [double]$parsed
}

function Get-NearestRankPercentile {
    param(
        [Parameter(Mandatory)][double[]]$Values,
        [Parameter(Mandatory)][ValidateRange(0.0, 1.0)][double]$Percentile
    )

    if ($Values.Count -eq 0) {
        throw 'Cannot calculate a percentile from an empty sample set.'
    }

    $sorted = @($Values | Sort-Object)
    $rank = [Math]::Ceiling($Percentile * $sorted.Count)
    $index = [Math]::Max(0, [int]$rank - 1)
    return [double]$sorted[$index]
}

function New-MetricSummary {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Unit,
        [Parameter(Mandatory)][double[]]$Values
    )

    if ($Values.Count -eq 0) {
        throw "Metric $Name has no samples."
    }

    $sum = 0.0
    $maximum = [double]::NegativeInfinity
    foreach ($value in $Values) {
        $sum += $value
        if ($value -gt $maximum) {
            $maximum = $value
        }
    }

    return [ordered]@{
        name = $Name
        unit = $Unit
        average = $sum / $Values.Count
        p95 = Get-NearestRankPercentile -Values $Values -Percentile 0.95
        p99 = Get-NearestRankPercentile -Values $Values -Percentile 0.99
        max = $maximum
    }
}

if (-not (Test-Path -LiteralPath $InputDirectory -PathType Container)) {
    throw "Process sample input directory not found: $InputDirectory"
}

$resolvedInputDirectory = (Resolve-Path -LiteralPath $InputDirectory).Path
$candidateFiles = @(Get-ChildItem -LiteralPath $resolvedInputDirectory -File -Filter 'run-*.csv')
if ($candidateFiles.Count -eq 0) {
    throw 'No run-*.csv process sample files were found.'
}

$numberedRunFiles = [System.Collections.Generic.List[object]]::new()
foreach ($candidateFile in $candidateFiles) {
    if ($candidateFile.Name -notmatch '^run-(\d+)\.csv$') {
        throw "Unexpected process sample filename: $($candidateFile.Name)."
    }

    $runNumber = [int]$Matches[1]
    if ($runNumber -lt 1) {
        throw "Process sample run numbers must start at 1: $($candidateFile.Name)."
    }

    $numberedRunFiles.Add([pscustomobject]@{
            RunNumber = $runNumber
            File = $candidateFile
        })
}

$runFiles = @($numberedRunFiles | Sort-Object RunNumber)
for ($index = 0; $index -lt $runFiles.Count; $index++) {
    $expectedNumber = $index + 1
    $expectedName = 'run-{0:D2}.csv' -f $expectedNumber
    if ($runFiles[$index].RunNumber -ne $expectedNumber -or $runFiles[$index].File.Name -ne $expectedName) {
        throw "Process sample files must be contiguous and use sampler names run-01.csv..run-NN.csv; expected $expectedName, found $($runFiles[$index].File.Name)."
    }
}

if (Test-Path -LiteralPath $OutputPath) {
    throw "Summary output already exists: $OutputPath"
}

$outputParent = Split-Path -Parent $OutputPath
if ([string]::IsNullOrWhiteSpace($outputParent)) {
    $outputParent = (Get-Location).Path
}
elseif (-not (Test-Path -LiteralPath $outputParent -PathType Container)) {
    New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
}

$runSummaries = [System.Collections.Generic.List[object]]::new()

for ($runIndex = 0; $runIndex -lt $runFiles.Count; $runIndex++) {
    $runFile = $runFiles[$runIndex].File
    $firstLine = Get-Content -LiteralPath $runFile.FullName -TotalCount 1
    if ($firstLine -ne $expectedHeader) {
        throw "$($runFile.Name) has an unexpected CSV header."
    }

    $rows = @(Import-Csv -LiteralPath $runFile.FullName)
    if ($rows.Count -eq 0) {
        throw "$($runFile.Name) contains no samples."
    }

    $cpuValues = [System.Collections.Generic.List[double]]::new()
    $privateBytesValues = [System.Collections.Generic.List[double]]::new()
    $workingSetValues = [System.Collections.Generic.List[double]]::new()
    $handleValues = [System.Collections.Generic.List[double]]::new()
    $threadValues = [System.Collections.Generic.List[double]]::new()
    $previousElapsed = -1.0

    for ($rowIndex = 0; $rowIndex -lt $rows.Count; $rowIndex++) {
        $row = $rows[$rowIndex]
        $context = "$($runFile.Name) row $($rowIndex + 1)"

        $timestamp = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParseExact(
                $row.timestampUtc,
                'O',
                $culture,
                [Globalization.DateTimeStyles]::None,
                [ref]$timestamp) -or $timestamp.Offset -ne [TimeSpan]::Zero) {
            throw "$context timestampUtc must be round-trip UTC ISO-8601."
        }

        $elapsed = Parse-FiniteDouble -Value $row.elapsedMilliseconds -Context "$context elapsedMilliseconds"
        if ($elapsed -le $previousElapsed) {
            throw "$context elapsedMilliseconds must increase strictly within a run."
        }

        $cpuValues.Add((Parse-FiniteDouble -Value $row.cpuPercent -Context "$context cpuPercent"))
        $privateBytesValues.Add((Parse-NonNegativeInteger -Value $row.privateBytes -Context "$context privateBytes"))
        $workingSetValues.Add((Parse-NonNegativeInteger -Value $row.workingSetBytes -Context "$context workingSetBytes"))
        $handleValues.Add((Parse-NonNegativeInteger -Value $row.handleCount -Context "$context handleCount"))
        $threadValues.Add((Parse-NonNegativeInteger -Value $row.threadCount -Context "$context threadCount"))
        $previousElapsed = $elapsed
    }

    $runSummaries.Add([ordered]@{
            run = $runIndex + 1
            rawResultFile = $runFile.Name
            sampleCount = $rows.Count
            metrics = @(
                (New-MetricSummary -Name 'cpuPercent' -Unit 'percent' -Values $cpuValues.ToArray()),
                (New-MetricSummary -Name 'privateBytes' -Unit 'bytes' -Values $privateBytesValues.ToArray()),
                (New-MetricSummary -Name 'workingSetBytes' -Unit 'bytes' -Values $workingSetValues.ToArray()),
                (New-MetricSummary -Name 'handleCount' -Unit 'count' -Values $handleValues.ToArray()),
                (New-MetricSummary -Name 'threadCount' -Unit 'count' -Values $threadValues.ToArray())
            )
        })
}

$summary = [ordered]@{
    summarySchemaVersion = 1
    source = 'zhuomian-process-sampler'
    percentileMethod = 'nearest-rank'
    runCount = $runSummaries.Count
    runs = $runSummaries.ToArray()
}

$summary | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $OutputPath -Encoding utf8
$summary
