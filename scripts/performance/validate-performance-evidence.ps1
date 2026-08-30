[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Path
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

function Get-RequiredBoolean {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Context
    )

    $value = Get-RequiredProperty -Object $Object -Name $Name -Context $Context
    if ($value -isnot [bool]) {
        throw "$Context.$Name must be a boolean."
    }

    return $value
}

function Get-RequiredArray {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Context
    )

    $value = Get-RequiredProperty -Object $Object -Name $Name -Context $Context
    $items = @($value)
    if ($items.Count -eq 0) {
        throw "$Context.$Name must contain at least one item."
    }

    return $items
}

function Test-ContainsPrivatePath {
    param([Parameter(Mandatory)][string]$Value)

    return $Value -match '(?i)([A-Z]:\\Users\\[^\\]+|/home/[^/]+|/Users/[^/]+)'
}

if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "Performance evidence file not found: $Path"
}

$resolvedEvidencePath = (Resolve-Path -LiteralPath $Path).Path
$evidenceDirectory = Split-Path -Parent $resolvedEvidencePath
$rawJson = Get-Content -LiteralPath $resolvedEvidencePath -Raw

$jsonDocument = $null
try {
    $jsonDocument = [System.Text.Json.JsonDocument]::Parse($rawJson)
    $timestampElement = $jsonDocument.RootElement.GetProperty('collectedAtUtc')
    if ($timestampElement.ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
        throw "evidence.collectedAtUtc must be a non-empty UTC string."
    }

    $collectedAtUtc = $timestampElement.GetString()
    if ([string]::IsNullOrWhiteSpace($collectedAtUtc)) {
        throw "evidence.collectedAtUtc must be a non-empty UTC string."
    }

    if ($collectedAtUtc -notmatch '(Z|\+00:00)$') {
        throw "evidence.collectedAtUtc must use UTC offset Z or +00:00."
    }

    $parsedTimestamp = [DateTimeOffset]::MinValue
    if (-not $timestampElement.TryGetDateTimeOffset([ref]$parsedTimestamp)) {
        throw "evidence.collectedAtUtc must use the extended ISO-8601 date/time profile accepted by System.Text.Json."
    }

    if ($parsedTimestamp.Offset -ne [TimeSpan]::Zero) {
        throw "evidence.collectedAtUtc must resolve to UTC offset zero."
    }
}
catch {
    if ($_.Exception.Message -like 'evidence.collectedAtUtc*') {
        throw
    }

    throw "Performance evidence is not valid JSON or is missing collectedAtUtc: $($_.Exception.Message)"
}
finally {
    if ($null -ne $jsonDocument) {
        $jsonDocument.Dispose()
    }
}

try {
    $evidence = $rawJson | ConvertFrom-Json
}
catch {
    throw "Performance evidence is not valid JSON: $($_.Exception.Message)"
}

$schemaVersion = Get-RequiredInteger -Object $evidence -Name 'schemaVersion' -Context 'evidence' -Minimum 1
if ($schemaVersion -ne 1) {
    throw "evidence.schemaVersion must be 1."
}

$commitSha = Get-RequiredString -Object $evidence -Name 'commitSha' -Context 'evidence'
if ($commitSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw "evidence.commitSha must be a full 40-character Git SHA."
}

$scenarioId = Get-RequiredString -Object $evidence -Name 'scenarioId' -Context 'evidence'
if ($scenarioId -notmatch '^S[1-8]$') {
    throw "evidence.scenarioId must be one of S1 through S8."
}

$machineTier = Get-RequiredString -Object $evidence -Name 'machineTier' -Context 'evidence'
$allowedMachineTiers = @('Baseline', 'Enhanced', 'Exploratory', 'CI')
if ($machineTier -notin $allowedMachineTiers) {
    throw "evidence.machineTier must be Baseline, Enhanced, Exploratory, or CI."
}

$eligibleForThresholdCalibration = Get-RequiredBoolean -Object $evidence -Name 'eligibleForThresholdCalibration' -Context 'evidence'
if ($eligibleForThresholdCalibration -and $machineTier -notin @('Baseline', 'Enhanced')) {
    throw "Only Baseline or Enhanced real-machine evidence may be eligible for threshold calibration."
}

$environment = Get-RequiredProperty -Object $evidence -Name 'environment' -Context 'evidence'
Get-RequiredString -Object $environment -Name 'windowsBuild' -Context 'evidence.environment' | Out-Null
Get-RequiredString -Object $environment -Name 'windowsAppSdkVersion' -Context 'evidence.environment' | Out-Null
Get-RequiredString -Object $environment -Name 'gpuDriver' -Context 'evidence.environment' | Out-Null
Get-RequiredString -Object $environment -Name 'cpu' -Context 'evidence.environment' | Out-Null
Get-RequiredNumber -Object $environment -Name 'memoryGb' -Context 'evidence.environment' -Minimum 0.1 | Out-Null
Get-RequiredString -Object $environment -Name 'gpu' -Context 'evidence.environment' | Out-Null

$displays = @(Get-RequiredArray -Object $environment -Name 'displays' -Context 'evidence.environment')
for ($index = 0; $index -lt $displays.Count; $index++) {
    Get-RequiredNumber -Object $displays[$index] -Name 'dpi' -Context "evidence.environment.displays[$index]" -Minimum 1 | Out-Null
    Get-RequiredNumber -Object $displays[$index] -Name 'refreshHz' -Context "evidence.environment.displays[$index]" -Minimum 1 | Out-Null
}

$build = Get-RequiredProperty -Object $evidence -Name 'build' -Context 'evidence'
$configuration = Get-RequiredString -Object $build -Name 'configuration' -Context 'evidence.build'
if ($configuration -ne 'Release') {
    throw "evidence.build.configuration must be Release."
}

$architecture = Get-RequiredString -Object $build -Name 'architecture' -Context 'evidence.build'
if ($architecture -ne 'x64') {
    throw "evidence.build.architecture must be x64."
}

Get-RequiredString -Object $build -Name 'packagingMode' -Context 'evidence.build' | Out-Null

$protocol = Get-RequiredProperty -Object $evidence -Name 'protocol' -Context 'evidence'
$warmupSeconds = Get-RequiredInteger -Object $protocol -Name 'warmupSeconds' -Context 'evidence.protocol' -Minimum 0
$measurementSeconds = Get-RequiredInteger -Object $protocol -Name 'measurementSeconds' -Context 'evidence.protocol' -Minimum 1
$repetitions = Get-RequiredInteger -Object $protocol -Name 'repetitions' -Context 'evidence.protocol' -Minimum 1
Get-RequiredString -Object $protocol -Name 'condition' -Context 'evidence.protocol' | Out-Null

$deviationReason = $null
$deviationProperty = $protocol.PSObject.Properties['deviationReason']
if ($null -ne $deviationProperty -and $null -ne $deviationProperty.Value) {
    if ($deviationProperty.Value -isnot [string]) {
        throw "evidence.protocol.deviationReason must be a string or null."
    }

    $deviationReason = $deviationProperty.Value
}

$usesDefaultProtocol = $warmupSeconds -eq 60 -and $measurementSeconds -eq 300 -and $repetitions -eq 3
if (-not $usesDefaultProtocol -and [string]::IsNullOrWhiteSpace($deviationReason)) {
    throw "Non-default warm-up, measurement duration, or repetition count requires evidence.protocol.deviationReason."
}

$collectionCommand = Get-RequiredString -Object $evidence -Name 'collectionCommand' -Context 'evidence'
if (Test-ContainsPrivatePath -Value $collectionCommand) {
    throw "evidence.collectionCommand must not contain a durable user-home path."
}

$rawResultFiles = @(Get-RequiredArray -Object $evidence -Name 'rawResultFiles' -Context 'evidence')
foreach ($rawResultFile in $rawResultFiles) {
    if ($rawResultFile -isnot [string] -or [string]::IsNullOrWhiteSpace($rawResultFile)) {
        throw "evidence.rawResultFiles entries must be non-empty strings."
    }

    if ([IO.Path]::IsPathRooted($rawResultFile) -or $rawResultFile -match '(^|[\\/])\.\.([\\/]|$)') {
        throw "evidence.rawResultFiles entries must be evidence-relative paths without traversal."
    }

    if (Test-ContainsPrivatePath -Value $rawResultFile) {
        throw "evidence.rawResultFiles entries must not contain durable user-home paths."
    }

    $rawPath = Join-Path $evidenceDirectory $rawResultFile
    if (-not (Test-Path -LiteralPath $rawPath -PathType Leaf)) {
        throw "Raw performance result file is missing: $rawResultFile"
    }
}

$metrics = @(Get-RequiredArray -Object $evidence -Name 'metrics' -Context 'evidence')
for ($index = 0; $index -lt $metrics.Count; $index++) {
    $context = "evidence.metrics[$index]"
    Get-RequiredString -Object $metrics[$index] -Name 'name' -Context $context | Out-Null
    Get-RequiredString -Object $metrics[$index] -Name 'unit' -Context $context | Out-Null
    $average = Get-RequiredNumber -Object $metrics[$index] -Name 'average' -Context $context
    $p95 = Get-RequiredNumber -Object $metrics[$index] -Name 'p95' -Context $context
    $p99 = Get-RequiredNumber -Object $metrics[$index] -Name 'p99' -Context $context
    $maximum = Get-RequiredNumber -Object $metrics[$index] -Name 'max' -Context $context

    if ($p95 -gt $p99 -or $p99 -gt $maximum -or $average -gt $maximum) {
        throw "$context must satisfy p95 <= p99 <= max and average <= max."
    }
}

$framePresentation = Get-RequiredProperty -Object $evidence -Name 'framePresentation' -Context 'evidence'
$frameMeasured = Get-RequiredBoolean -Object $framePresentation -Name 'measured' -Context 'evidence.framePresentation'
if ($frameMeasured) {
    $droppedFrameRatio = Get-RequiredNumber -Object $framePresentation -Name 'droppedFrameRatio' -Context 'evidence.framePresentation'
    if ($droppedFrameRatio -gt 1) {
        throw "evidence.framePresentation.droppedFrameRatio must be between 0 and 1."
    }
}
else {
    Get-RequiredString -Object $framePresentation -Name 'notApplicableReason' -Context 'evidence.framePresentation' | Out-Null
}

$runSelection = Get-RequiredProperty -Object $evidence -Name 'runSelection' -Context 'evidence'
$selector = Get-RequiredProperty -Object $runSelection -Name 'selector' -Context 'evidence.runSelection'
$selectorMetricName = Get-RequiredString -Object $selector -Name 'metricName' -Context 'evidence.runSelection.selector'
$selectorUnit = Get-RequiredString -Object $selector -Name 'unit' -Context 'evidence.runSelection.selector'
$selectorStatistic = Get-RequiredString -Object $selector -Name 'statistic' -Context 'evidence.runSelection.selector'
if ($selectorStatistic -notin @('average', 'p95', 'p99', 'max')) {
    throw "evidence.runSelection.selector.statistic must be average, p95, p99, or max."
}

$selectorDirection = Get-RequiredString -Object $selector -Name 'direction' -Context 'evidence.runSelection.selector'
if ($selectorDirection -notin @('higher-is-worse', 'lower-is-worse')) {
    throw "evidence.runSelection.selector.direction must be higher-is-worse or lower-is-worse."
}

$medianMethod = Get-RequiredString -Object $selector -Name 'medianMethod' -Context 'evidence.runSelection.selector'
if ($medianMethod -ne 'nearest-rank-50') {
    throw "evidence.runSelection.selector.medianMethod must be nearest-rank-50."
}

$tieBreak = Get-RequiredString -Object $selector -Name 'tieBreak' -Context 'evidence.runSelection.selector'
if ($tieBreak -ne 'lowest-run-number') {
    throw "evidence.runSelection.selector.tieBreak must be lowest-run-number."
}

$medianRun = Get-RequiredInteger -Object $runSelection -Name 'medianRun' -Context 'evidence.runSelection' -Minimum 1
$medianValue = Get-RequiredNumber -Object $runSelection -Name 'medianValue' -Context 'evidence.runSelection'
$worstRun = Get-RequiredInteger -Object $runSelection -Name 'worstRun' -Context 'evidence.runSelection' -Minimum 1
$worstValue = Get-RequiredNumber -Object $runSelection -Name 'worstValue' -Context 'evidence.runSelection'
if ($medianRun -gt $repetitions -or $worstRun -gt $repetitions) {
    throw "evidence.runSelection indices must be within evidence.protocol.repetitions."
}

$selectorMetrics = @($metrics | Where-Object {
        $nameProperty = $_.PSObject.Properties['name']
        $null -ne $nameProperty -and $nameProperty.Value -eq $selectorMetricName
    })
if ($selectorMetrics.Count -ne 1) {
    throw "evidence.metrics must contain exactly one run-selection metric named '$selectorMetricName'."
}

$selectorMetric = $selectorMetrics[0]
$metricUnit = Get-RequiredString -Object $selectorMetric -Name 'unit' -Context "evidence.metrics[$selectorMetricName]"
if ($metricUnit -ne $selectorUnit) {
    throw "evidence.runSelection.selector.unit must match the selected evidence.metrics unit."
}

$selectedMedianValue = Get-RequiredNumber `
    -Object $selectorMetric `
    -Name $selectorStatistic `
    -Context "evidence.metrics[$selectorMetricName]"
if ([Math]::Abs($selectedMedianValue - $medianValue) -gt 0.000000001) {
    throw "evidence.metrics must describe medianRun: the selected metric statistic must equal evidence.runSelection.medianValue."
}

if ($selectorDirection -eq 'higher-is-worse' -and $worstValue -lt $medianValue) {
    throw "evidence.runSelection.worstValue must be >= medianValue when direction is higher-is-worse."
}

if ($selectorDirection -eq 'lower-is-worse' -and $worstValue -gt $medianValue) {
    throw "evidence.runSelection.worstValue must be <= medianValue when direction is lower-is-worse."
}

[pscustomobject]@{
    Valid = $true
    Path = $resolvedEvidencePath
    ScenarioId = $scenarioId
    MachineTier = $machineTier
    EligibleForThresholdCalibration = $eligibleForThresholdCalibration
}
