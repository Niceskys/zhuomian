# E3-4A history safety increment and evidence

Baseline: latest main ec43366ab3b86b54bac9e311b5f7d9ebb2fa9b6d, confirmed #36 squash merge. Independent feature/e3-4-safe-history-compaction, target main. #31/#32/#33 remain separate unchanged Drafts. [ADR-0011](ADR/0011-safe-history-compaction.md) is Proposed; ADR-0009/0010 remain Proposed.

**E3-4 is not complete. The 256 quota is not relieved.** This delivers actual C# history conflict checks, native-bound reads, 16 new external-behavior tests, an executable immutable-old-code compatibility harness and reproducible capacity evidence. It does not deliver production archives, a new format, v1→v2 migration, archive crash recovery, or daily-use acceptance. Those are explicit merge/integration gates, not passing tests.

## Implemented support matrix

| Operation / disk | Implemented behavior | Remaining boundary |
|---|---|---|
| Save v1/schema1 | Main/backup/prepared ID+revision+canonical content checked; all older contradictions refuse; matching retry preserved; trusted high water includes backups | Unchanged 256/64 MiB per-family limits; no source-set coverage manifest |
| Backup | Complete understood conflict-free main/backup/prepared history required before adding a new immutable backup | No deletion or quota relief |
| Load | Readable main remains diagnostic view; incomplete/conflicting history gives error + read-only; partial/future rules retained | State alone does not imply writable/healthy; failed history cannot authorize rewriting |
| Restore | Valid backup recovery; complete understood history before mutation; floor above all trusted revisions; overflow refusal | At 256 prepared, adding restore reservation can fail; missing v1 records cannot be proved absent safely |
| Inventory / DryRun / ValidatePlan | Shared content witness, original stricter aggregate budget/namespace checks and fresh plan binding | Unknown/temp/evidence still block plans; no cold format exists |
| Apply / Export | AuthorizationRequired or MaintenanceDisabled, no filesystem mutation | Remain disabled regardless of true authorization |
| Proposed v2/archive | Unsupported, migration absent | Fence/generation/coverage/native-directory/crash proof required |
| Fixed Windows NTFS | Existing controlled engineering adapter; real CI required | Windows 11 default-profile/device/UI acceptance remains open |
| Linux fixtures | Owned temporary resources, algorithm and process verification | Does not prove NTFS/hardlink/Windows sharing/physical durability |

The commitment model is in [HistoryCommitments](../src/Zhuomian.Persistence/HistoryCommitments.cs); callers remain [WorkspaceFileStore](../src/Zhuomian.Persistence/WorkspaceFileStore.cs) and [StorageLifecycle](../src/Zhuomian.Persistence/StorageLifecycle.cs). It is an in-memory content witness, not a compacted index or authorization. Complete raw original files remain the recovery evidence. Unknown fields/objects are never rewritten by this increment. No external Item path/icon/managed asset is opened or removed.

## Tests and reproducibility

[HistorySafetyTests](../tests/Zhuomian.Persistence.Tests/HistorySafetyTests.cs) adds 16 cases: older prepared and backup contradictions; cross-family conflict with missing main; foreign Workspace history; corrupt/future/partial backup/prepared coverage; high backup watermark without a matching prepared record; gaps, canonical duplicate retry and equal-content/different-content decisions; overflow; and 1000 actual Save attempts with exact retained-asset hashes.

[Legacy probe](../scripts/persistence/LegacyProbe/Program.cs) compiles pinned original E3-2 and E3-3 source, uses the complete existing v1 fixture and runs each as an actual independent .NET process. Twelve cases per old source combine synthetic future main complete/corrupt/missing/leftover-v1/leftover-v1 and absent/future/corrupt prepared fence. The counterexample expects three unsafe Save/Restore downgrade scenarios per old source for a **main-only format bump with unrecognized cold history**. It does not claim actual v2 migration or full old-client write fencing. Original backup/prepared/raw cold bytes are asserted unchanged. A failed counterexample expectation exits nonzero; a regression cannot be called success.

[Runner](../scripts/persistence/test-legacy-history-compatibility.ps1) uses git archive on immutable SHAs and generated temporary roots. It also builds the same capacity executable against baseline and current code, in Release: same 1000 sequential tiny-workspace Save calls, same checkpoints 256/257/512/1000, same Load/Restore calls. Failed saves are measured and reported, not omitted. Current xUnit additionally records successful logical read files/bytes and wildcard enumeration entries; a separate retained-byte estimate is labeled as an estimate, not measured device I/O. File bytes are logical lengths, not allocated NTFS cluster size. Total/free volume bytes are environment observations, not configured reservation capacity.

CI retains every original docs/performance-tool/native-exit/restore/Release/complete-format/xUnit/native-evidence gate. Checkout includes immutable history. Collector requires 451 Core + 37 Launching + 165 Persistence = **653**, checks original 103 persistence and original 46 lifecycle cases independently and requires all 16 new cases plus actual capacity output. TRX, platform/native-fault outcomes, old-client matrices and capacity before/after JSON upload only generated fixture results; no personal configs or paths.

Local commands (.NET 9.0.318, Linux x64):

```text
dotnet restore Zhuomian.sln -p:EnableWindowsTargeting=true
dotnet build Zhuomian.sln --no-restore -c Release -p:EnableWindowsTargeting=true
EnableWindowsTargeting=true dotnet format Zhuomian.sln --no-restore --verify-no-changes
dotnet test Zhuomian.sln --no-build -c Release -p:EnableWindowsTargeting=true --logger trx --results-directory <owned-results>
LegacySourceRoot=<repo> dotnet format scripts/persistence/LegacyProbe/LegacyProbe.csproj --no-restore --verify-no-changes
pwsh scripts/persistence/test-legacy-history-compatibility.ps1 -OutputDirectory <owned-evidence>
git diff --check
```

Initial development build refused nullable filename dictionary keys (CS8714); fixed without suppressing analyzers. First persistence run passed **165/165**, zero failures/skips, before the final logical read counters and missing-main read-error assertion. Full final gates and Windows head are recorded in the PR to avoid a self-referential SHA.

## Results and limitations

The expected capacity result is **256 committed / 744 refused out of 1000 attempts**, not 1000 successful saves. At 256 the tiny workload retains 256 backups + 256 prepared + main + lock = 514 files. Identical revision 255 retry remains Unchanged; an old revision is StaleRevision; Restore refuses TooLarge rather than discarding a reservation or inventing success. Main/history bytes remain exact after quota refusals. Benchmarks will record timings and total volume capacity; these are cloud-test-environment observations only. No production archive exists, so archive copy cost/near-full temporary headroom are not measured or claimed.

Legacy local execution confirmed complete future main returns UnsupportedVersion on Load/Restore. Missing/corrupt future main with absent fence loads v1 backup and old Restore publishes a revision below hidden high water 1000. Future/corrupt prepared fence refuses the tested mutations. This supports rejecting the main-only migration candidate, not claiming the sentinel alone solves migration atomicity.

All original Windows native/process tests remain: NTFS root/ancestor/leaf pinning, reparse/junction/hardlink/symlink/alias rejection, path replacement/rename refusal, share/ACL denial, post-submission Unknown commit, disk-full/write/Flush/Replace fault injection, child-process lock competition and killed-owner restart. Those test **existing single-main-file transactions**, not new archive/index/migration cut points. No physical disk-full or power-cut experiment is performed. Two competing storage processes beyond the existing lock-holder fixture, process abort at every archive/index stage, and new archive native-directory ownership are still open.

v1 has no complete covered source-set manifest. It cannot detect an entirely missing historical record or hidden cold directory, or establish safe index repair. Temp/unknown/evidence handling also differs between v1 store and stricter Inventory. This increment does not certify a single new trustworthy state model or authorize compaction. Future implementation must close those gaps before relocating originals or enabling migration.

Rollback: revert only this increment and leave all bytes. Reverting restores old code semantics and loses the stronger history checks; do not use rollback to bypass conflict errors. Windows 11 real-user/default-root, physical power loss/device disk exhaustion, long real-device operation, product UI/E2/E4 integration and user acceptance remain blocking gates. The user has no Windows desktop available; cloud CI cannot substitute for that acceptance.

## Completed local full gate and measured capacity

2026-10-13, Debian GNU/Linux 13, x64, SDK 9.0.318: full restore, complete Release solution build, complete solution format verification, and all xUnit exit 0. Build has zero warnings/errors. Counts **451 + 37 + 165 = 653**, zero failures/skips. Original 637 case bodies, Core/Launching/Spike production sources and ADR-0009/0010 are byte-unchanged against ec43366. Native tests' explicit unsupported-platform paths on Linux do not certify NTFS. PowerShell documentation validator: 46 Markdown / 16 required files, exit 0. Complete additional probe format had an initial whitespace/import refusal; the probe was formatted and complete verification then passed, without suppressing style checks.

Actual current xUnit capacity measurement (owned temporary overlay volume): total capacity **33,770,192,896 bytes**, initially available **31,570,640,896 bytes**. Logical retained storage **124,950 bytes / 514 files**, unchanged after attempts 257–1000. This is a small synthetic Workspace, not a user workload.

| Attempt | Committed / refused | Cumulative elapsed ms | Last Save ms | Load ms | Files / logical bytes |
|---|---|---:|---:|---:|---|
| 256 | 256 / 0 | 15820.95 | 39.74 | 39.31 | 514 / 124950 |
| 257 | 256 / 1 | 15918.78 | 34.76 | 34.15 | 514 / 124950 |
| 512 | 256 / 256 | 28189.62 | 27.94 | 29.46 | 514 / 124950 |
| 1000 | 256 / 744 | 44810.61 | 29.81 | 31.02 | 514 / 124950 |

Restore took **48.20 ms and refused TooLarge**; no recovered commit is claimed at quota. Last good revision remains 255; identical retry returns Unchanged. Logical storage counters (including checkpoint Load/Inventory/readback and final stale/retry/Restore checks): **454,880 successful reads / 110,775,936 bytes / 706,048 wildcard enumeration entries**. Device I/O, NTFS allocation and physical durability are not measured. The separately labeled retained-history-per-attempt estimate is 108,787,780 bytes. Same-protocol baseline/current executable measurements run separately and upload their own raw JSON, rather than compare this xUnit observation to a differently instrumented run. All timings are cloud-only, not Windows 11 user hardware acceptance.

## Same-protocol local comparison and actual old-code matrix

The separate Release [baseline capacity JSON](evidence/e3-4-local/capacity-before.json) and [current capacity JSON](evidence/e3-4-local/capacity-after.json) use the same executable, fixture and 1000-attempt/checkpoint protocol. Baseline elapsed to attempt 1000 **20,721.29 ms**, current **29,223.87 ms** (approximately **+41.0%**). At attempt 256: 4454.41 → 6318.18 ms. Checkpoint Load at attempt 1000: 0.0499 → 35.2498 ms; that snapshot timing is not a percentile or calibrated hardware benchmark. Both keep 256 committed / 744 refused, 514 files and 124950 bytes. Restore refuses TooLarge in both (26.27 → 44.92 ms); no successful recovery latency is inferred from a refusal.

The regression is explicit: current checks both content-bound families for every operation, whereas old Save checked prepared only and valid-main Load bypassed history entirely. This improves refusal consistency and adds bounded I/O/canonicalization cost; it **does not improve scalability**, and the >10% regression is a review/merge concern. Do not market this as the sustained-save solution. One cloud trial is not a median/worst product benchmark or Windows 11 measurement. A future complete verified-generation protocol must address cost without skipping covered records.

[Instrumented xUnit measurement](evidence/e3-4-local/capacity-instrumented-xunit.json) is a separate run with Inventory and logical read counters; its elapsed value is not compared to the uninstrumented baseline run. Volume remains overlay with the observed 33.77 GB capacity, not NTFS or a physical near-full-disk test.

Executed matrices: [original E3-2](evidence/e3-4-local/6687bc39d060029e8f0a692419694ffa3c5a7a29.json) and [original E3-3](evidence/e3-4-local/ec43366ab3b86b54bac9e311b5f7d9ebb2fa9b6d.json), **12 cases each**, script exit 0. Complete future main refuses Load/Backup/Restore; corrupt or absent future main with absent fence permits Restore revision **43** below hidden 1000. Leftover v1 main with absent fence permits Save **43**, Restore **44**. With future/corrupt prepared fence, those Save/Restore paths refuse; **old Backup still appends** with leftover v1 main. All existing source backup/prepared/hidden bytes remain exact. Synthetic future main/cold state is a negative candidate experiment, not a shipped migration or proof of sufficient fence.
