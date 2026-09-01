[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SummaryPath,

    [Parameter(Mandatory)]
    [string]$SelectionPath,

    [Parameter(Mandatory)]
    [string]$MetadataPath,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RequiredProperty {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Context
    )

    if ($Object -is [System.Collections.IDictionary]) {
        if (-not $Object.Contains($Name)) {
            throw "$Context.$Name is required."
        }

        return $Object[$Name]
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "$Context.$Name is required."
    }

    return $property.Value
}

function Get-RequiredString {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Context
    )

    $value = Get-RequiredProperty -Object $Object -Name $Name -Context $Context
    if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) {
        throw "$Context.$Name must be a non-empty string."
    }

    return $value
}

function Get-RequiredNumber {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Context,
        [double]$Minimum = 0
    )

    $value = Get-RequiredProperty -Object $Object -Name $Name -Context $Context
    if ($value -isnot [ValueType] -or $value -is [bool]) {
        throw "$Context.$Name must be numeric."
    }

    try {
        $number = [double]$value
    }
    catch {
        throw "$Context.$Name must be numeric."
    }

    if ([double]::IsNaN($number) -or [double]::IsInfinity($number) -or $number -lt $Minimum) {
        throw "$Context.$Name must be a finite number >= $Minimum."
    }

    return $number
}

function Get-RequiredInteger {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Context,
        [long]$Minimum = 0
    )

    $number = Get-RequiredNumber -Object $Object -Name $Name -Context $Context -Minimum $Minimum
    if ([math]::Floor($number) -ne $number) {
        throw "$Context.$Name must be an integer."
    }

    return [long]$number
}

function Test-ContainsPrivatePath {
    param([Parameter(Mandatory)][string]$Value)

    return $Value -match '(?i)([A-Z]:\\Users\\[^\\]+|/home/[^/]+|/Users/[^/]+)'
}

function Assert-SafeRelativePath {
    param(
        [Parameter(Mandatory)][string]$Value,
        [Parameter(Mandatory)][string]$Context
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "$Context must be a non-empty relative path."
    }

    if ([IO.Path]::IsPathRooted($Value) -or $Value -match '^[A-Za-z]:' -or $Value -match '(^|[\\/])\.\.([\\/]|$)') {
        throw "$Context must be an evidence-relative path without traversal."
    }

    if (Test-ContainsPrivatePath -Value $Value) {
        throw "$Context must not contain a durable user-home path."
    }
}

function Resolve-ExistingFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description not found: $Path"
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Description
    )

    try {
        return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)
    }
    catch {
        throw "$Description is not valid JSON: $($_.Exception.Message)"
    }
}

function Get-SummaryFingerprint {
    param([Parameter(Mandatory)]$Summary)

    $runs = @(Get-RequiredProperty -Object $Summary -Name 'runs' -Context 'summary')
    $normalizedRuns = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $runs.Count; $index++) {
        $context = "summary.runs[$index]"
        $metrics = @(Get-RequiredProperty -Object $runs[$index] -Name 'metrics' -Context $context)
        $normalizedMetrics = [System.Collections.Generic.List[object]]::new()
        foreach ($metric in $metrics) {
            $metricContext = "$context.metrics"
            $normalizedMetrics.Add([ordered]@{
                    name = Get-RequiredString -Object $metric -Name 'name' -Context $metricContext
                    unit = Get-RequiredString -Object $metric -Name 'unit' -Context $metricContext
                    average = Get-RequiredNumber -Object $metric -Name 'average' -Context $metricContext
                    p95 = Get-RequiredNumber -Object $metric -Name 'p95' -Context $metricContext
                    p99 = Get-RequiredNumber -Object $metric -Name 'p99' -Context $metricContext
                    max = Get-RequiredNumber -Object $metric -Name 'max' -Context $metricContext
                })
        }

        $normalizedRuns.Add([ordered]@{
                run = Get-RequiredInteger -Object $runs[$index] -Name 'run' -Context $context -Minimum 1
                rawResultFile = Get-RequiredString -Object $runs[$index] -Name 'rawResultFile' -Context $context
                sampleCount = Get-RequiredInteger -Object $runs[$index] -Name 'sampleCount' -Context $context -Minimum 1
                metrics = $normalizedMetrics.ToArray()
            })
    }

    return [ordered]@{
        summarySchemaVersion = Get-RequiredInteger -Object $Summary -Name 'summarySchemaVersion' -Context 'summary' -Minimum 1
        source = Get-RequiredString -Object $Summary -Name 'source' -Context 'summary'
        percentileMethod = Get-RequiredString -Object $Summary -Name 'percentileMethod' -Context 'summary'
        runCount = Get-RequiredInteger -Object $Summary -Name 'runCount' -Context 'summary' -Minimum 1
        runs = $normalizedRuns.ToArray()
    }
}

function Get-SelectionFingerprint {
    param([Parameter(Mandatory)]$Selection)

    $selector = Get-RequiredProperty -Object $Selection -Name 'selector' -Context 'selection'
    $rankedRuns = @(Get-RequiredProperty -Object $Selection -Name 'rankedRuns' -Context 'selection')
    $normalizedRanking = [System.Collections.Generic.List[object]]::new()
    foreach ($rankedRun in $rankedRuns) {
        $normalizedRanking.Add([ordered]@{
                severityRank = Get-RequiredInteger -Object $rankedRun -Name 'severityRank' -Context 'selection.rankedRuns' -Minimum 1
                run = Get-RequiredInteger -Object $rankedRun -Name 'run' -Context 'selection.rankedRuns' -Minimum 1
                value = Get-RequiredNumber -Object $rankedRun -Name 'value' -Context 'selection.rankedRuns'
            })
    }

    return [ordered]@{
        selectionSchemaVersion = Get-RequiredInteger -Object $Selection -Name 'selectionSchemaVersion' -Context 'selection' -Minimum 1
        sourceSummarySchemaVersion = Get-RequiredInteger -Object $Selection -Name 'sourceSummarySchemaVersion' -Context 'selection' -Minimum 1
        sourceSummaryFile = Get-RequiredString -Object $Selection -Name 'sourceSummaryFile' -Context 'selection'
        runCount = Get-RequiredInteger -Object $Selection -Name 'runCount' -Context 'selection' -Minimum 1
        selector = [ordered]@{
            metricName = Get-RequiredString -Object $selector -Name 'metricName' -Context 'selection.selector'
            unit = Get-RequiredString -Object $selector -Name 'unit' -Context 'selection.selector'
            statistic = Get-RequiredString -Object $selector -Name 'statistic' -Context 'selection.selector'
            direction = Get-RequiredString -Object $selector -Name 'direction' -Context 'selection.selector'
            medianMethod = Get-RequiredString -Object $selector -Name 'medianMethod' -Context 'selection.selector'
            tieBreak = Get-RequiredString -Object $selector -Name 'tieBreak' -Context 'selection.selector'
        }
        medianRun = Get-RequiredInteger -Object $Selection -Name 'medianRun' -Context 'selection' -Minimum 1
        medianValue = Get-RequiredNumber -Object $Selection -Name 'medianValue' -Context 'selection'
        worstRun = Get-RequiredInteger -Object $Selection -Name 'worstRun' -Context 'selection' -Minimum 1
        worstValue = Get-RequiredNumber -Object $Selection -Name 'worstValue' -Context 'selection'
        rankedRuns = $normalizedRanking.ToArray()
    }
}

function Get-RecomputedContracts {
    param(
        [Parameter(Mandatory)][string]$SummaryPath,
        [Parameter(Mandatory)][object[]]$RawArtifacts,
        [Parameter(Mandatory)][string]$MetricName,
        [Parameter(Mandatory)][string]$Statistic,
        [Parameter(Mandatory)][string]$Direction
    )

    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("zhuomian-perf-assembly-verify-{0}" -f [guid]::NewGuid())
    $tempRawDirectory = Join-Path $tempRoot 'raw'
    New-Item -ItemType Directory -Path $tempRawDirectory -Force | Out-Null

    try {
        foreach ($artifact in $RawArtifacts) {
            $destination = Join-Path $tempRawDirectory $artifact.RelativePath
            $destinationParent = Split-Path -Parent $destination
            if (-not (Test-Path -LiteralPath $destinationParent -PathType Container)) {
                New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
            }

            Copy-Item -LiteralPath $artifact.FullPath -Destination $destination
        }

        $tempSummaryPath = Join-Path $tempRawDirectory ([IO.Path]::GetFileName($SummaryPath))
        $summarizer = Join-Path $PSScriptRoot 'summarize-process-samples.ps1'
        & $summarizer -InputDirectory $tempRawDirectory -OutputPath $tempSummaryPath | Out-Null
        $recomputedSummary = Read-JsonFile -Path $tempSummaryPath -Description 'Recomputed per-run summary'

        $tempSelectionPath = Join-Path $tempRoot 'selection.json'
        $selector = Join-Path $PSScriptRoot 'select-performance-runs.ps1'
        & $selector `
            -SummaryPath $tempSummaryPath `
            -OutputPath $tempSelectionPath `
            -MetricName $MetricName `
            -Statistic $Statistic `
            -Direction $Direction | Out-Null
        $recomputedSelection = Read-JsonFile -Path $tempSelectionPath -Description 'Recomputed run-selection record'

        return [pscustomobject]@{
            Summary = $recomputedSummary
            Selection = $recomputedSelection
        }
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$resolvedSummaryPath = Resolve-ExistingFile -Path $SummaryPath -Description 'Per-run summary'
$resolvedSelectionPath = Resolve-ExistingFile -Path $SelectionPath -Description 'Run-selection record'
$resolvedMetadataPath = Resolve-ExistingFile -Path $MetadataPath -Description 'Evidence metadata'

$outputFullPath = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $outputFullPath) {
    throw "Evidence output already exists: $OutputPath"
}

$outputDirectory = Split-Path -Parent $outputFullPath
if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
    $outputDirectory = (Get-Location).Path
    $outputFullPath = Join-Path $outputDirectory ([IO.Path]::GetFileName($outputFullPath))
}
elseif (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$summary = Read-JsonFile -Path $resolvedSummaryPath -Description 'Per-run summary'
$selection = Read-JsonFile -Path $resolvedSelectionPath -Description 'Run-selection record'
$metadata = Read-JsonFile -Path $resolvedMetadataPath -Description 'Evidence metadata'

$metadataSchemaProperty = $metadata.PSObject.Properties['schemaVersion']
if ($null -ne $metadataSchemaProperty) {
    $metadataSchema = Get-RequiredInteger -Object $metadata -Name 'schemaVersion' -Context 'metadata' -Minimum 1
    if ($metadataSchema -ne 1) {
        throw 'metadata.schemaVersion must be 1.'
    }
}

foreach ($ownedField in @('rawResultFiles', 'metrics', 'runSelection')) {
    if ($null -ne $metadata.PSObject.Properties[$ownedField]) {
        throw "metadata.$ownedField is assembler-owned and must not be supplied."
    }
}

$evidence = [ordered]@{ schemaVersion = 1 }
foreach ($property in $metadata.PSObject.Properties) {
    if ($property.Name -ne 'schemaVersion') {
        $evidence[$property.Name] = $property.Value
    }
}

$resolvedSummaryDirectory = Split-Path -Parent $resolvedSummaryPath
$summarySchemaVersion = Get-RequiredInteger -Object $summary -Name 'summarySchemaVersion' -Context 'summary' -Minimum 1
if ($summarySchemaVersion -ne 1) {
    throw 'summary.summarySchemaVersion must be 1.'
}

$summarySource = Get-RequiredString -Object $summary -Name 'source' -Context 'summary'
if ($summarySource -ne 'zhuomian-process-sampler') {
    throw 'summary.source must be zhuomian-process-sampler.'
}

$summaryRunCount = Get-RequiredInteger -Object $summary -Name 'runCount' -Context 'summary' -Minimum 1
$summaryRuns = @(Get-RequiredProperty -Object $summary -Name 'runs' -Context 'summary')
if ($summaryRuns.Count -ne $summaryRunCount) {
    throw 'summary.runCount must equal summary.runs count.'
}

$rawResultFiles = [System.Collections.Generic.List[string]]::new()
$rawArtifacts = [System.Collections.Generic.List[object]]::new()
$summaryRunByNumber = @{}
for ($index = 0; $index -lt $summaryRuns.Count; $index++) {
    $context = "summary.runs[$index]"
    $runNumber = Get-RequiredInteger -Object $summaryRuns[$index] -Name 'run' -Context $context -Minimum 1
    if ($runNumber -ne ($index + 1)) {
        throw "summary.runs must be contiguous and ordered from run 1; expected $($index + 1), found $runNumber."
    }

    $rawResultFile = Get-RequiredString -Object $summaryRuns[$index] -Name 'rawResultFile' -Context $context
    Assert-SafeRelativePath -Value $rawResultFile -Context "$context.rawResultFile"

    $rawPath = [IO.Path]::GetFullPath((Join-Path $resolvedSummaryDirectory $rawResultFile))
    if (-not (Test-Path -LiteralPath $rawPath -PathType Leaf)) {
        throw "Raw performance result file is missing: $rawResultFile"
    }

    $outputRelativeRawPath = [IO.Path]::GetRelativePath($outputDirectory, $rawPath).Replace('\', '/')
    Assert-SafeRelativePath -Value $outputRelativeRawPath -Context "$context.rawResultFile"
    if ($rawResultFiles -contains $outputRelativeRawPath) {
        throw "summary.runs rawResultFile entries must be unique: $outputRelativeRawPath"
    }

    $rawResultFiles.Add($outputRelativeRawPath)
    $rawArtifacts.Add([pscustomobject]@{
            RelativePath = $rawResultFile
            FullPath = $rawPath
        })
    $summaryRunByNumber[$runNumber] = $summaryRuns[$index]
}

$selectionSchemaVersion = Get-RequiredInteger -Object $selection -Name 'selectionSchemaVersion' -Context 'selection' -Minimum 1
if ($selectionSchemaVersion -ne 1) {
    throw 'selection.selectionSchemaVersion must be 1.'
}

$selectionSourceSummarySchemaVersion = Get-RequiredInteger -Object $selection -Name 'sourceSummarySchemaVersion' -Context 'selection' -Minimum 1
if ($selectionSourceSummarySchemaVersion -ne $summarySchemaVersion) {
    throw 'selection.sourceSummarySchemaVersion must match summary.summarySchemaVersion.'
}

$selectionSourceSummaryFile = Get-RequiredString -Object $selection -Name 'sourceSummaryFile' -Context 'selection'
if ($selectionSourceSummaryFile -ne [IO.Path]::GetFileName($resolvedSummaryPath)) {
    throw 'selection.sourceSummaryFile must match the supplied summary filename.'
}

$selectionRunCount = Get-RequiredInteger -Object $selection -Name 'runCount' -Context 'selection' -Minimum 1
if ($selectionRunCount -ne $summaryRunCount) {
    throw 'selection.runCount must match summary.runCount.'
}

$protocol = Get-RequiredProperty -Object $evidence -Name 'protocol' -Context 'evidence'
$protocolRepetitions = Get-RequiredInteger -Object $protocol -Name 'repetitions' -Context 'evidence.protocol' -Minimum 1
if ($protocolRepetitions -ne $summaryRunCount) {
    throw 'evidence.protocol.repetitions must match summary and selection run counts.'
}

$selector = Get-RequiredProperty -Object $selection -Name 'selector' -Context 'selection'
$selectorMetricName = Get-RequiredString -Object $selector -Name 'metricName' -Context 'selection.selector'
$selectorUnit = Get-RequiredString -Object $selector -Name 'unit' -Context 'selection.selector'
$selectorStatistic = Get-RequiredString -Object $selector -Name 'statistic' -Context 'selection.selector'
if ($selectorStatistic -notin @('average', 'p95', 'p99', 'max')) {
    throw 'selection.selector.statistic must be average, p95, p99, or max.'
}

$selectorDirection = Get-RequiredString -Object $selector -Name 'direction' -Context 'selection.selector'
if ($selectorDirection -notin @('higher-is-worse', 'lower-is-worse')) {
    throw 'selection.selector.direction must be higher-is-worse or lower-is-worse.'
}

$medianMethod = Get-RequiredString -Object $selector -Name 'medianMethod' -Context 'selection.selector'
if ($medianMethod -ne 'nearest-rank-50') {
    throw 'selection.selector.medianMethod must be nearest-rank-50.'
}

$tieBreak = Get-RequiredString -Object $selector -Name 'tieBreak' -Context 'selection.selector'
if ($tieBreak -ne 'lowest-run-number') {
    throw 'selection.selector.tieBreak must be lowest-run-number.'
}

$medianRun = Get-RequiredInteger -Object $selection -Name 'medianRun' -Context 'selection' -Minimum 1
$medianValue = Get-RequiredNumber -Object $selection -Name 'medianValue' -Context 'selection'
$worstRun = Get-RequiredInteger -Object $selection -Name 'worstRun' -Context 'selection' -Minimum 1
$worstValue = Get-RequiredNumber -Object $selection -Name 'worstValue' -Context 'selection'
if (-not $summaryRunByNumber.ContainsKey($medianRun) -or -not $summaryRunByNumber.ContainsKey($worstRun)) {
    throw 'selection medianRun and worstRun must refer to runs present in the summary.'
}

$recomputedContracts = Get-RecomputedContracts `
    -SummaryPath $resolvedSummaryPath `
    -RawArtifacts $rawArtifacts.ToArray() `
    -MetricName $selectorMetricName `
    -Statistic $selectorStatistic `
    -Direction $selectorDirection
$summaryFingerprint = Get-SummaryFingerprint -Summary $summary | ConvertTo-Json -Depth 30 -Compress
$recomputedSummaryFingerprint = Get-SummaryFingerprint -Summary $recomputedContracts.Summary | ConvertTo-Json -Depth 30 -Compress
if ($summaryFingerprint -ne $recomputedSummaryFingerprint) {
    throw 'Supplied summary does not match the current raw performance result files.'
}

$selectionFingerprint = Get-SelectionFingerprint -Selection $selection | ConvertTo-Json -Depth 30 -Compress
$recomputedSelectionFingerprint = Get-SelectionFingerprint -Selection $recomputedContracts.Selection | ConvertTo-Json -Depth 30 -Compress
if ($selectionFingerprint -ne $recomputedSelectionFingerprint) {
    throw 'Supplied run-selection record does not match deterministic selection from the supplied summary.'
}

$medianRunObject = $summaryRunByNumber[$medianRun]
$worstRunObject = $summaryRunByNumber[$worstRun]
$medianMetrics = @(Get-RequiredProperty -Object $medianRunObject -Name 'metrics' -Context "summary.runs[$($medianRun - 1)]")
if ($medianMetrics.Count -eq 0) {
    throw 'The selected median summary run must contain metrics.'
}

$medianMetricMatches = @($medianMetrics | Where-Object {
        $nameProperty = $_.PSObject.Properties['name']
        $null -ne $nameProperty -and $nameProperty.Value -eq $selectorMetricName
    })
if ($medianMetricMatches.Count -ne 1) {
    throw "The selected median summary run must contain exactly one metric named '$selectorMetricName'."
}

$medianMetric = $medianMetricMatches[0]
$medianMetricUnit = Get-RequiredString -Object $medianMetric -Name 'unit' -Context "summary median metric '$selectorMetricName'"
if ($medianMetricUnit -ne $selectorUnit) {
    throw 'selection.selector.unit must match the selected summary metric unit.'
}

$medianMetricValue = Get-RequiredNumber -Object $medianMetric -Name $selectorStatistic -Context "summary median metric '$selectorMetricName'"
if ([Math]::Abs($medianMetricValue - $medianValue) -gt 0.000000001) {
    throw 'selection.medianValue must match the selected median summary metric.'
}

$worstMetrics = @(Get-RequiredProperty -Object $worstRunObject -Name 'metrics' -Context "summary.runs[$($worstRun - 1)]")
$worstMetricMatches = @($worstMetrics | Where-Object {
        $nameProperty = $_.PSObject.Properties['name']
        $null -ne $nameProperty -and $nameProperty.Value -eq $selectorMetricName
    })
if ($worstMetricMatches.Count -ne 1) {
    throw "The selected worst summary run must contain exactly one metric named '$selectorMetricName'."
}

$worstMetricValue = Get-RequiredNumber -Object $worstMetricMatches[0] -Name $selectorStatistic -Context "summary worst metric '$selectorMetricName'"
if ([Math]::Abs($worstMetricValue - $worstValue) -gt 0.000000001) {
    throw 'selection.worstValue must match the selected worst summary metric.'
}

$evidence['rawResultFiles'] = $rawResultFiles.ToArray()
$evidence['metrics'] = $medianMetrics
$evidence['runSelection'] = [ordered]@{
    selector = [ordered]@{
        metricName = $selectorMetricName
        unit = $selectorUnit
        statistic = $selectorStatistic
        direction = $selectorDirection
        medianMethod = $medianMethod
        tieBreak = $tieBreak
    }
    medianRun = $medianRun
    medianValue = $medianValue
    worstRun = $worstRun
    worstValue = $worstValue
}

$validator = Join-Path $PSScriptRoot 'validate-performance-evidence.ps1'
if (-not (Test-Path -LiteralPath $validator -PathType Leaf)) {
    throw "Evidence validator not found: $validator"
}

$temporaryOutputPath = Join-Path $outputDirectory ('.performance-evidence-{0}.json' -f [IO.Path]::GetRandomFileName())
try {
    $evidence | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $temporaryOutputPath -Encoding utf8
    & $validator -Path $temporaryOutputPath | Out-Null
    Move-Item -LiteralPath $temporaryOutputPath -Destination $outputFullPath
}
catch {
    Remove-Item -LiteralPath $temporaryOutputPath -Force -ErrorAction SilentlyContinue
    throw "Assembled performance evidence failed canonical validation: $($_.Exception.Message)"
}

[pscustomobject]@{
    Valid = $true
    Path = $outputFullPath
    RawResultFiles = $rawResultFiles.ToArray()
    MedianRun = $medianRun
    WorstRun = $worstRun
    SelectorMetric = $selectorMetricName
}
