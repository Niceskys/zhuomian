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
$total = 0; $passed = 0; $failed = 0; $skipped = 0; $v2 = 0; $killedCuts = 0
foreach ($file in Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File) {
    [xml]$trx = Get-Content -LiteralPath $file.FullName -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    $total += [int]$counters.total; $passed += [int]$counters.passed
    $failed += [int]$counters.failed; $skipped += [int]$counters.notExecuted
    foreach ($result in $trx.TestRun.Results.UnitTestResult) {
        if ($result.testName -match '\.V2(Store|Native)Tests\.') {
            if ($result.outcome -ne 'Passed') { throw 'v2 failure or skip.' }
            $v2++
            if ($result.testName -match 'KilledChildReleasesMutex') { $killedCuts++ }
        }
    }
}
if ($total -lt 697 -or $passed -ne $total -or $failed -ne 0 -or $skipped -ne 0 -or $v2 -lt 38 -or $killedCuts -ne 4) {
    throw "Incomplete suite: total=$total passed=$passed failed=$failed skipped=$skipped v2=$v2 cuts=$killedCuts."
}
[ordered]@{
    commitSha = $CommitSha
    windowsBuild = [Environment]::OSVersion.Version.ToString()
    total = $total; passed = $passed; failed = $failed; skipped = $skipped
    v2Tests = $v2; terminatedChildCuts = $killedCuts
    realSaves = $capacity.saves; restoredRevision = $capacity.restoredRevision
    damagedRootRepairImplemented = $false; physicalPowerLossTested = $false
    engineeringMergeGateReached = $false; productReleaseGateReached = $false
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'v2-platform.json') -Encoding utf8

# Generated measurements only, never Workspace bytes or personal paths.
Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'v2-capacity.json') -Raw
Get-Content -LiteralPath (Join-Path $EvidenceDirectory 'v2-platform.json') -Raw
