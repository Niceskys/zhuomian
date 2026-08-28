param(
    [int]$Count = 5,
    [string]$OutputPath = "$PSScriptRoot/evidence/windows-11-26100-platform-lifecycle-summary.json"
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Zhuomian.Spike.PlatformLifecycle/Zhuomian.Spike.PlatformLifecycle.csproj'
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("zhuomian-lifecycle-" + [guid]::NewGuid().ToString('N'))
$runs = @()

New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
try {
    dotnet build $project --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    for ($index = 1; $index -le $Count; $index++) {
        $runOutput = Join-Path $temporaryDirectory ("run-$index.json")
        dotnet run --project $project --configuration Release --no-build -- --output $runOutput
        if ($LASTEXITCODE -ne 0) { throw "Platform lifecycle run $index failed." }
        $runs += Get-Content -LiteralPath $runOutput -Raw | ConvertFrom-Json
    }

    $summary = [ordered]@{
        schemaVersion = 1
        timestampUtc = [DateTimeOffset]::UtcNow
        requestedRuns = $Count
        passedRuns = @($runs | Where-Object Passed).Count
        failedRuns = @($runs | Where-Object { -not $_.Passed }).Count
        coverageComplete = @($runs | Where-Object CoverageComplete).Count -eq $Count
        environment = [ordered]@{
            osVersion = $runs[0].OsVersion
            processArchitecture = $runs[0].ProcessArchitecture
            monitorCount = $runs[0].MonitorCount
        }
        realFullscreenRuns = @($runs | Where-Object RealFullscreenTested).Count
        realLockRuns = @($runs | Where-Object RealLockTested).Count
        realUacSecureDesktopRuns = @($runs | Where-Object RealUacSecureDesktopTested).Count
        suspendedHoverAttempts = ($runs | Measure-Object SuspendedHoverAttempts -Sum).Sum
        suspendedHoverExpansions = ($runs | Measure-Object SuspendedHoverExpansions -Sum).Sum
        runs = $runs
        limitations = $runs[0].Limitations
    }

    $summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
    if ($summary.failedRuns -ne 0) { exit 1 }
}
finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
