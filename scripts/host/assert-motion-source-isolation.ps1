function Assert-ReviewedMotionSourceIsolation {
    param([Parameter(Mandatory)][string]$Repository)
    $baseline = 'b3b278b0759d5c0af55e224b250c27c33a6bbb10'
    # Exact reviewed blobs, not a broad Core exclusion. Historical E3 source stays immutable.
    # The controller blob is the minimum original #32 Returning branch; the test blob
    # preserves all original cases and adds exactly nine re-entry scenarios.
    $approved = @{
        'src/Zhuomian.Core/Interaction/SpaceInteractionController.cs' = @(
            '884f8ab4fec7a51bed5e12c36180dec3bd2a7a2d', 'db24f30c901b4c830fbfd23a330436b651c08139')
        'tests/Zhuomian.Core.Tests/Interaction/SpaceInteractionControllerTests.cs' = @(
            '44c2f22191e530ca7d901da78611130890e13ca8', '02384777ce2295e9590470b4e1b490cb78c66c77')
    }
    $original = @(git -C $Repository ls-tree -r --name-only $baseline -- src tests)
    if ($LASTEXITCODE -ne 0 -or $original.Count -eq 0) { throw 'Original source inventory unavailable.' }
    foreach ($path in $approved.Keys) {
        $historicalBlob = git -C $Repository rev-parse "${baseline}:$path"
        if ($LASTEXITCODE -ne 0 -or $historicalBlob -ne $approved[$path][0]) { throw 'Historical Core identity changed.' }
        $currentBlob = git -C $Repository hash-object -- $path
        if ($LASTEXITCODE -ne 0 -or $currentBlob -notin $approved[$path]) { throw "Unreviewed Core/test change: $path" }
    }
    $frozen = @($original | Where-Object { -not $approved.ContainsKey($_) })
    git -C $Repository diff --quiet $baseline -- $frozen
    if ($LASTEXITCODE -ne 0) { throw 'An original E3/other Core/production/test file changed; historical gate remains closed.' }
}
