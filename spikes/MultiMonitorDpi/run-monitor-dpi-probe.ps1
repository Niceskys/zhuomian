param(
    [ValidateRange(1, 50)]
    [int]$Count = 10,

    [string]$OutputPath = "$PSScriptRoot/evidence/windows-11-26100-monitor-dpi-summary.json"
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'Zhuomian.Spike.MultiMonitorDpi/Zhuomian.Spike.MultiMonitorDpi.csproj'
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
        coverageComplete = [bool]$evidence.CoverageComplete
        failedChecks = @($evidence.FailedChecks)
    })
}

$failedRuns = @($runs | Where-Object { -not $_.passed }).Count
$summary = [ordered]@{
    schemaVersion = 1
    timestampUtc = [DateTimeOffset]::UtcNow.ToString('O')
    osVersion = $firstEvidence.OsVersion
    processArchitecture = $firstEvidence.ProcessArchitecture
    requestedRuns = $Count
    passedRuns = @($runs | Where-Object passed).Count
    failedRuns = $failedRuns
    passed = $failedRuns -eq 0
    coverageComplete = [bool]$firstEvidence.CoverageComplete
    monitorCount = [int]$firstEvidence.MonitorCount
    distinctDpiCount = [int]$firstEvidence.DistinctDpiCount
    processPerMonitorV2 = [bool]$firstEvidence.ProcessPerMonitorV2
    actualPerMonitorHostsPassed = [bool]$firstEvidence.ActualPerMonitorHostsPassed
    syntheticMixedDpiMappingPassed = [bool]$firstEvidence.SyntheticMixedDpiMappingPassed
    monitors = @($firstEvidence.Monitors)
    syntheticChecks = @($firstEvidence.SyntheticChecks)
    runs = $runs
    coverageGaps = @($firstEvidence.CoverageGaps)
    limitations = @($firstEvidence.Limitations)
}

$fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $fullOutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fullOutputPath -Encoding utf8NoBOM
$summary | ConvertTo-Json -Depth 8

if (-not $summary.passed) { exit 1 }
