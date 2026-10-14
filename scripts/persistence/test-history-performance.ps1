param([Parameter(Mandatory)][string]$OutputDirectory, [int]$Repetitions = 5)
$ErrorActionPreference = 'Stop'
if ($Repetitions -lt 5 -or $Repetitions -gt 20) { throw 'Require 5-20 independent paired rounds.' }
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$baselineSha = 'fa6338d662e5e59efe01a6ce01b399b3551a95ba'
$currentSha = git -C $repo rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Current SHA query failed.' }
$sourceChanges = @(git -C $repo status --porcelain -- src scripts/persistence/LegacyProbe scripts/persistence/test-history-performance.ps1)
if ($LASTEXITCODE -ne 0 -or $sourceChanges.Count -gt 0) { throw 'Commit measured production/probe sources before authoritative SHA-bound performance measurement.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$owned = Join-Path ([IO.Path]::GetTempPath()) ('zhuomian-performance-source-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $owned | Out-Null
try {
    $baselineRoot = Join-Path $owned 'baseline'
    New-Item -ItemType Directory -Path $baselineRoot | Out-Null
    $zip = Join-Path $owned 'baseline.zip'
    git -C $repo archive --format=zip --output=$zip $baselineSha
    if ($LASTEXITCODE -ne 0) { throw 'Reviewed source SHA unavailable; cannot substitute older E3-3.' }
    Expand-Archive -LiteralPath $zip -DestinationPath $baselineRoot
    $binaries = @{}
    foreach ($side in @('before', 'after')) {
        $source = if ($side -eq 'before') { $baselineRoot } else { $repo }
        $buildRoot = Join-Path $owned $side
        New-Item -ItemType Directory -Path $buildRoot | Out-Null
        Copy-Item -LiteralPath (Join-Path $repo 'Directory.Build.props') -Destination $buildRoot
        Copy-Item -LiteralPath (Join-Path $repo '.editorconfig') -Destination $buildRoot
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LegacyProbe') -Destination (Join-Path $buildRoot 'LegacyProbe') -Recurse
        $project = Join-Path $buildRoot 'LegacyProbe/LegacyProbe.csproj'
        dotnet restore $project "-p:LegacySourceRoot=$source"
        if ($LASTEXITCODE -ne 0) { throw "Matrix $side restore failed." }
        dotnet build $project --no-restore -c Release "-p:LegacySourceRoot=$source"
        if ($LASTEXITCODE -ne 0) { throw "Matrix $side build failed." }
        if ($side -eq 'after') {
            $previousRoot = $env:LegacySourceRoot
            try {
                $env:LegacySourceRoot = $source
                dotnet format $project --no-restore --verify-no-changes
                if ($LASTEXITCODE -ne 0) { throw 'Complete performance probe format failed.' }
            }
            finally { $env:LegacySourceRoot = $previousRoot }
        }
        $binaries[$side] = Join-Path $buildRoot 'LegacyProbe/bin/Release/net9.0/LegacyProbe.dll'
    }
    $fixture = Join-Path $repo 'tests/Zhuomian.Persistence.Tests/Fixtures/workspace-v1.json'
    $all = @{ before = @(); after = @() }
    $fixtureDigest = $null
    for ($round = 1; $round -le $Repetitions; $round++) {
        $order = if ($round % 2 -eq 1) { @('before', 'after') } else { @('after', 'before') }
        foreach ($side in $order) {
            $sha = if ($side -eq 'before') { $baselineSha } else { $currentSha }
            $json = dotnet $binaries[$side] $fixture $sha matrix $round
            if ($LASTEXITCODE -ne 0) { throw "Measured $side round $round failed; no silent retry." }
            $parsed = $json | ConvertFrom-Json
            if ($parsed.samples.Count -ne 20 -or $parsed.quotaRelief) { throw 'Incomplete or misleading measured round.' }
            if ($null -ne $fixtureDigest -and $fixtureDigest -ne $parsed.fixtureSha256) { throw 'Before/after input mismatch.' }
            $fixtureDigest = $parsed.fixtureSha256
            $json | Set-Content -LiteralPath (Join-Path $OutputDirectory "$side-$round.json") -Encoding utf8
            $all[$side] += $parsed.samples
        }
    }
    $summary = @()
    foreach ($history in @(0, 32, 128, 256, 512)) {
        foreach ($operation in @('Load', 'Save', 'Backup', 'Restore')) {
            $rows = @{}
            foreach ($side in @('before', 'after')) {
                $rows[$side] = @($all[$side] | Where-Object { $_.totalHistory -eq $history -and $_.operation -eq $operation })
                if ($rows[$side].Count -ne $Repetitions) { throw 'Missing repeated cell.' }
            }
            for ($i = 0; $i -lt $Repetitions; $i++) {
                foreach ($property in @('outcome', 'error', 'observedRevision', 'successfulReadFiles', 'successfulReadBytes', 'enumerationEntries', 'beforeFiles', 'beforeBytes', 'afterFiles', 'filesystem', 'seedDigest')) {
                    if ($rows.before[$i].$property -ne $rows.after[$i].$property) { throw "Acceptance or logical I/O changed: $operation/$history/$property." }
                }
            }
            $cells = @{}
            foreach ($side in @('before', 'after')) {
                $times = @($rows[$side].elapsedMs | Sort-Object)
                $allocations = @($rows[$side].allocatedBytes | Sort-Object)
                $cells[$side] = [ordered]@{ minMs = $times[0]; medianMs = $times[[int][Math]::Floor($Repetitions / 2)]; maxMs = $times[-1];
                    medianAllocatedBytes = $allocations[[int][Math]::Floor($Repetitions / 2)]; samples = $Repetitions }
            }
            $summary += [ordered]@{ totalHistory = $history; backups = $history / 2; prepared = $history / 2; operation = $operation;
                outcome = $rows.before[0].outcome; error = $rows.before[0].error; before = $cells.before; after = $cells.after;
                medianChangePercent = 100 * ($cells.after.medianMs / $cells.before.medianMs - 1);
                successfulReadFiles = $rows.after[0].successfulReadFiles; successfulReadBytes = $rows.after[0].successfulReadBytes;
                enumerationEntries = $rows.after[0].enumerationEntries }
        }
    }
    $document = [ordered]@{ schemaVersion = 1; reviewedBaselineSha = $baselineSha; currentSha = $currentSha; fixtureSha256 = $fixtureDigest;
        repetitions = $Repetitions; alternatingOrder = $true; immutableBaseline = $true; sameAcceptanceAndLogicalIo = $true;
        quotaRelief = $false; physicalDeviceIoMeasured = $false; desktopAcceptance = $false; cells = $summary }
    $document | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json') -Encoding utf8
    $document | ConvertTo-Json -Depth 10
}
finally { Remove-Item -LiteralPath $owned -Recurse -Force }
