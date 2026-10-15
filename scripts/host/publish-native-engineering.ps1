[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$CommitSha
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Native host evidence requires Windows.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Engineering output must be a new directory.' }
$sdk = dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'SDK query failed.' }
dotnet publish src/Zhuomian.App --configuration Release --runtime win-x64 --self-contained true --output $OutputDirectory `
    "-p:InformationalVersion=0.0.0-e2-i2+$CommitSha" -p:IncludeSourceRevisionInInformationalVersion=false
if ($LASTEXITCODE -ne 0) { throw "Native engineering publish failed (exit $LASTEXITCODE)." }
$exe = Join-Path $OutputDirectory 'Zhuomian.App.exe'
foreach ($file in @('Zhuomian.App.exe', 'coreclr.dll', 'hostfxr.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory $file) -PathType Leaf)) { throw "Published dependency missing: $file" }
}
Copy-Item src/Zhuomian.App/Start-Engineering.cmd, src/Zhuomian.App/README.md -Destination $OutputDirectory
Copy-Item docs/ADR/0005-e2-native-engineering-host.md, docs/ADR/0006-e2-native-motion.md, docs/E2_I1_INTEGRATION_EVIDENCE.md, docs/E2_I2_INTEGRATION_EVIDENCE.md -Destination $OutputDirectory
foreach ($file in @('README.md', '0005-e2-native-engineering-host.md', '0006-e2-native-motion.md')) {
    $path = Join-Path $OutputDirectory $file
    $content = Get-Content -LiteralPath $path -Raw
    $content = $content.Replace('../../docs/ADR/0005-e2-native-engineering-host.md', '0005-e2-native-engineering-host.md')
    $content = $content.Replace('../../docs/E2_I1_INTEGRATION_EVIDENCE.md', 'E2_I1_INTEGRATION_EVIDENCE.md')
    $content = $content.Replace('../../docs/ADR/0006-e2-native-motion.md', '0006-e2-native-motion.md').Replace('../../docs/E2_I2_INTEGRATION_EVIDENCE.md', 'E2_I2_INTEGRATION_EVIDENCE.md').Replace('../E2_I2_INTEGRATION_EVIDENCE.md', 'E2_I2_INTEGRATION_EVIDENCE.md')
    Set-Content -LiteralPath $path -Value $content -Encoding utf8
}
& $exe --native-smoke 2>&1 | Tee-Object -FilePath (Join-Path $OutputDirectory 'ci-native-smoke.log')
if ($LASTEXITCODE -ne 0) { throw "Published EXE native smoke failed (exit $LASTEXITCODE)." }
$smoke = Get-Content -LiteralPath (Join-Path $OutputDirectory 'ci-native-smoke.log') -Raw
foreach ($required in @('native_motion hidden=true', 'native_motion_safety hidden=true', 'work_area_smoke hidden=true', 'native_region hidden=true', 'idle_timers=0', 'native_smoke_cleanup hwnd_destroyed=true')) {
    if (-not $smoke.Contains($required)) { throw "Incomplete native evidence: $required" }
}
if ($smoke -match 'unhook_ok=False|session_release_ok=False|class_release_failed|native_callback_failed|native_event_callback_failed') {
    throw 'Native cleanup or callback failure prevents publishing a usable engineering package.'
}
$checkedOutCommit = & git rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Git provenance query failed.' }
$baseMain = '0cdd6c1868f7c8339fe0343141b8d23f6f054ac5'
git diff --exit-code $baseMain -- src/Zhuomian.Core src/Zhuomian.Launching.Windows src/Zhuomian.Persistence `
    tests/Zhuomian.Core.Tests tests/Zhuomian.Launching.Windows.Tests tests/Zhuomian.LaunchFixture `
    tests/Zhuomian.Persistence.Tests tests/Zhuomian.Persistence.V2.Tests tests/Zhuomian.Persistence.LockFixture `
    tests/Zhuomian.Persistence.V2Fixture spikes scripts/persistence scripts/performance global.json Directory.Build.props `
    ':(exclude)scripts/persistence/test-baseline-history-performance.ps1' `
    ':(exclude)src/Zhuomian.Core/Interaction/SpaceInteractionController.cs' `
    ':(exclude)tests/Zhuomian.Core.Tests/Interaction/SpaceInteractionControllerTests.cs'
if ($LASTEXITCODE -ne 0) { throw 'Existing Core/E3/Spike/test/tooling source must remain unchanged.' }
"All Core/E3/Spike/test/tooling source except the documented minimal Returning change, additive Core cases and exact-blob history wrapper integration matches $baseMain byte-for-byte." | Set-Content -LiteralPath (Join-Path $OutputDirectory 'ISOLATION.txt')
. (Join-Path $PSScriptRoot 'assert-motion-source-isolation.ps1')
Assert-ReviewedMotionSourceIsolation -Repository (git rev-parse --show-toplevel)
$coreCases = & git diff --numstat $baseMain -- tests/Zhuomian.Core.Tests/Interaction/SpaceInteractionControllerTests.cs
if ($LASTEXITCODE -ne 0 -or $coreCases -notmatch '^\d+\t0\t') { throw 'Core regression changes must be additive.' }
New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'test-results') | Out-Null
$testResults = @()
foreach ($trx in Get-ChildItem -LiteralPath "$env:RUNNER_TEMP/test-results" -Filter *.trx -File) {
    Copy-Item -LiteralPath $trx.FullName -Destination (Join-Path $OutputDirectory 'test-results')
    [xml]$document = Get-Content -LiteralPath $trx.FullName -Raw
    $counters = $document.TestRun.ResultSummary.Counters
    if (-not $counters -or [int]$counters.failed -ne 0 -or [int]$counters.total -ne [int]$counters.passed) {
        throw 'Every test result must be complete and passing.'
    }
    $testResults += [ordered]@{ file = $trx.Name; total = [int]$counters.total; passed = [int]$counters.passed }
}
if ($testResults.Count -lt 5) { throw 'Missing original or Platform TRX results.' }
$metadata = [ordered]@{
    version = '0.0.0-e2-i2'
    commitSha = $CommitSha
    checkedOutCommit = $checkedOutCommit
    baseMain = $baseMain
    sourceCandidate = '5ea708e0e1b7f760816380dbf56f303d899cc8e6'
    nativeHostSource = '06d6f14520ebc1e7cc6486a4b3cdd90f11d7a678'
    runUrl = "https://github.com/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID"
    runnerImage = $env:ImageOS
    runnerImageVersion = $env:ImageVersion
    windows = (Get-CimInstance Win32_OperatingSystem).Caption
    windowsBuild = [Environment]::OSVersion.Version.ToString()
    sdk = $sdk.Trim()
    architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    testResults = $testResults
    deployment = 'self-contained win-x64 directory; unsigned engineering fixture'
    nativeEvidence = 'ci-native-smoke.log: actual monitor/DPI, hidden startup/resource snapshot and teardown'
    desktopAcceptance = $false
    productPerformanceAcceptance = $false
}
$metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'BUILD.json') -Encoding utf8
$metadata | ConvertTo-Json
dotnet list src/Zhuomian.App package --include-transitive | Set-Content -LiteralPath (Join-Path $OutputDirectory 'DEPENDENCIES.txt')
if ($LASTEXITCODE -ne 0) { throw 'Dependency inventory failed.' }
$cache = $env:NUGET_PACKAGES
if (-not $cache) { $cache = Join-Path $env:USERPROFILE '.nuget/packages' }
$runtime = Join-Path $cache 'microsoft.netcore.app.runtime.win-x64'
$notices = @(Get-ChildItem -LiteralPath $runtime -File -Recurse | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES|ThirdPartyNotices)' })
if ($notices.Count -eq 0) { throw 'Runtime license notices missing.' }
$index = 0
foreach ($notice in $notices) {
    $index++
    Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $OutputDirectory "runtime-$index-$($notice.Name)")
}
Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse | Sort-Object FullName | ForEach-Object {
    "$( (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash )  $([IO.Path]::GetRelativePath($OutputDirectory, $_.FullName))"
} | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding utf8
