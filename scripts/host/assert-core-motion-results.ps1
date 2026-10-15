function Get-ReviewedCoreResultCounts {
    param([Parameter(Mandatory)][xml]$Document)
    $count = $Document.TestRun.ResultSummary.Counters
    $rows = @($Document.TestRun.Results.UnitTestResult)
    if (-not $count -or [int]$count.failed -ne 0 -or [int]$count.notExecuted -ne 0 -or
        [int]$count.total -ne [int]$count.passed -or $rows.Count -ne [int]$count.total -or
        @($rows | Where-Object { $_.outcome -ne 'Passed' }).Count -ne 0) {
        throw 'Core results incomplete, failed or skipped.'
    }
    $prefix = 'Zhuomian.Core.Tests.Interaction.SpaceInteractionControllerTests.ReturningHoverReentryUsesExistingTokenWithSafetyAndInputPrecedence'
    $additional = @($rows | Where-Object { $_.testName.StartsWith($prefix, [StringComparison]::Ordinal) })
    if ($additional.Count -ne 9) { throw 'Reviewed re-entry cases incomplete or duplicated.' }
    foreach ($scenario in @('ordinary', 'armed', 'expired', 'suspended', 'hostlost', 'external', 'pending', 'preview', 'drag')) {
        $expected = $prefix + '(scenario: "' + $scenario + '")'
        if (@($additional | Where-Object { $_.testName -ceq $expected }).Count -ne 1) { throw "Missing/duplicate re-entry scenario: $scenario" }
    }
    $retained = $rows.Count - $additional.Count
    if ($retained -ne 451) { throw 'Original 451 Core results were reduced or replaced.' }
    return [pscustomobject]@{ original = $retained; additional = $additional.Count; actual = $rows.Count }
}
