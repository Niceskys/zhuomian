param(
    [ValidateRange(1, 50)]
    [int]$Count = 20,

    [string]$OutputPath = "$PSScriptRoot/evidence/windows-11-26100-enhanced-fallback-summary.json"
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'Zhuomian.Spike.EnhancedHosting/Zhuomian.Spike.EnhancedHosting.csproj'
dotnet build $project --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$runs = [System.Collections.Generic.List[object]]::new()
$firstEvidence = $null

for ($index = 1; $index -le $Count; $index++) {
    $arguments = @('run', '--project', $project, '--configuration', 'Release', '--no-build', '--')
    if ($index -eq 1) { $arguments += '--request-private-worker' }
    $raw = & dotnet @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $json = $raw -join [Environment]::NewLine
    $evidence = $json | ConvertFrom-Json

    if ($null -eq $firstEvidence) { $firstEvidence = $evidence }

    $runs.Add([ordered]@{
        run = $index
        exitCode = $exitCode
        passed = [bool]$evidence.Passed
        viableWorkerWCount = [int]$evidence.ViableWorkerWCountAfterRequest
        selectedHostMode = $evidence.SelectedHostMode
        automaticFallbackOccurred = [bool]$evidence.AutomaticFallbackOccurred
        crossProcessSetParentAttempted = [bool]$evidence.CrossProcessSetParentAttempted
        failedChecks = @($evidence.FailedChecks)
    })
}

$failedRuns = @($runs | Where-Object { -not $_.passed }).Count
$summary = [ordered]@{
    schemaVersion = 1
    timestampUtc = [DateTimeOffset]::UtcNow.ToString('O')
    osVersion = $firstEvidence.OsVersion
    processArchitecture = $firstEvidence.ProcessArchitecture
    requestedRuns = $Count
    passedRuns = @($runs | Where-Object passed).Count
    failedRuns = $failedRuns
    passed = $failedRuns -eq 0
    workerWCountBeforePrivateRequest = [int]$firstEvidence.WorkerWCountBeforeRequest
    visibleWorkerWCountBeforePrivateRequest = [int]$firstEvidence.VisibleWorkerWCountBeforeRequest
    viableWorkerWCountBeforePrivateRequest = [int]$firstEvidence.ViableWorkerWCountBeforeRequest
    privateWorkerRequestDelivered = [bool]$firstEvidence.PrivateWorkerRequestDelivered
    workerWCountAfterPrivateRequest = [int]$firstEvidence.WorkerWCountAfterRequest
    visibleWorkerWCountAfterPrivateRequest = [int]$firstEvidence.VisibleWorkerWCountAfterRequest
    viableWorkerWCountAfterPrivateRequest = [int]$firstEvidence.ViableWorkerWCountAfterRequest
    privateRequestProducedViableCandidate = [bool]$firstEvidence.PrivateRequestProducedViableCandidate
    fallbackRuns = @($runs | Where-Object automaticFallbackOccurred).Count
    crossProcessSetParentAttempts = @($runs | Where-Object crossProcessSetParentAttempted).Count
    policyChecks = @($firstEvidence.PolicyChecks)
    validatedFallback = [ordered]@{
        shownWithoutActivation = [bool]$firstEvidence.FallbackShownWithoutActivation
        noActivate = [bool]$firstEvidence.FallbackWasNoActivate
        notTopMost = [bool]$firstEvidence.FallbackWasNotTopMost
        borderless = [bool]$firstEvidence.FallbackWasBorderless
        clientAreaMatchedWindow = [bool]$firstEvidence.FallbackClientAreaMatchedWindow
        matchedWorkArea = [bool]$firstEvidence.FallbackMatchedWorkArea
        dpiAvailable = [bool]$firstEvidence.FallbackDpiWasAvailable
        destroyed = [bool]$firstEvidence.FallbackWasDestroyed
    }
    runs = $runs
    enhancedRejectionReasons = @($firstEvidence.EnhancedRejectionReasons)
    limitations = @($firstEvidence.Limitations)
}

$fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $fullOutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fullOutputPath -Encoding utf8NoBOM
$summary | ConvertTo-Json -Depth 8

if (-not $summary.passed) { exit 1 }
