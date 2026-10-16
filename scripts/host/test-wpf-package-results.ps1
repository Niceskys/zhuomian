$ErrorActionPreference = 'Stop'
$publisher = Get-Content (Join-Path $PSScriptRoot 'publish-wpf-engineering.ps1') -Raw
$start = $publisher.IndexOf('$passedTotal = 0', [StringComparison]::Ordinal)
$end = $publisher.IndexOf('$diagnostics =', [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -le $start) { throw 'Actual publisher result gate not found.' }
$gate = [scriptblock]::Create($publisher.Substring($start, $end - $start))
# Ordered dictionaries deliberately reproduce the actual TRX projection in the publisher.
$complete = @(460, 37, 171, 286, 87, 9) | ForEach-Object { [ordered]@{ passed = $_; total = $_ } }
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
Write-Output 'wpf_package_result_gate six_suites=1050 ordered_dictionary=true missing_wrong_duplicate_rejected=3 synthetic_fixture_only=true'
# Attest the actual timed baseline implementation against immutable UI-I1 production source.
$baselineSha = '74b497da2d3993e58f1c3a788a86faea37453038'
$original = (git show "${baselineSha}:src/Zhuomian.Rendering.Wpf/WpfHostRenderer.cs") -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Immutable UI-I1 renderer source unavailable.' }
$fixture = Get-Content (Join-Path $PSScriptRoot '../../tests/Zhuomian.Rendering.Wpf.Tests/BaselineWpfRenderer.cs') -Raw
function Get-SubmitBlock([string]$source, [string]$endMarker) {
    $startIndex = $source.IndexOf('    public void Submit(SceneSnapshot scene)', [StringComparison]::Ordinal)
    $endIndex = $source.IndexOf($endMarker, $startIndex, [StringComparison]::Ordinal)
    if ($startIndex -lt 0 -or $endIndex -le $startIndex) { throw 'Baseline Submit block missing.' }
    return $source.Substring($startIndex, $endIndex - $startIndex).Replace("`r`n", "`n").Trim()
}
if ((Get-SubmitBlock $original '    internal bool MatchesSubmittedGeometry') -cne (Get-SubmitBlock $fixture '    public int Run()')) {
    throw 'Test-only renderer Submit/Place/Brush/Label diverges from immutable UI-I1 baseline.'
}
Write-Output "wpf_renderer_baseline immutable_source=$baselineSha submit_and_helpers_exact=true"
