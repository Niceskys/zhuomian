$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $PSScriptRoot 'assert-motion-source-isolation.ps1')
Assert-ReviewedMotionSourceIsolation -Repository $repo
$owned = Join-Path ([IO.Path]::GetTempPath()) ('e2-motion-isolation-' + [Guid]::NewGuid().ToString('N'))
try {
    git clone --shared --no-checkout $repo $owned
    if ($LASTEXITCODE -ne 0) { throw 'Isolation fixture clone failed.' }
    git -C $owned checkout --detach HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Isolation fixture checkout failed.' }
    Assert-ReviewedMotionSourceIsolation -Repository $owned
    foreach ($path in @(
        'src/Zhuomian.Core/Interaction/SpaceInteractionController.cs',
        'tests/Zhuomian.Core.Tests/Interaction/SpaceInteractionControllerTests.cs',
        'src/Zhuomian.Core/WorkspaceModel/ResourceIdentity.cs',
        'tests/Zhuomian.Core.Tests/WorkspaceModel/ResourceIdentityTests.cs',
        'src/Zhuomian.Core/WorkspaceModel/README.md',
        'tests/Zhuomian.Core.Tests/Launching/LaunchPolicyTests.cs',
        'src/Zhuomian.Core/Layout/SpaceLayoutEngine.cs',
        'src/Zhuomian.Persistence/WorkspaceFileStore.cs',
        'tests/Zhuomian.Persistence.Tests/CodecTests.cs'
    )) {
        # Any unreviewed addition is rejected, even a source comment. Test deletion is rejected too.
        $file = Join-Path $owned $path
        if (-not (Test-Path -LiteralPath $file)) { throw "Fixture path missing: $path" }
        Add-Content -LiteralPath $file -Value '// unreviewed fixture mutation'
        $rejected = $false
        try { Assert-ReviewedMotionSourceIsolation -Repository $owned } catch { $rejected = $true }
        if (-not $rejected) { throw "Isolation accepted mutation: $path" }
        git -C $owned restore -- $path
        if ($LASTEXITCODE -ne 0) { throw 'Fixture reset failed.' }
    }
    $deleted = Join-Path $owned 'tests/Zhuomian.Core.Tests/Interaction/SpaceInteractionControllerTests.cs'
    Remove-Item -LiteralPath $deleted
    $rejected = $false
    try { Assert-ReviewedMotionSourceIsolation -Repository $owned } catch { $rejected = $true }
    if (-not $rejected) { throw 'Isolation accepted missing original tests.' }
    git -C $owned restore -- tests/Zhuomian.Core.Tests/Interaction/SpaceInteractionControllerTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Deleted-test fixture reset failed.' }
    # Revalidate the restored fixture: success must end in a real successful Git check,
    # not leave the deliberately rejected hash-object exit code as the script result.
    Assert-ReviewedMotionSourceIsolation -Repository $owned
    Write-Output 'motion_source_isolation accepted_exact_blobs=true rejected_mutations=9 rejected_test_deletion=true immutable_E3_baseline_unchanged=true'
}
finally {
    if (Test-Path -LiteralPath $owned) { Remove-Item -LiteralPath $owned -Recurse -Force }
}
