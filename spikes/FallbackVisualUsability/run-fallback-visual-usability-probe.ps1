param(
    [int]$Count = 5,
    [string]$OutputPath = "$PSScriptRoot/evidence/windows-11-26100-fallback-visual-usability-summary.json",
    [string]$ScreenshotPath = "$PSScriptRoot/evidence/windows-11-26100-fallback-visual-usability.png"
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Zhuomian.Spike.FallbackVisualUsability/Zhuomian.Spike.FallbackVisualUsability.csproj'
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("zhuomian-visual-" + [guid]::NewGuid().ToString('N'))
$runs = @()

New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
try {
    dotnet build $project --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    for ($index = 1; $index -le $Count; $index++) {
        $runOutput = Join-Path $temporaryDirectory ("run-$index.json")
        $runScreenshot = Join-Path $temporaryDirectory ("run-$index.png")
        dotnet run --project $project --configuration Release --no-build -- --output $runOutput --screenshot $runScreenshot
        if ($LASTEXITCODE -ne 0) { throw "Visual usability run $index failed." }
        $runs += Get-Content -LiteralPath $runOutput -Raw | ConvertFrom-Json

        if ($index -eq $Count) {
            Copy-Item -LiteralPath $runScreenshot -Destination $ScreenshotPath -Force
        }
    }

    $summary = [ordered]@{
        schemaVersion = 1
        timestampUtc = [DateTimeOffset]::UtcNow
        requestedRuns = $Count
        passedRuns = @($runs | Where-Object Passed).Count
        failedRuns = @($runs | Where-Object { -not $_.Passed }).Count
        environment = [ordered]@{
            osVersion = $runs[0].OsVersion
            processArchitecture = $runs[0].ProcessArchitecture
            monitorCount = $runs[0].MonitorCount
        }
        strategy = $runs[0].Strategy
        screenshotArtifact = [System.IO.Path]::GetFileName($ScreenshotPath)
        runs = $runs
        limitations = $runs[0].Limitations
    }

    $summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
    if ($summary.failedRuns -ne 0) { exit 1 }
}
finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
