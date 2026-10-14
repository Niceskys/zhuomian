param(
    [Parameter(Mandatory = $true)][string]$ResultsDirectory,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][string]$CommitSha
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'v2 native evidence requires Windows.' }
$capacity = Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'v2-capacity.json') -Raw | ConvertFrom-Json
if ($capacity.saves -ne 1024 -or -not $capacity.windowsNtfs -or $capacity.filesystem -ne 'NTFS') {
    throw '1024 real NTFS Save evidence missing.'
}
foreach ($point in @(256, 512, 1000, 1024)) {
    $cell = @($capacity.checkpoints | Where-Object { $_.attempts -eq $point })
    if ($cell.Count -ne 1 -or $cell[0].successes -ne $point -or $cell[0].refusals -ne 0 -or
        $cell[0].currentRevision -ne $point -or $cell[0].historyFiles -ne ($point + 1) -or
        -not $cell[0].completeHistoryVerified) { throw "Invalid checkpoint $point." }
}
$matrix = @(Get-ChildItem -LiteralPath $EvidenceDirectory -Filter "matrix-*.json" -File)
$terminated = @(Get-ChildItem -LiteralPath $EvidenceDirectory -Filter "killed-*.json" -File)
$authority = @(Get-ChildItem -LiteralPath $EvidenceDirectory -Filter "authority-*.json" -File)
if ($matrix.Count -ne 81 -or $terminated.Count -ne 81 -or $authority.Count -ne 7) { throw "Incomplete round-2 fault evidence." }
foreach ($file in $terminated) {
    $cut = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if (-not $cut.windowsNtfs -or -not $cut.evidenceRetained -or $cut.competingSaveAndRestore -ne "Busy") { throw "Invalid native termination evidence." }
}
$native = @(Get-ChildItem -LiteralPath $EvidenceDirectory -Filter "native-*.json" -File)
if ($native.Count -ne 8) { throw "Incomplete initialization/restore native ACL and sharing evidence." }
foreach ($file in $native) { $cell = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json; if (-not $cell.windowsNtfs) { throw "Not native Windows evidence." } }
$benchmark = Get-Content -LiteralPath (Join-Path $EvidenceDirectory "v2-read-benchmark.json") -Raw | ConvertFrom-Json
if (-not $benchmark.windowsNtfs -or $benchmark.realDistinctSaves -ne 1024 -or @($benchmark.samples).Count -ne 216) { throw "Incomplete paired read benchmark." }
foreach ($cell in $benchmark.growth) { if ($null -eq $cell.ntfsAllocationBytes) { throw "NTFS allocation measurement missing." } }

$total = 0; $passed = 0; $failed = 0; $skipped = 0; $v2 = 0; $originalV2 = 0; $killedCuts = 0; $newKilled = 0; $matrixCases = 0
foreach ($file in Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File) {
    [xml]$trx = Get-Content -LiteralPath $file.FullName -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    $total += [int]$counters.total; $passed += [int]$counters.passed
    $failed += [int]$counters.failed; $skipped += [int]$counters.notExecuted
    foreach ($result in $trx.TestRun.Results.UnitTestResult) {
        if ($result.testName -match '^Zhuomian\.Persistence\.V2\.Tests\.') {
            if ($result.outcome -ne 'Passed') { throw 'v2 failure or skip.' }
            $v2++
            if ($result.testName -match "V2(Store|Native)Tests\.") { $originalV2++ }
            if ($result.testName -match "TerminatedProcessAtEveryTransactionCut") { $newKilled++ }
            if ($result.testName -match "EveryWriteAndCommitCut") { $matrixCases++ }
            if ($result.testName -match 'KilledChildReleasesMutex') { $killedCuts++ }
        }
    }
}
if ($total -lt 887 -or $passed -ne $total -or $failed -ne 0 -or $skipped -ne 0 -or $v2 -lt 228 -or $originalV2 -lt 38 -or $killedCuts -ne 4 -or $newKilled -ne 81 -or $matrixCases -ne 81) {
    throw "Incomplete suite: total=$total passed=$passed failed=$failed skipped=$skipped v2=$v2 cuts=$killedCuts."
}
[ordered]@{
    commitSha = $CommitSha
    windowsBuild = [Environment]::OSVersion.Version.ToString()
    total = $total; passed = $passed; failed = $failed; skipped = $skipped
    originalV2Tests = $originalV2
    v2Tests = $v2; terminatedChildCuts = $killedCuts
    deterministicMatrixCuts = $matrix.Count; additionalTerminatedCuts = $terminated.Count
    nativeInitializationRestoreCases = $native.Count
    authorityLossCases = $authority.Count; pairedPerformanceSamples = @($benchmark.samples).Count
    realSaves = $capacity.saves; restoredRevision = $capacity.restoredRevision
    damagedRootRepairImplemented = $false; physicalPowerLossTested = $false
    engineeringMergeGateReached = $false; productReleaseGateReached = $false
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'v2-platform.json') -Encoding utf8

# Generated measurements only, never Workspace bytes or personal paths.
Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'v2-capacity.json') -Raw
Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'v2-platform.json') -Raw

Get-Content -LiteralPath (Join-Path $EvidenceDirectory "v2-read-benchmark.json") -Raw
