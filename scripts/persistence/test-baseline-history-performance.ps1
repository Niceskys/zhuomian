param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$baselineSourceSha = 'b3b278b0759d5c0af55e224b250c27c33a6bbb10'
# Squash-merged PR commits are not ancestors of main. Fetch the retained read-only
# PR ref so the historical B/C commit objects can be resolved without substitutions.
git -C $repository fetch origin refs/pull/37/head
if ($LASTEXITCODE -ne 0) { throw 'Immutable E3-4A PR objects unavailable.' }
foreach ($sourceSha in @('ec43366ab3b86b54bac9e311b5f7d9ebb2fa9b6d', 'fa6338d662e5e59efe01a6ce01b399b3551a95ba', 'b25f9cefcf15f77be82b365563e4814b528d2f40')) {
    git -C $repository cat-file -e "$sourceSha^{commit}"
    if ($LASTEXITCODE -ne 0) { throw 'An immutable A/B/C source commit is unavailable.' }
}
# Preserve every original source/test file, not just a numeric suite count.
$originalSourcePaths = @(git -C $repository ls-tree -r --name-only $baselineSourceSha -- src tests)
if ($LASTEXITCODE -ne 0 -or $originalSourcePaths.Count -eq 0) { throw 'Original source inventory unavailable.' }
git -C $repository diff --quiet $baselineSourceSha -- $originalSourcePaths
if ($LASTEXITCODE -ne 0) { throw 'An original production/test file changed; immutable v1 baseline gate remains closed.' }
$baselineClone = Join-Path ([IO.Path]::GetTempPath()) ('zhuomian-v1-baseline-' + [Guid]::NewGuid().ToString('N'))
try {
    # Detached local immutable checkout; never change main or its working tree.
    git clone --shared --no-checkout $repository $baselineClone
    if ($LASTEXITCODE -ne 0) { throw 'Isolated baseline checkout failed.' }
    git -C $baselineClone checkout --detach $baselineSourceSha
    if ($LASTEXITCODE -ne 0) { throw 'Pinned E3-4A baseline checkout failed.' }
    # The historical script and its exact C src/tests freeze assertion are unchanged.
    # All six A/B/C permutations and all original raw/identity checks still execute.
    & (Join-Path $baselineClone 'scripts/persistence/test-history-three-version-performance.ps1') -OutputDirectory $OutputDirectory
}
finally {
    if (Test-Path -LiteralPath $baselineClone) { Remove-Item -LiteralPath $baselineClone -Recurse -Force }
}
