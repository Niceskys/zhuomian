param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$versions = [ordered]@{
    A = 'ec43366ab3b86b54bac9e311b5f7d9ebb2fa9b6d'
    B = 'fa6338d662e5e59efe01a6ce01b399b3551a95ba'
    C = 'b25f9cefcf15f77be82b365563e4814b528d2f40'
}
# All six permutations: each version occurs twice in each position, no sample discarded.
$orders = @('ABC', 'CBA', 'BCA', 'ACB', 'CAB', 'BAC')
$changed = @(git -C $repo diff $versions.C -- src tests)
if ($LASTEXITCODE -ne 0 -or $changed.Count -ne 0) { throw 'Frozen C production and all 659 tests must remain byte unchanged.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$owned = Join-Path ([IO.Path]::GetTempPath()) ('zhuomian-three-version-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $owned | Out-Null
function Median($Values) {
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 0) { return ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
    return $sorted[$middle]
}
try {
    $binaries = @{}
    foreach ($version in $versions.Keys) {
        $sha = $versions[$version]
        $resolved = git -C $repo rev-parse "$sha^{commit}"
        if ($LASTEXITCODE -ne 0 -or $resolved -ne $sha) { throw 'Fixed source commit unavailable.' }
        $source = Join-Path $owned "source-$version"
        $zip = Join-Path $owned "$version.zip"
        git -C $repo archive --format=zip --output=$zip $sha
        if ($LASTEXITCODE -ne 0) { throw 'Immutable source export failed.' }
        Expand-Archive -LiteralPath $zip -DestinationPath $source
        $buildRoot = Join-Path $owned "probe-$version"
        New-Item -ItemType Directory -Path $buildRoot | Out-Null
        Copy-Item -LiteralPath (Join-Path $repo 'Directory.Build.props') -Destination $buildRoot
        Copy-Item -LiteralPath (Join-Path $repo '.editorconfig') -Destination $buildRoot
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LegacyProbe') -Destination (Join-Path $buildRoot 'LegacyProbe') -Recurse
        $project = Join-Path $buildRoot 'LegacyProbe/LegacyProbe.csproj'
        dotnet restore $project "-p:LegacySourceRoot=$source"
        if ($LASTEXITCODE -ne 0) { throw "Version $version restore failed." }
        dotnet build $project --no-restore -c Release "-p:LegacySourceRoot=$source"
        if ($LASTEXITCODE -ne 0) { throw "Version $version Release build failed." }
        $previousRoot = $env:LegacySourceRoot
        try {
            $env:LegacySourceRoot = $source
            # Probe only: original A/B/C sources are authentic immutable snapshots.
            dotnet format $project --no-restore --verify-no-changes --include (Join-Path $buildRoot 'LegacyProbe')
            if ($LASTEXITCODE -ne 0) { throw "Version $version probe format failed." }
        }
        finally { $env:LegacySourceRoot = $previousRoot }
        $binaries[$version] = Join-Path $buildRoot 'LegacyProbe/bin/Release/net9.0/LegacyProbe.dll'
    }
    $fixture = Join-Path $repo 'tests/Zhuomian.Persistence.Tests/Fixtures/workspace-v1.json'
    $fixtureDigest = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
    $all = @{ A = @(); B = @(); C = @() }
    $assemblyDigests = @{}
    $platform = $null
    for ($round = 1; $round -le $orders.Count; $round++) {
        foreach ($letter in $orders[$round - 1].ToCharArray()) {
            $version = $letter.ToString()
            $json = dotnet $binaries[$version] $fixture $versions[$version] matrix $round
            if ($LASTEXITCODE -ne 0) { throw "Version $version round $round failed; no retry or sample removal." }
            # Persist before checks: rejected measurement evidence must not disappear.
            $json | Set-Content -LiteralPath (Join-Path $OutputDirectory "$version-$round.json") -Encoding utf8
            Write-Output ("E3_4_TRIPLE_RAW=" + ($json -join "`n"))
            $parsed = $json | ConvertFrom-Json
            if ($parsed.samples.Count -ne 20 -or $parsed.quotaRelief -or $parsed.fixtureSha256 -ne $fixtureDigest -or $parsed.sourceSha -ne $versions[$version] -or $parsed.repetition -ne $round) { throw 'Invalid source/fixture/round/protocol binding.' }
            $identity = "$($parsed.platform)/$($parsed.runtime)/$($parsed.architecture)/$($parsed.protocol)"
            if ($null -ne $platform -and $platform -ne $identity) { throw 'Mixed environment or protocol.' }
            $platform = $identity
            if ($assemblyDigests.ContainsKey($version) -and $assemblyDigests[$version] -ne $parsed.persistenceAssemblySha256) { throw 'Compiled source changed between rounds.' }
            $assemblyDigests[$version] = $parsed.persistenceAssemblySha256
            $all[$version] += $parsed.samples
        }
    }
    $cells = @()
    foreach ($history in @(0, 32, 128, 256, 512)) {
        foreach ($operation in @('Load', 'Save', 'Backup', 'Restore')) {
            $rows = @{}; $stats = @{}
            foreach ($version in $versions.Keys) {
                $rows[$version] = @($all[$version] | Where-Object { $_.totalHistory -eq $history -and $_.operation -eq $operation })
                if ($rows[$version].Count -ne $orders.Count) { throw 'Missing or duplicate repeated cell.' }
                $times = @($rows[$version].elapsedMs | Sort-Object)
                $stats[$version] = [ordered]@{ minMs = $times[0]; medianMs = (Median $times); maxMs = $times[-1]; samples = $orders.Count;
                    medianAllocatedBytes = (Median $rows[$version].allocatedBytes); readMetricsAvailable = $rows[$version][0].readMetricsAvailable;
                    successfulReadFiles = $rows[$version][0].successfulReadFiles; successfulReadBytes = $rows[$version][0].successfulReadBytes;
                    enumerationEntries = $rows[$version][0].enumerationEntries }
            }
            for ($i = 0; $i -lt $orders.Count; $i++) {
                foreach ($version in @('B', 'C')) {
                    foreach ($property in @('repetition', 'warmup', 'backups', 'prepared', 'outcome', 'error', 'observedRevision', 'beforeFiles', 'beforeBytes', 'afterFiles', 'filesystem', 'totalDiskBytes', 'seedDigest')) {
                        if ($rows.A[$i].$property -ne $rows[$version][$i].$property) { throw "Different acceptance/input/environment: $version/$history/$operation/$property." }
                    }
                }
                foreach ($property in @('readMetricsAvailable', 'successfulReadFiles', 'successfulReadBytes', 'enumerationEntries')) {
                    if (-not $rows.B[$i].readMetricsAvailable -or $rows.B[$i].$property -ne $rows.C[$i].$property) { throw 'B/C optimized logical reads changed.' }
                }
                if ($rows.A[$i].readMetricsAvailable) { throw 'Unexpected instrumentation of original A.' }
                if ($IsWindows -and $rows.C[$i].filesystem -ne 'NTFS') { throw 'Windows measurement requires NTFS.' }
            }
            $paired = @(for ($i = 0; $i -lt $orders.Count; $i++) { 100 * ($rows.C[$i].elapsedMs / $rows.B[$i].elapsedMs - 1) })
            $cells += [ordered]@{ totalHistory = $history; operation = $operation; outcome = $rows.C[0].outcome; error = $rows.C[0].error;
                A = $stats.A; B = $stats.B; C = $stats.C;
                aToBPercent = 100 * ($stats.B.medianMs / $stats.A.medianMs - 1);
                bToCPercent = 100 * ($stats.C.medianMs / $stats.B.medianMs - 1);
                aToCPercent = 100 * ($stats.C.medianMs / $stats.A.medianMs - 1);
                pairedBToCPercent = $paired; cSlowerThanBRounds = @($paired | Where-Object { $_ -gt 0 }).Count }
        }
    }
    $document = [ordered]@{ schemaVersion = 1; versions = $versions; assemblySha256 = $assemblyDigests; fixtureSha256 = $fixtureDigest;
        environment = $platform; roundOrders = $orders; repetitions = $orders.Count; measuredSamplesPerVersion = 120;
        sameAcceptanceAndSeeds = $true; sameBAndCLogicalIo = $true; aLogicalIoUnavailable = $true;
        quotaRelief = $false; desktopAcceptance = $false; physicalDeviceIoMeasured = $false; cells = $cells }
    $document | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json') -Encoding utf8
    $document | ConvertTo-Json -Depth 12
}
finally { Remove-Item -LiteralPath $owned -Recurse -Force }
