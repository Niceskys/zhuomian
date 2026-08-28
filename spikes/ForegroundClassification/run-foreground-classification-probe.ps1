param(
    [ValidateRange(1, 20)]
    [int]$Count = 5,

    [string]$OutputPath = "$PSScriptRoot/evidence/windows-11-26100-foreground-classification-summary.json"
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'Zhuomian.Spike.ForegroundClassification/Zhuomian.Spike.ForegroundClassification.csproj'
dotnet build $project --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$runs = [System.Collections.Generic.List[object]]::new()
$firstEvidence = $null

for ($index = 1; $index -le $Count; $index++) {
    $raw = & dotnet run --project $project --configuration Release --no-build 2>&1
    $exitCode = $LASTEXITCODE
    $json = $raw -join [Environment]::NewLine
    $evidence = $json | ConvertFrom-Json

    if ($null -eq $firstEvidence) { $firstEvidence = $evidence }

    $runs.Add([ordered]@{
        run = $index
        exitCode = $exitCode
        passed = [bool]$evidence.Passed
        realExternalSamples = [int]$evidence.RealExternalSamples
        realExternalBlockedSamples = [int]$evidence.RealExternalBlockedSamples
        externalDisarmedExpansions = [int]$evidence.ExternalDisarmedExpansions
        failedChecks = @($evidence.FailedChecks)
        safetyAborts = @($evidence.SafetyAborts)
    })
}

$failedRuns = @($runs | Where-Object { -not $_.passed }).Count
$totalRealExternalSamples = 0
$totalRealExternalBlockedSamples = 0
$totalExternalDisarmedExpansions = 0
foreach ($run in $runs) {
    $totalRealExternalSamples += [int]$run['realExternalSamples']
    $totalRealExternalBlockedSamples += [int]$run['realExternalBlockedSamples']
    $totalExternalDisarmedExpansions += [int]$run['externalDisarmedExpansions']
}

$summary = [ordered]@{
    schemaVersion = 1
    timestampUtc = [DateTimeOffset]::UtcNow.ToString('O')
    osVersion = $firstEvidence.OsVersion
    processArchitecture = $firstEvidence.ProcessArchitecture
    requestedRuns = $Count
    passedRuns = @($runs | Where-Object passed).Count
    failedRuns = $failedRuns
    passed = $failedRuns -eq 0
    totalRealExternalSamples = $totalRealExternalSamples
    totalRealExternalBlockedSamples = $totalRealExternalBlockedSamples
    totalExternalDisarmedExpansions = $totalExternalDisarmedExpansions
    truthTableCases = [int]$firstEvidence.TruthTableCases
    truthTablePassedCases = [int]$firstEvidence.TruthTablePassedCases
    syntheticChecks = @($firstEvidence.SyntheticChecks)
    validatedBehavior = [ordered]@{
        liveShellClassifiedAsDesktopAvailable = [bool]$firstEvidence.LiveShellClassifiedAsDesktopAvailable
        ownForegroundClassifiedAsDesktopAvailable = [bool]$firstEvidence.OwnForegroundClassifiedAsDesktopAvailable
        originalForegroundRestored = [bool]$firstEvidence.OriginalForegroundWasRestored
        pointerRestored = [bool]$firstEvidence.PointerWasRestored
        hoverGateHasNoKeyboardCapturePath = [bool]$firstEvidence.HoverGateHasNoKeyboardCapturePath
    }
    runs = $runs
    limitations = @($firstEvidence.Limitations)
}

$fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $fullOutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fullOutputPath -Encoding utf8NoBOM
$summary | ConvertTo-Json -Depth 8

if (-not $summary.passed) { exit 1 }
