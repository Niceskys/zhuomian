param(
    [ValidateRange(1, 20)]
    [int]$Count = 3,

    [string]$OutputPath = "$PSScriptRoot/evidence/windows-11-26100-explorer-recovery-summary.json"
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ZhuomianExplorerRecoveryNative
{
    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
'@

function Get-ShellProcessId {
    $window = [ZhuomianExplorerRecoveryNative]::GetShellWindow()
    if ($window -eq [IntPtr]::Zero) { return [uint32]0 }

    $processId = [uint32]0
    $threadId = [ZhuomianExplorerRecoveryNative]::GetWindowThreadProcessId(
        $window,
        [ref]$processId)
    if ($threadId -eq 0) { return [uint32]0 }
    return $processId
}

function Wait-ForNewShell([uint32]$PreviousProcessId, [TimeSpan]$Timeout) {
    $deadline = [DateTimeOffset]::UtcNow + $Timeout
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $current = Get-ShellProcessId
        if ($current -ne 0 -and $current -ne $PreviousProcessId) { return $true }
        Start-Sleep -Milliseconds 100
    }

    return $false
}

$project = Join-Path $PSScriptRoot 'Zhuomian.Spike.ExplorerRecovery/Zhuomian.Spike.ExplorerRecovery.csproj'
dotnet build $project --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$executable = Join-Path $PSScriptRoot 'Zhuomian.Spike.ExplorerRecovery/bin/Release/net9.0-windows10.0.19041.0/Zhuomian.Spike.ExplorerRecovery.exe'
$explorerPath = [IO.Path]::GetFullPath((Join-Path $env:WINDIR 'explorer.exe'))
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = Join-Path $temporaryBase ("zhuomian-explorer-recovery-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null

$runs = [System.Collections.Generic.List[object]]::new()
$firstEvidence = $null

try {
    for ($index = 1; $index -le $Count; $index++) {
        $readyPath = Join-Path $temporaryRoot "ready-$index.json"
        $evidencePath = Join-Path $temporaryRoot "evidence-$index.json"
        $stdoutPath = Join-Path $temporaryRoot "stdout-$index.txt"
        $stderrPath = Join-Path $temporaryRoot "stderr-$index.txt"
        $probe = $null

        try {
            $probe = Start-Process `
                -FilePath $executable `
                -ArgumentList @('--output', $evidencePath, '--ready', $readyPath) `
                -RedirectStandardOutput $stdoutPath `
                -RedirectStandardError $stderrPath `
                -WindowStyle Hidden `
                -PassThru

            $readyDeadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
            while (-not (Test-Path -LiteralPath $readyPath) -and
                -not $probe.HasExited -and
                [DateTimeOffset]::UtcNow -lt $readyDeadline) {
                Start-Sleep -Milliseconds 100
            }

            if (-not (Test-Path -LiteralPath $readyPath)) {
                throw "Recovery probe did not become ready."
            }

            $ready = Get-Content -Raw -LiteralPath $readyPath | ConvertFrom-Json
            if (-not $ready.Ready -or [uint32]$ready.ShellProcessId -eq 0) {
                throw "Recovery probe emitted an invalid ready signal."
            }

            $shellProcess = Get-Process -Id ([int]$ready.ShellProcessId) -ErrorAction Stop
            $actualPath = [IO.Path]::GetFullPath($shellProcess.Path)
            if ($shellProcess.ProcessName -ne 'explorer' -or
                -not [string]::Equals($actualPath, $explorerPath, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Resolved Shell process was not the signed-in user's Windows Explorer."
            }

            Stop-Process -Id $shellProcess.Id -Force
            $null = $shellProcess.WaitForExit(5000)

            $autoRestarted = Wait-ForNewShell `
                -PreviousProcessId ([uint32]$ready.ShellProcessId) `
                -Timeout ([TimeSpan]::FromSeconds(2))
            if (-not $autoRestarted) {
                Start-Process -FilePath $explorerPath -WindowStyle Hidden
            }

            $shellRestored = Wait-ForNewShell `
                -PreviousProcessId ([uint32]$ready.ShellProcessId) `
                -Timeout ([TimeSpan]::FromSeconds(10))
            if (-not $shellRestored) {
                throw "Windows Explorer did not restore its Shell window."
            }

            if (-not $probe.WaitForExit(30000)) {
                throw "Recovery probe exceeded its completion timeout."
            }

            if (-not (Test-Path -LiteralPath $evidencePath)) {
                throw "Recovery probe did not write evidence."
            }

            $evidence = Get-Content -Raw -LiteralPath $evidencePath | ConvertFrom-Json
            if ($null -eq $firstEvidence) { $firstEvidence = $evidence }
            $runs.Add([ordered]@{
                run = $index
                exitCode = $probe.ExitCode
                passed = [bool]$evidence.Passed
                recoveryMilliseconds = [double]$evidence.RecoveryMilliseconds
                taskbarCreatedReceived = [bool]$evidence.TaskbarCreatedWasReceived
                failedChecks = @($evidence.FailedChecks)
            })
        }
        finally {
            if ($null -ne $probe -and -not $probe.HasExited) {
                Stop-Process -Id $probe.Id -Force -ErrorAction SilentlyContinue
            }

            if ((Get-ShellProcessId) -eq 0) {
                Start-Process -FilePath $explorerPath -WindowStyle Hidden
                Wait-ForNewShell -PreviousProcessId 0 -Timeout ([TimeSpan]::FromSeconds(10)) | Out-Null
            }
        }
    }

    $failedRuns = @($runs | Where-Object { -not $_.passed }).Count
    $recoveryValues = @($runs | ForEach-Object recoveryMilliseconds)
    $summary = [ordered]@{
        schemaVersion = 1
        timestampUtc = [DateTimeOffset]::UtcNow.ToString('O')
        osVersion = $firstEvidence.OsVersion
        processArchitecture = $firstEvidence.ProcessArchitecture
        requestedRuns = $Count
        passedRuns = @($runs | Where-Object passed).Count
        failedRuns = $failedRuns
        passed = $failedRuns -eq 0
        recoveryBudgetMilliseconds = 5000
        minimumRecoveryMilliseconds = ($recoveryValues | Measure-Object -Minimum).Minimum
        maximumRecoveryMilliseconds = ($recoveryValues | Measure-Object -Maximum).Maximum
        taskbarCreatedObservedRuns = @($runs | Where-Object taskbarCreatedReceived).Count
        runs = $runs
        validatedFinalState = [ordered]@{
            desktopAvailability = $firstEvidence.FinalDesktopAvailability
            intentGate = $firstEvidence.FinalIntentGate
            keyboardMode = $firstEvidence.FinalKeyboardMode
            spaceState = $firstEvidence.FinalSpaceState
        }
        validatedBehavior = [ordered]@{
            hostLostDetected = [bool]$firstEvidence.HostLostWasDetected
            initialHostDestroyedAfterLoss = [bool]$firstEvidence.InitialHostWasDestroyedAfterLoss
            taskbarCreatedReceived = [bool]$firstEvidence.TaskbarCreatedWasReceived
            newShellGenerationDetected = [bool]$firstEvidence.NewShellGenerationWasDetected
            newHostCreated = [bool]$firstEvidence.NewHostWasCreated
            newHostNoActivate = [bool]$firstEvidence.NewHostWasNoActivate
            newHostNeverForeground = [bool]$firstEvidence.NewHostNeverBecameForeground
            inputCaptureReleased = [bool]$firstEvidence.InputCaptureWasReleased
            animationCallbacksStopped = [bool]$firstEvidence.AnimationCallbacksWereStopped
            mediaResourcesReleased = [bool]$firstEvidence.MediaResourcesWereReleased
            explorerAvailableAtEnd = [bool]$firstEvidence.ExplorerWasAvailableAtEnd
            allProbeWindowsDestroyed = [bool]$firstEvidence.AllProbeWindowsWereDestroyed
        }
        limitations = @($firstEvidence.Limitations)
    }

    $fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
    $outputDirectory = Split-Path -Parent $fullOutputPath
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fullOutputPath -Encoding utf8NoBOM
    $summary | ConvertTo-Json -Depth 8

    if (-not $summary.passed) { exit 1 }
}
finally {
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    if ($resolvedTemporaryRoot.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase) -and
        $resolvedTemporaryRoot.Length -gt $temporaryBase.Length) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
