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

if (-not ('Zhuomian.Performance.ProcessJob' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Zhuomian.Performance
{
    public sealed class ProcessJob : IDisposable
    {
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private const int JobObjectExtendedLimitInformation = 9;

        private IntPtr _handle;

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(
            IntPtr job,
            int informationClass,
            ref JobObjectExtendedLimitInformation information,
            uint informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        public ProcessJob()
        {
            _handle = CreateJobObject(IntPtr.Zero, null);
            if (_handle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create performance-sampler Job Object.");
            }

            var information = new JobObjectExtendedLimitInformation();
            information.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

            if (!SetInformationJobObject(
                    _handle,
                    JobObjectExtendedLimitInformation,
                    ref information,
                    (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
            {
                var error = Marshal.GetLastWin32Error();
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
                throw new Win32Exception(error, "Failed to enable kill-on-close for performance-sampler Job Object.");
            }
        }

        public void Assign(Process process)
        {
            if (process == null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            if (_handle == IntPtr.Zero)
            {
                throw new ObjectDisposedException(nameof(ProcessJob));
            }

            if (!AssignProcessToJobObject(_handle, process.Handle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to assign sampled process to performance-sampler Job Object.");
            }
        }

        public void Dispose()
        {
            var handle = _handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            _handle = IntPtr.Zero;
            if (!CloseHandle(handle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to close performance-sampler Job Object.");
            }

            GC.SuppressFinalize(this);
        }

        ~ProcessJob()
        {
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
'@
}

function Close-OwnedJob {
    param(
        [Parameter(Mandatory)]
        [Zhuomian.Performance.ProcessJob]$Job,

        [Parameter(Mandatory)]
        [System.Diagnostics.Process]$Process,

        [Parameter(Mandatory)]
        [bool]$ProcessStarted
    )

    $Job.Dispose()

    if ($ProcessStarted -and -not $Process.HasExited) {
        if (-not $Process.WaitForExit(5000)) {
            throw "Failed to terminate sampled process after closing its Job Object within 5 seconds."
        }
    }
}

if (-not $IsWindows) {
    throw "The process sampler currently requires Windows because descendant containment uses a Windows Job Object."
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
    $job = $null
    $processStarted = $false

    try {
        $job = [Zhuomian.Performance.ProcessJob]::new()

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
        $job.Assign($process)

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
        if ($null -ne $job) {
            try {
                if ($null -ne $process) {
                    Close-OwnedJob -Job $job -Process $process -ProcessStarted $processStarted
                }
                else {
                    $job.Dispose()
                }
            }
            finally {
                if ($null -ne $process) {
                    $process.Dispose()
                }
            }
        }
        elseif ($null -ne $process) {
            $process.Dispose()
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
