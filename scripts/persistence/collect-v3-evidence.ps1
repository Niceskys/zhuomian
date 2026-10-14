param(
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][string]$CommitSha
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Format 3 evidence requires Windows NTFS.' }
$suite = Get-Content -LiteralPath "$EvidenceDirectory/v2-platform.json" -Raw | ConvertFrom-Json
if ($suite.total -lt 942 -or $suite.passed -ne $suite.total -or $suite.failed -ne 0 -or $suite.skipped -ne 0 -or $suite.v2Tests -lt 283) {
    throw 'Retained and new tests are not all passed.'
}
$performance = Get-Content -LiteralPath "$EvidenceDirectory/v3-performance.json" -Raw | ConvertFrom-Json
$baseline = Get-Content -LiteralPath "$EvidenceDirectory/v3-baseline.json" -Raw | ConvertFrom-Json
if ($baseline.sourceSha -ne '514a5594f594728d2093cb041ed3552472384472' -or
    $performance.baselineSha -ne $baseline.sourceSha -or -not $performance.windowsNtfs -or
    $performance.sequentialBaselineSaves -ne 1024 -or $performance.sequentialFormat3Saves -ne 1024 -or
    @($performance.samples).Count -ne 135 -or @($performance.profiles).Count -ne 30) {
    throw 'Incomplete immutable baseline, actual capacity or repeated performance evidence.'
}
foreach ($n in @(256, 512, 1000, 1024)) {
    $point = @($performance.checkpoints | Where-Object { $_.saves -eq $n })
    if ($point.Count -ne 1 -or $point[0].successes -ne $n -or $point[0].refusals -ne 0 -or
        $point[0].revision -ne $n -or $point[0].files -ne (3 * $n + 4) -or
        $point[0].pages -ne ($n + 1) -or $point[0].witnesses -ne (2 * $n) -or
        $point[0].filesystem -ne 'NTFS' -or -not $point[0].completeHistory -or
        $null -eq $point[0].ntfsAllocationBytes) { throw "Invalid new-protocol capacity $n." }
}
$killed = @(Get-ChildItem -LiteralPath $EvidenceDirectory -Filter 'v3-killed-*.json' -File)
$matrix = @(Get-ChildItem -LiteralPath $EvidenceDirectory -Filter 'v3-matrix-*.json' -File)
$recovery = @(Get-ChildItem -LiteralPath $EvidenceDirectory -Filter 'v3-recovery-*.json' -File)
if ($killed.Count -ne 10 -or $matrix.Count -ne 12 -or $recovery.Count -ne 4) { throw 'Targeted recovery evidence missing.' }
foreach ($file in $killed) {
    $cut = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if (-not $cut.windowsNtfs -or -not $cut.retained -or $cut.competing -ne 'Busy') { throw 'Invalid native recovery termination.' }
}
[ordered]@{
    commitSha = $CommitSha; baselineSha = $baseline.sourceSha
    totalTests = $suite.total; passed = $suite.passed; failed = $suite.failed; skipped = $suite.skipped
    optInBinaryFormat = 3; realDistinctSaves = 1024
    singleMainRepairImplemented = $true; authoritySlotRepairImplemented = $false
    targetedKilledCuts = $killed.Count; targetedWriteCuts = $matrix.Count; mainLossCases = $recovery.Count
    physicalPowerLossTested = $false; productDefaultEnabled = $false; adr = 'Proposed'
} | ConvertTo-Json | Set-Content -LiteralPath "$EvidenceDirectory/v3-platform.json" -Encoding utf8
Get-Content -LiteralPath "$EvidenceDirectory/v3-platform.json" -Raw
Get-Content -LiteralPath "$EvidenceDirectory/v3-performance.json" -Raw
