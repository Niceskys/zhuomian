[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ExecutablePath,

    [string[]]$ArgumentList = @(),

    [string]$WorkingDirectory,

    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [ValidateRange(0, 3600)]
    [int]$WarmupSeconds = 60,

    [ValidateRange(1, 86400)]
    [int]$MeasurementSeconds = 300,

    [ValidateRange(50, 60000)]
    [int]$SampleIntervalMilliseconds = 1000,

    [ValidateRange(1, 100)]
    [int]$Repetitions = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Stop-OwnedProcess {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process]$Process
    )

    if ($Process.HasExited) {
        return
    }

    $Process.Kill($true)
    if (-not $Process.WaitForExit(5000)) {
        throw "Failed to terminate sampled process tree within 5 seconds."
    }
}

if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
    throw "Executable not found: $ExecutablePath"
}

$resolvedExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path

if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    $resolvedWorkingDirectory = Split-Path -Parent $resolvedExecutablePath
}
else {
    if (-not (Test-Path -LiteralPath $WorkingDirectory -PathType Container)) {
        throw "Working directory not found: $WorkingDirectory"
    }

    $resolvedWorkingDirectory = (Resolve-Path -LiteralPath $WorkingDirectory).Path
}

if (Test-Path -LiteralPath $OutputDirectory) {
    if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container)) {
        throw "Output path exists but is not a directory: $OutputDirectory"
    }

    if (@(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -ne 0) {
        throw "Output directory must be empty so samples from different runs cannot be mixed."
    }
}
else {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

$resolvedOutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$rawResultFiles = [System.Collections.Generic.List[string]]::new()
$sampleCounts = [System.Collections.Generic.List[int]]::new()
$launchedProcessIds = [System.Collections.Generic.List[int]]::new()
$culture = [Globalization.CultureInfo]::InvariantCulture
$processorCount = [Environment]::ProcessorCount

for ($run = 1; $run -le $Repetitions; $run++) {
    $process = $null
    $processStarted = $false

    try {
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $resolvedExecutablePath
        $startInfo.WorkingDirectory = $resolvedWorkingDirectory
        $startInfo.UseShellExecute = $false

        foreach ($argument in $ArgumentList) {
            $startInfo.ArgumentList.Add($argument)
        }

        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = $startInfo

        if (-not $process.Start()) {
            throw "Failed to start sampled process for repetition $run."
        }

        $processStarted = $true
        $launchedProcessIds.Add($process.Id)

        if ($WarmupSeconds -gt 0) {
            if ($process.WaitForExit($WarmupSeconds * 1000)) {
                throw "Sampled process exited during warm-up for repetition $run."
            }
        }
        elseif ($process.HasExited) {
            throw "Sampled process exited before measurement for repetition $run."
        }

        $process.Refresh()
        $previousCpu = $process.TotalProcessorTime
        $previousTimestamp = [System.Diagnostics.Stopwatch]::GetTimestamp()
        $measurementStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $rows = [System.Collections.Generic.List[string]]::new()
        $rows.Add('timestampUtc,elapsedMilliseconds,cpuPercent,privateBytes,workingSetBytes,handleCount,threadCount')

        while ($measurementStopwatch.Elapsed.TotalSeconds -lt $MeasurementSeconds) {
            $remainingMilliseconds = ($MeasurementSeconds * 1000.0) - $measurementStopwatch.Elapsed.TotalMilliseconds
            $sleepMilliseconds = [Math]::Min($SampleIntervalMilliseconds, [Math]::Max(1, [Math]::Ceiling($remainingMilliseconds)))
            Start-Sleep -Milliseconds ([int]$sleepMilliseconds)

            if ($process.HasExited) {
                throw "Sampled process exited during measurement for repetition $run."
            }

            $process.Refresh()
            $currentTimestamp = [System.Diagnostics.Stopwatch]::GetTimestamp()
            $currentCpu = $process.TotalProcessorTime
            $wallMilliseconds = (($currentTimestamp - $previousTimestamp) * 1000.0) / [System.Diagnostics.Stopwatch]::Frequency

            if ($wallMilliseconds -le 0) {
                throw "Non-positive sampling interval observed for repetition $run."
            }

            $cpuDeltaMilliseconds = ($currentCpu - $previousCpu).TotalMilliseconds
            $cpuPercent = ($cpuDeltaMilliseconds / ($wallMilliseconds * $processorCount)) * 100.0
            if ($cpuPercent -lt 0 -or [double]::IsNaN($cpuPercent) -or [double]::IsInfinity($cpuPercent)) {
                throw "Invalid CPU sample observed for repetition $run."
            }

            $elapsedMilliseconds = $measurementStopwatch.Elapsed.TotalMilliseconds
            $timestampUtc = [DateTimeOffset]::UtcNow.ToString('O', $culture)
            $rows.Add([string]::Format(
                    $culture,
                    '{0},{1:F3},{2:F6},{3},{4},{5},{6}',
                    $timestampUtc,
                    $elapsedMilliseconds,
                    $cpuPercent,
                    $process.PrivateMemorySize64,
                    $process.WorkingSet64,
                    $process.HandleCount,
                    $process.Threads.Count))

            $previousCpu = $currentCpu
            $previousTimestamp = $currentTimestamp
        }

        if ($rows.Count -le 1) {
            throw "No performance samples were collected for repetition $run."
        }

        $relativeRawPath = 'run-{0:D2}.csv' -f $run
        $rawPath = Join-Path $resolvedOutputDirectory $relativeRawPath
        $rows | Set-Content -LiteralPath $rawPath -Encoding utf8

        $rawResultFiles.Add($relativeRawPath)
        $sampleCounts.Add($rows.Count - 1)
    }
    finally {
        if ($null -ne $process) {
            try {
                if ($processStarted) {
                    Stop-OwnedProcess -Process $process
                }
            }
            finally {
                $process.Dispose()
            }
        }
    }
}

[pscustomobject]@{
    TargetFileName = [IO.Path]::GetFileName($resolvedExecutablePath)
    OutputDirectory = $resolvedOutputDirectory
    WarmupSeconds = $WarmupSeconds
    MeasurementSeconds = $MeasurementSeconds
    SampleIntervalMilliseconds = $SampleIntervalMilliseconds
    Repetitions = $Repetitions
    ProcessorCount = $processorCount
    RawResultFiles = $rawResultFiles.ToArray()
    SampleCounts = $sampleCounts.ToArray()
    LaunchedProcessIds = $launchedProcessIds.ToArray()
}
