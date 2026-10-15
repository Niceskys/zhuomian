$ErrorActionPreference = 'Stop'
$publisher = Get-Content (Join-Path $PSScriptRoot 'publish-wpf-engineering.ps1') -Raw
$start = $publisher.IndexOf('$passedTotal = 0', [StringComparison]::Ordinal)
$end = $publisher.IndexOf('$metadata =', [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -le $start) { throw 'Actual publisher result gate not found.' }
$gate = [scriptblock]::Create($publisher.Substring($start, $end - $start))
# Ordered dictionaries deliberately reproduce the actual TRX projection in the publisher.
$complete = @(460, 37, 171, 286, 76, 2) | ForEach-Object { [ordered]@{ passed = $_; total = $_ } }
$testResults = $complete
& $gate
foreach ($failure in @('missing_suite', 'wrong_total', 'duplicate_suite')) {
    $testResults = switch ($failure) {
        'missing_suite' { $complete[0..4] }
        'wrong_total' { @($complete[0..4]) + @([ordered]@{ passed = 1; total = 1 }) }
        'duplicate_suite' { @($complete) + @($complete[0]) }
    }
    $rejected = $false
    try { & $gate } catch { $rejected = $true }
    if (-not $rejected) { throw "Publisher accepted $failure." }
}
Write-Output 'wpf_package_result_gate six_suites=1032 ordered_dictionary=true missing_wrong_duplicate_rejected=3 synthetic_fixture_only=true'
