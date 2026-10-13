param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$heads = @('6687bc39d060029e8f0a692419694ffa3c5a7a29', 'ec43366ab3b86b54bac9e311b5f7d9ebb2fa9b6d')
foreach ($head in $heads) {
    $owned = Join-Path ([IO.Path]::GetTempPath()) ('zhuomian-legacy-source-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $owned | Out-Null
    try {
        $zip = Join-Path $owned 'source.zip'
        git -C $repo archive --format=zip --output=$zip $head
        if ($LASTEXITCODE -ne 0) { throw 'Immutable legacy source unavailable; do not substitute current implementation.' }
        Expand-Archive -LiteralPath $zip -DestinationPath $owned
        $probe = Join-Path $owned 'LegacyProbe'
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LegacyProbe') -Destination $probe -Recurse
        $project = Join-Path $probe 'LegacyProbe.csproj'
        dotnet restore $project "-p:LegacySourceRoot=$owned"
        if ($LASTEXITCODE -ne 0) { throw 'Legacy restore failed.' }
        dotnet build $project --no-restore -c Release "-p:LegacySourceRoot=$owned"
        if ($LASTEXITCODE -ne 0) { throw 'Legacy build failed.' }
        $fixture = Join-Path $owned 'tests/Zhuomian.Persistence.Tests/Fixtures/workspace-v1.json'
        $json = dotnet (Join-Path $probe 'bin/Release/net9.0/LegacyProbe.dll') $fixture $head
        if ($LASTEXITCODE -ne 0) { throw 'Actual old-process compatibility assertions failed.' }
        $parsed = $json | ConvertFrom-Json
        if ($parsed.cases.Count -ne 12 -or @($parsed.cases | Where-Object rollback).Count -ne 3) { throw 'Incomplete compatibility matrix.' }
        $json | Set-Content -LiteralPath (Join-Path $OutputDirectory "$head.json") -Encoding utf8
        if ($head -eq 'ec43366ab3b86b54bac9e311b5f7d9ebb2fa9b6d') {
            $capacity = dotnet (Join-Path $probe 'bin/Release/net9.0/LegacyProbe.dll') $fixture $head capacity
            if ($LASTEXITCODE -ne 0) { throw 'Baseline capacity assertions failed.' }
            $capacity | Set-Content -LiteralPath (Join-Path $OutputDirectory 'capacity-before.json') -Encoding utf8
        }
        Write-Host "Legacy $head : 12 executed cases; 3 expected unsafe main-only downgrades; migration remains disabled."
    }
    finally { Remove-Item -LiteralPath $owned -Recurse -Force }
}

$owned = Join-Path ([IO.Path]::GetTempPath()) ('zhuomian-current-probe-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $owned | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LegacyProbe') -Destination (Join-Path $owned 'LegacyProbe') -Recurse
    Copy-Item -LiteralPath (Join-Path $repo 'Directory.Build.props') -Destination $owned
    Copy-Item -LiteralPath (Join-Path $repo '.editorconfig') -Destination $owned
    $project = Join-Path $owned 'LegacyProbe/LegacyProbe.csproj'
    dotnet restore $project "-p:LegacySourceRoot=$repo"
    if ($LASTEXITCODE -ne 0) { throw 'Current capacity probe restore failed.' }
    dotnet build $project --no-restore -c Release "-p:LegacySourceRoot=$repo"
    if ($LASTEXITCODE -ne 0) { throw 'Current capacity probe build failed.' }
    $previousRoot = $env:LegacySourceRoot
    try {
        $env:LegacySourceRoot = $repo
        dotnet format $project --no-restore --verify-no-changes
        if ($LASTEXITCODE -ne 0) { throw 'Complete current probe format failed.' }
    }
    finally { $env:LegacySourceRoot = $previousRoot }
    $fixture = Join-Path $repo 'tests/Zhuomian.Persistence.Tests/Fixtures/workspace-v1.json'
    $head = git -C $repo rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Current SHA query failed.' }
    $capacity = dotnet (Join-Path $owned 'LegacyProbe/bin/Release/net9.0/LegacyProbe.dll') $fixture $head capacity
    if ($LASTEXITCODE -ne 0) { throw 'Current capacity assertions failed.' }
    $capacity | Set-Content -LiteralPath (Join-Path $OutputDirectory 'capacity-after.json') -Encoding utf8
}
finally { Remove-Item -LiteralPath $owned -Recurse -Force }
