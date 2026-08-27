param(
    [ValidateRange(1, 100)]
    [int]$Count = 20,

    [string]$OutputPath = "$PSScriptRoot/evidence/windows-11-26100-fallback-host-summary.json"
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'Zhuomian.Spike.DesktopHosting/Zhuomian.Spike.DesktopHosting.csproj'
dotnet build $project --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$runs = [System.Collections.Generic.List[object]]::new()

for ($index = 1; $index -le $Count; $index++) {
    $raw = & dotnet run --project $project --configuration Release --no-build 2>&1
    $exitCode = $LASTEXITCODE
    $json = $raw -join [Environment]::NewLine
    $evidence = $json | ConvertFrom-Json

    $runs.Add([ordered]@{
        run = $index
        exitCode = $exitCode
        passed = [bool]$evidence.Passed
        failedChecks = @($evidence.FailedChecks)
    })
}

$first = & dotnet run --project $project --configuration Release --no-build 2>&1 |
    Out-String |
    ConvertFrom-Json

$summary = [ordered]@{
    schemaVersion = 1
    timestampUtc = [DateTimeOffset]::UtcNow.ToString('O')
    osVersion = $first.OsVersion
    processArchitecture = $first.ProcessArchitecture
    monitorCount = $first.MonitorCount
    hostMode = $first.HostMode
    requestedRuns = $Count
    passedRuns = @($runs | Where-Object passed).Count
    failedRuns = @($runs | Where-Object { -not $_.passed }).Count
    passed = @($runs | Where-Object { -not $_.passed }).Count -eq 0
    runs = $runs
    limitations = @($first.Limitations)
}

$fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $fullOutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fullOutputPath -Encoding utf8NoBOM
$summary | ConvertTo-Json -Depth 8

if (-not $summary.passed) { exit 1 }
