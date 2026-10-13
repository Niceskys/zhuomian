param(
    [Parameter(Mandatory)][string]$ResultsDirectory,
    [Parameter(Mandatory)][string]$OutputPath,
    [Parameter(Mandatory)][string]$CommitSha
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Native persistence evidence requires Windows.' }
$trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File)
$selected = @($trxFiles | Where-Object {
    [xml]$candidate = Get-Content -LiteralPath $_.FullName -Raw
    @($candidate.TestRun.Results.UnitTestResult | Where-Object { $_.testName -match '^Zhuomian.Persistence.Tests\.' }).Count -gt 0
})
if ($selected.Count -ne 1) { throw 'Expected exactly one persistence TRX.' }
[xml]$trx = Get-Content -LiteralPath $selected[0].FullName -Raw
$counters = $trx.TestRun.ResultSummary.Counters
if ([int]$counters.failed -ne 0 -or [int]$counters.notExecuted -ne 0 -or [int]$counters.passed -ne [int]$counters.total -or [int]$counters.total -ne 165) {
    throw 'Persistence suite incomplete, failed or skipped.'
}
$baseline = [ordered]@{}
foreach ($suite in @(@{ name = 'Zhuomian.Core.Tests'; count = 451 }, @{ name = 'Zhuomian.Launching.Windows.Tests'; count = 37 })) {
    $matches = @($trxFiles | Where-Object {
        [xml]$candidate = Get-Content -LiteralPath $_.FullName -Raw
        @($candidate.TestRun.Results.UnitTestResult | Where-Object { $_.testName.StartsWith($suite.name + '.', [StringComparison]::Ordinal) }).Count -gt 0
    })
    if ($matches.Count -ne 1) { throw 'Expected exactly one original-suite TRX.' }
    [xml]$original = Get-Content -LiteralPath $matches[0].FullName -Raw
    $count = $original.TestRun.ResultSummary.Counters
    if ([int]$count.total -ne $suite.count -or [int]$count.passed -ne $suite.count -or [int]$count.failed -ne 0 -or [int]$count.notExecuted -ne 0) {
        throw 'Original test suite was reduced, skipped or failed.'
    }
    $baseline[$suite.name] = [int]$count.passed
}
$results = @($trx.TestRun.Results.UnitTestResult)
$originalPersistence = @($results | Where-Object { $_.testName -match '^Zhuomian.Persistence.Tests\.(CodecTests|FileStoreTests|WindowsStorageTests)\.' })
if ($originalPersistence.Count -ne 103 -or @($originalPersistence | Where-Object { $_.outcome -ne 'Passed' }).Count -ne 0) {
    throw 'The 103 original persistence assertions must remain passing.'
}
$originalLifecycle = @($results | Where-Object { $_.testName -match '^Zhuomian.Persistence.Tests\.(StorageLifecycleTests|WindowsLifecycleTests)\.' })
if ($originalLifecycle.Count -ne 46 -or @($originalLifecycle | Where-Object { $_.outcome -ne 'Passed' }).Count -ne 0) {
    throw 'The 46 original lifecycle assertions must remain passing.'
}
$history = @($results | Where-Object { $_.testName -match '^Zhuomian.Persistence.Tests\.HistorySafetyTests\.' })
if ($history.Count -ne 16 -or @($history | Where-Object { $_.outcome -ne 'Passed' }).Count -ne 0) {
    throw 'The 16 new history safety assertions must all execute and pass.'
}
$capacityResult = @($history | Where-Object { $_.testName -match 'OneThousandRealSaveAttempts' })
$capacityOutput = [string]$capacityResult[0].Output.StdOut
$capacityLine = @($capacityOutput -split "`n" | Where-Object { $_.StartsWith('E3_4_CAPACITY=', [StringComparison]::Ordinal) })
if ($capacityLine.Count -ne 1) { throw 'Missing actual capacity measurement.' }
$capacity = $capacityLine[0].Substring('E3_4_CAPACITY='.Length) | ConvertFrom-Json
if ($capacity.attempts -ne 1000 -or $capacity.committed -ne 256 -or $capacity.refused -ne 744 -or $capacity.quotaRelief) {
    throw 'Capacity evidence must retain unresolved quota rather than fabricate success.'
}
$lifecycleAssertions = @(
    'InventoryClassifiesAllOwnedFamiliesAndExposesOnlyRedactedDiagnostics',
    'DamagedHistoryFailsClosedAndExactEvidenceIsPreserved',
    'UnsupportedAndReadOnlySnapshotsCannotAuthorizeMaintenanceOrExport',
    'QuotaPressureNeverDeletesToMakeRoom',
    'InventoryScanBudgetIsBoundedEvenAcrossDifferentFamilies',
    'DryRunRetainsMainAllGenerationsReservationsAndUnfinishedEvidence',
    'EqualRevisionDifferentContentsInHistoryBlockPlanning',
    'RecoveryThenMaintenanceKeepsReservationFloorAndCorruptEvidence',
    'ExportPlanValidatesHighestBackupExactHashAndNeverWritesOrMoves',
    'SaveFaultsAndSimulatedFullDiskRemainRecoverableAfterMaintenanceRefusal',
    'ActualSecondProcessLockBlocksInventoryAndPlanValidation',
    'KilledLockOwnerReleasesMutexWithoutChangingRecoveryAssets',
    'MixedConcurrentSaveRestoreInventoryAndDisabledApplyPreserveRecovery',
    'ActualWindowsInventoryRejectsHardlinkedHistoryWithoutTouchingSource',
    'ActualWindowsSameBytesReplacementInvalidatesNativeFileIdentity',
    'ActualWindowsSharedReadDenialFailsClosedAndPreservesAllAssets',
    'ActualWindowsJunctionAndDirectoryAliasesFailClosedDuringInventory',
    'ActualWindowsInventoryAccessDenialIsRedactedAndRestoresFixtureAcl',
    'ActualWindowsInventoryPinsFilesAgainstConcurrentReplacementAndDirectoriesAgainstRename'
)
$required = @(
    'ActualDiskCreateReplaceLoadBackupAndRestorePreserveSourceResources',
    'InjectedWriteFlushReplaceAndDiskFullFailuresKeepPreviousMainAndGoodBackups',
    'CorruptPrimaryLoadsGoodBackupWithoutRewritingEvidenceThenExplicitlyRestores',
    'ActualWindowsDirectoryPinsPreventRenameDuringTransactionAndReleaseAfterward',
    'ActualWindowsPostReplaceReadDenialReportsUnknownCommitThenAllowsSafeRetry',
    'ActualWindowsReplacementFailureRetainsMainAndDoesNotOverwriteLockedSource',
    'ActualWindowsWritePermissionDenialIsStructuredAndDoesNotMutateAclOrMain',
    'ActualWindowsJunctionCannotRedirectStorageIntoAnotherOwnedTarget',
    'ActualWindowsHardLinkedMainAndLockAreRejectedWithoutModifyingSource',
    'ActualWindowsLockedExternalResourcesAndIconsAreNotReadWrittenOrDeleted',
    'ActualSecondProcessOwnsLockAndCannotBeBypassedByAnotherStore',
    'SymbolicLeafAndStorageRootNeverRedirectWritesToExternalData',
    'DanglingStorageRootLinkCannotCreateAnExternalTarget'
)
foreach ($name in ($required + $lifecycleAssertions)) {
    $matching = @($results | Where-Object { $_.testName -match [regex]::Escape($name) })
    if ($matching.Count -eq 0 -or @($matching | Where-Object { $_.outcome -ne 'Passed' }).Count -gt 0) {
        throw "Missing/failing required persistence assertion: $name"
    }
}
$sdk = dotnet --version
if ($LASTEXITCODE -ne 0) { throw "SDK query failed (exit $LASTEXITCODE)." }
$drive = [System.IO.DriveInfo]::new([System.IO.Path]::GetPathRoot([System.IO.Path]::GetTempPath()))
if ($drive.DriveFormat -ne 'NTFS') { throw 'Native fixture volume must be NTFS.' }
$evidence = [ordered]@{
    evidenceVersion = 3
    historySafetyCount = $history.Count
    capacityExperiment = $capacity
    commitSha = $CommitSha
    windowsBuild = [Environment]::OSVersion.Version.ToString()
    sdk = $sdk.Trim()
    architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    filesystem = $drive.DriveFormat
    persistenceProject = 'Zhuomian.Persistence'
    persistenceTestProject = 'Zhuomian.Persistence.Tests'
    total = [int]$counters.total
    passed = [int]$counters.passed
    failed = [int]$counters.failed
    skipped = [int]$counters.notExecuted
    originalSuiteCounts = $baseline
    nativeAssertions = $required
    lifecycleAssertions = $lifecycleAssertions
    originalPersistenceCount = $originalPersistence.Count
    lifecycleCount = 46
    invariantOutcomes = @($results | Where-Object { $_.testName -match '^Zhuomian.Persistence.Tests\.(StorageLifecycleTests|WindowsLifecycleTests)\.' } |
        ForEach-Object { [ordered]@{ testName = $_.testName; outcome = $_.outcome } })
    lifecycleSupport = [ordered]@{
        inventory = 'bounded, fail closed, redacted'
        retentionPlan = 'retain all; zero reclaim; in-process store-bound validation'
        apply = 'disabled; no deletion protocol'
        export = 'local source plan/hash validation only; external writing disabled'
        quotaRelief = $false
        cleanupCrashInjection = 'not applicable: Apply has no mutation stages'
        rollback = 'no lifecycle mutation; exact before/after bytes asserted; killed lock fixture restart verified'
    }
    faultOutcomes = @($results | ForEach-Object { [ordered]@{ testName = $_.testName; outcome = $_.outcome } })
    fixtureOwnership = 'private generated temporary roots only'
    injectedFailures = 'before write, flush, replace and after replace; simulated disk-full HRESULT'
    windows11DesktopAcceptance = $false
    physicalDiskFullExperiment = $false
    powerCutExperiment = $false
    absolutePowerLossGuarantee = $false
}
$evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "Persistence native evidence: $($counters.passed)/$($counters.total), 0 failed/skipped; NTFS; no physical power-cut claim."
