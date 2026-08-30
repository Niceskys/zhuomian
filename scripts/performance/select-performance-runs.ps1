[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SummaryPath,

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [Parameter(Mandatory)]
    [string]$MetricName,

    [Parameter(Mandatory)]
    [ValidateSet('average', 'p95', 'p99', 'max')]
    [string]$Statistic,

    [Parameter(Mandatory)]
    [ValidateSet('higher-is-worse', 'lower-is-worse')]
    [string]$Direction
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RequiredProperty {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Context
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "$Context.$Name is required."
    }

    return $property.Value
}

function Get-RequiredPositiveInteger {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Context
    )

    $value = Get-RequiredProperty -Object $Object -Name $Name -Context $Context
    $number = 0L
    if (-not [long]::TryParse(
            [string]$value,
            [Globalization.NumberStyles]::Integer,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$number) -or $number -lt 1) {
        throw "$Context.$Name must be a positive integer."
    }

    return $number
}

function Get-FiniteNonNegativeNumber {
    param(
        [Parameter(Mandatory)]$Value,
        [Parameter(Mandatory)][string]$Context
    )

    if ($Value -isnot [ValueType] -or $Value -is [bool]) {
        throw "$Context must be numeric."
    }

    $number = [double]$Value
    if ([double]::IsNaN($number) -or [double]::IsInfinity($number) -or $number -lt 0) {
        throw "$Context must be a finite number >= 0."
    }

    return $number
}

if (-not (Test-Path -LiteralPath $SummaryPath -PathType Leaf)) {
    throw "Per-run summary not found: $SummaryPath"
}

if (Test-Path -LiteralPath $OutputPath) {
    throw "Run-selection output already exists: $OutputPath"
}

if ([string]::IsNullOrWhiteSpace($MetricName)) {
    throw 'MetricName must be non-empty.'
}

try {
    $summary = Get-Content -LiteralPath $SummaryPath -Raw | ConvertFrom-Json
}
catch {
    throw "Per-run summary is not valid JSON: $($_.Exception.Message)"
}

$schemaVersion = Get-RequiredPositiveInteger -Object $summary -Name 'summarySchemaVersion' -Context 'summary'
if ($schemaVersion -ne 1) {
    throw 'summary.summarySchemaVersion must be 1.'
}

$source = Get-RequiredProperty -Object $summary -Name 'source' -Context 'summary'
if ($source -ne 'zhuomian-process-sampler') {
    throw "summary.source must be zhuomian-process-sampler."
}

$percentileMethod = Get-RequiredProperty -Object $summary -Name 'percentileMethod' -Context 'summary'
if ($percentileMethod -ne 'nearest-rank') {
    throw 'summary.percentileMethod must be nearest-rank.'
}

$runCount = Get-RequiredPositiveInteger -Object $summary -Name 'runCount' -Context 'summary'
$runs = @(Get-RequiredProperty -Object $summary -Name 'runs' -Context 'summary')
if ($runs.Count -ne $runCount) {
    throw "summary.runCount must equal summary.runs count."
}

$candidates = [System.Collections.Generic.List[object]]::new()
$selectedUnit = $null
for ($index = 0; $index -lt $runs.Count; $index++) {
    $context = "summary.runs[$index]"
    $runNumber = Get-RequiredPositiveInteger -Object $runs[$index] -Name 'run' -Context $context
    $expectedRun = $index + 1
    if ($runNumber -ne $expectedRun) {
        throw "summary.runs must be contiguous and ordered from run 1; expected $expectedRun, found $runNumber."
    }

    Get-RequiredPositiveInteger -Object $runs[$index] -Name 'sampleCount' -Context $context | Out-Null
    $rawResultFile = Get-RequiredProperty -Object $runs[$index] -Name 'rawResultFile' -Context $context
    if ($rawResultFile -isnot [string] -or [string]::IsNullOrWhiteSpace($rawResultFile)) {
        throw "$context.rawResultFile must be a non-empty string."
    }

    $metrics = @(Get-RequiredProperty -Object $runs[$index] -Name 'metrics' -Context $context)
    $matches = @($metrics | Where-Object {
            $nameProperty = $_.PSObject.Properties['name']
            $null -ne $nameProperty -and $nameProperty.Value -eq $MetricName
        })
    if ($matches.Count -ne 1) {
        throw "$context.metrics must contain exactly one metric named '$MetricName'."
    }

    $metric = $matches[0]
    $unit = Get-RequiredProperty -Object $metric -Name 'unit' -Context "$context.metrics[$MetricName]"
    if ($unit -isnot [string] -or [string]::IsNullOrWhiteSpace($unit)) {
        throw "$context.metrics[$MetricName].unit must be a non-empty string."
    }

    if ($null -eq $selectedUnit) {
        $selectedUnit = $unit
    }
    elseif ($unit -ne $selectedUnit) {
        throw "Metric '$MetricName' must use the same unit in every run."
    }

    $value = Get-FiniteNonNegativeNumber `
        -Value (Get-RequiredProperty -Object $metric -Name $Statistic -Context "$context.metrics[$MetricName]") `
        -Context "$context.metrics[$MetricName].$Statistic"
    $candidates.Add([pscustomobject]@{
            Run = $runNumber
            Value = $value
        })
}

if ($Direction -eq 'higher-is-worse') {
    $ranked = @($candidates | Sort-Object @{ Expression = 'Value'; Ascending = $true }, @{ Expression = 'Run'; Ascending = $true })
    $worst = @($candidates | Sort-Object @{ Expression = 'Value'; Descending = $true }, @{ Expression = 'Run'; Ascending = $true })[0]
}
else {
    $ranked = @($candidates | Sort-Object @{ Expression = 'Value'; Descending = $true }, @{ Expression = 'Run'; Ascending = $true })
    $worst = @($candidates | Sort-Object @{ Expression = 'Value'; Ascending = $true }, @{ Expression = 'Run'; Ascending = $true })[0]
}

$medianRank = [int][Math]::Ceiling($ranked.Count * 0.5)
$median = $ranked[$medianRank - 1]
$rankedRuns = for ($index = 0; $index -lt $ranked.Count; $index++) {
    [ordered]@{
        severityRank = $index + 1
        run = $ranked[$index].Run
        value = $ranked[$index].Value
    }
}

$selection = [ordered]@{
    selectionSchemaVersion = 1
    sourceSummarySchemaVersion = $schemaVersion
    sourceSummaryFile = [IO.Path]::GetFileName($SummaryPath)
    runCount = $runCount
    selector = [ordered]@{
        metricName = $MetricName
        unit = $selectedUnit
        statistic = $Statistic
        direction = $Direction
        medianMethod = 'nearest-rank-50'
        tieBreak = 'lowest-run-number'
    }
    medianRun = $median.Run
    medianValue = $median.Value
    worstRun = $worst.Run
    worstValue = $worst.Value
    rankedRuns = @($rankedRuns)
}

$outputParent = Split-Path -Parent $OutputPath
if ([string]::IsNullOrWhiteSpace($outputParent)) {
    $outputParent = (Get-Location).Path
}
elseif (-not (Test-Path -LiteralPath $outputParent -PathType Container)) {
    New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
}

$selection | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $OutputPath -Encoding utf8
$selection
