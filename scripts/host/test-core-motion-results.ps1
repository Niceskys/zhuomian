$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'assert-core-motion-results.ps1')
function Fixture {
    $rows = 1..451 | ForEach-Object { '<UnitTestResult testName="Zhuomian.Core.Tests.Fixture.Original' + $_ + '" outcome="Passed" />' }
    $prefix = 'Zhuomian.Core.Tests.Interaction.SpaceInteractionControllerTests.ReturningHoverReentryUsesExistingTokenWithSafetyAndInputPrecedence'
    $rows += @('ordinary', 'armed', 'expired', 'suspended', 'hostlost', 'external', 'pending', 'preview', 'drag') | ForEach-Object {
        '<UnitTestResult testName="' + $prefix + '(scenario: &quot;' + $_ + '&quot;)" outcome="Passed" />'
    }
    [xml]$document = '<TestRun><Results>' + ($rows -join '') + '</Results><ResultSummary><Counters total="460" passed="460" failed="0" notExecuted="0" /></ResultSummary></TestRun>'
    return ,$document
}
$accepted = Get-ReviewedCoreResultCounts -Document (Fixture)
if ($accepted.original -ne 451 -or $accepted.additional -ne 9 -or $accepted.actual -ne 460) { throw 'Core evidence fixture totals incorrect.' }
foreach ($failure in @('missing_original', 'missing_new', 'missing_all_new', 'duplicate_scenario', 'failed', 'skipped', 'wrong_total')) {
    $document = Fixture
    switch ($failure) {
        'missing_original' { $null = $document.TestRun.Results.RemoveChild($document.TestRun.Results.FirstChild); $document.TestRun.ResultSummary.Counters.SetAttribute('total', '459'); $document.TestRun.ResultSummary.Counters.SetAttribute('passed', '459') }
        'missing_new' { $null = $document.TestRun.Results.RemoveChild($document.TestRun.Results.LastChild); $document.TestRun.ResultSummary.Counters.SetAttribute('total', '459'); $document.TestRun.ResultSummary.Counters.SetAttribute('passed', '459') }
        'missing_all_new' { for ($i = 0; $i -lt 9; $i++) { $null = $document.TestRun.Results.RemoveChild($document.TestRun.Results.LastChild) }; $document.TestRun.ResultSummary.Counters.SetAttribute('total', '451'); $document.TestRun.ResultSummary.Counters.SetAttribute('passed', '451') }
        'duplicate_scenario' { $document.TestRun.Results.LastChild.SetAttribute('testName', $document.TestRun.Results.ChildNodes[451].testName) }
        'failed' { $document.TestRun.Results.FirstChild.SetAttribute('outcome', 'Failed') }
        'skipped' { $document.TestRun.ResultSummary.Counters.SetAttribute('notExecuted', '1') }
        'wrong_total' { $document.TestRun.ResultSummary.Counters.SetAttribute('total', '459') }
    }
    $rejected = $false
    try { $null = Get-ReviewedCoreResultCounts -Document $document } catch { $rejected = $true }
    if (-not $rejected) { throw "Core evidence accepted $failure" }
}
Write-Output 'reviewed_core_results original=451 exact_additional=9 rejected_negative_cases=7 assertions_preserved=true synthetic_fixture_only=true'
