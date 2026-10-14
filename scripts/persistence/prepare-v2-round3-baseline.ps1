param([Parameter(Mandatory = $true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$baseline = '514a5594f594728d2093cb041ed3552472384472'
$checkout = Join-Path $OutputDirectory 'source'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
git clone --shared --no-checkout (git rev-parse --show-toplevel) $checkout
if ($LASTEXITCODE -ne 0) { throw 'Baseline clone failed.' }
git -C $checkout checkout --detach $baseline
if ($LASTEXITCODE -ne 0) { throw 'Immutable reviewed baseline unavailable.' }
dotnet build (Join-Path $checkout 'src/Zhuomian.Persistence/Zhuomian.Persistence.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Baseline build failed.' }
$dll = Join-Path $checkout 'src/Zhuomian.Persistence/bin/Release/net9.0/Zhuomian.Persistence.dll'
"E3_V2_BASELINE_DLL=$dll" >> $env:GITHUB_ENV
New-Item -ItemType Directory -Path $env:E3_V2_EVIDENCE_DIR -Force | Out-Null
[ordered]@{ sourceSha = $baseline; assemblySha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash; generatedFixturesOnly = $true } |
    ConvertTo-Json | Set-Content -LiteralPath "$env:E3_V2_EVIDENCE_DIR/v3-baseline.json" -Encoding utf8
