[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Deliberately exercise the unsafe native-error preference as well as PS 7's opt-in mode.
$PSNativeCommandUseErrorActionPreference = $false
$workflowPath = Join-Path $PSScriptRoot '../.github/workflows/ci.yml'
$workflow = Get-Content -LiteralPath $workflowPath -Raw
$match = [regex]::Match($workflow, '(?ms)^      - name: Restore and test \.NET solution\r?\n.*?        run: \|\r?\n(?<body>(?:          [^\r\n]*\r?\n)+)')
if (-not $match.Success) { throw 'Cannot locate the actual .NET workflow body.' }
$body = $match.Groups['body'].Value -replace '(?m)^          ', ''
$legacyBody = $body -replace '(?m)^\s*if \(\$LASTEXITCODE -ne 0\).*\r?\n', ''

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("zhuomian-ci-exit-{0}" -f [guid]::NewGuid())
$oldPath = $env:PATH
$oldRunnerTemp = $env:RUNNER_TEMP
$pwshPath = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
    # A native shim, not a PowerShell mock: its exit status sets LASTEXITCODE.
    $stub = @'
$verb, $solution = $args[0..1]
$name = [IO.Path]::GetFileNameWithoutExtension($solution)
Add-Content -LiteralPath (Join-Path $PSScriptRoot 'calls.txt') -Value "$name/$verb"
$failure = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'failure.txt') -Raw
if ("$name/$verb" -eq $failure.Trim()) {
    [Console]::Error.WriteLine("fixture diagnostic: $name/$verb exit 23")
    exit 23
}
Write-Host "fixture success: $name/$verb"
exit 0
'@
    Set-Content -LiteralPath (Join-Path $tempRoot 'stub.ps1') -Value $stub
    if ($IsWindows) {
        $shim = '@echo off' + "`r`n" + '"' + $pwshPath + '" -NoLogo -NoProfile -NonInteractive -File "%~dp0stub.ps1" %*' + "`r`nexit /b %errorlevel%`r`n"
        Set-Content -LiteralPath (Join-Path $tempRoot 'dotnet.cmd') -Value $shim
    }
    else {
        $shellQuote = "'" + [char]34 + "'" + [char]34 + "'"
        $shim = @'
#!/bin/sh
exec '{0}' -NoLogo -NoProfile -NonInteractive -File '{1}' "$@"
'@ -f $pwshPath.Replace("'", $shellQuote), (Join-Path $tempRoot 'stub.ps1').Replace("'", $shellQuote)
        Set-Content -LiteralPath (Join-Path $tempRoot 'dotnet') -Value $shim
        & chmod +x (Join-Path $tempRoot 'dotnet')
        if ($LASTEXITCODE -ne 0) { throw 'Cannot make fixture executable.' }
    }
    $env:PATH = $tempRoot + [IO.Path]::PathSeparator + $oldPath
    $env:RUNNER_TEMP = $tempRoot
    'first', 'second' | ForEach-Object {
        New-Item -ItemType File -Path (Join-Path $tempRoot "$_.sln") | Out-Null
    }
    $callsPath = Join-Path $tempRoot 'calls.txt'
    $failurePath = Join-Path $tempRoot 'failure.txt'
    $verbs = @('restore', 'build', 'format', 'test')
    $expectedAll = @('first', 'second') | ForEach-Object {
        $name = $_
        $verbs | ForEach-Object { "$name/$_" }
    }
    function Invoke-Fixture {
        param([string]$ScriptBody, [string]$Failure, [bool]$NativePreference)
        Set-Content -LiteralPath $failurePath -Value $Failure
        Set-Content -LiteralPath $callsPath -Value '' -NoNewline
        # Mirror the runner's pwsh prologue, dot-sourcing and final exit-code epilogue.
        $prefix = '$ErrorActionPreference = "Stop"' + "`n" + '$PSNativeCommandUseErrorActionPreference = $' + $NativePreference.ToString().ToLowerInvariant() + "`n"
        Set-Content -LiteralPath (Join-Path $tempRoot 'step.ps1') -Value $ScriptBody
        $wrapper = $prefix + '. (Join-Path $PSScriptRoot "step.ps1")' + "`n" + 'if (Test-Path variable:LASTEXITCODE) { exit $LASTEXITCODE }'
        Set-Content -LiteralPath (Join-Path $tempRoot 'runner.ps1') -Value $wrapper
        Push-Location $tempRoot
        try {
            $output = & $pwshPath -NoLogo -NoProfile -NonInteractive -File (Join-Path $tempRoot 'runner.ps1') 2>&1
            $code = $LASTEXITCODE
        }
        finally { Pop-Location }
        return @{ Code = $code; Calls = @(Get-Content -LiteralPath $callsPath); Output = ($output -join "`n") }
    }

    $legacy = Invoke-Fixture $legacyBody 'first/format' $false
    if ($legacy.Code -ne 0 -or ($legacy.Calls -join ',') -ne ($expectedAll -join ',') -or $legacy.Output -notmatch 'fixture diagnostic: first/format exit 23') {
        throw 'Legacy format-failure/test-success masking was not reproduced.'
    }
    Write-Host 'Legacy reproduced: format exit 23 + later test exit 0 -> runner exit 0.'

    foreach ($nativePreference in @($false, $true)) {
        foreach ($failureIndex in 0..7) {
            $failure = $expectedAll[$failureIndex]
            $result = Invoke-Fixture $body $failure $nativePreference
            $expectedCalls = $expectedAll[0..$failureIndex]
            if ($result.Code -eq 0 -or ($result.Calls -join ',') -ne ($expectedCalls -join ',') -or $result.Output -notmatch "fixture diagnostic: $failure exit 23") {
                throw "Failure propagation/diagnostic lost: $failure, native preference $nativePreference."
            }
            if (-not $nativePreference -and $result.Output -notmatch "dotnet $($verbs[$failureIndex % 4]) failed") {
                throw "Missing command-specific failure context: $failure."
            }
            Write-Host "Verified $failure : runner exit $($result.Code), no subsequent commands; native preference $nativePreference."
        }
        $success = Invoke-Fixture $body '' $nativePreference
        if ($success.Code -ne 0 -or ($success.Calls -join ',') -ne ($expectedAll -join ',')) {
            throw "Successful two-solution path failed; native preference $nativePreference."
        }
    }
    Write-Host 'CI exit-code self-test passed: 16 failure injections, 2 success paths, 1 legacy reproduction.'
}
finally {
    $env:PATH = $oldPath
    $env:RUNNER_TEMP = $oldRunnerTemp
    Remove-Item -LiteralPath $tempRoot -Recurse -Force
}
