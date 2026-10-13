# E3-3 storage lifecycle implementation and evidence

Baseline: latest main **6687bc39d060029e8f0a692419694ffa3c5a7a29**, squash merge #35. Independent feature/e3-3-storage-lifecycle, target main. #31–#33 are untouched independent Drafts; no dependency or merge. [ADR-0010](ADR/0010-storage-lifecycle.md) is Proposed; ADR-0009, disk v1, transaction protocol and E3-1 launch authority are unchanged.

## Actual support matrix

| Interface | Actual support | Mutation / default status |
|---|---|---|
| Inventory | Bounded classification, redacted validation/risk, observed count/bytes/revisions, recovery and quota diagnostics; fails closed | No data mutation; existing root only, coordination lock may be created |
| DryRun / RetentionPlan | Per-entry preservation reason; incomplete/unsafe/conflicting history refusal | All files retained; zero eligible deletions and zero reclaimed bytes |
| ValidatePlan | Locked fresh scan; private namespace/native identity/hash/content/revision/health binding; instance ownership | Enabled; read-only; no mutation lease |
| Apply | Explicit authorization parameter; structured refusal | Unconditionally disabled, including authorized calls; no filesystem operations |
| PlanBackupExport | Highest fully understood valid backup, exact source SHA256 and v1/schema 1 provenance | Enabled local plan only; retains original assets |
| ValidateExport | Entire original inventory freshness, integrity and ownership recheck | Enabled; read-only |
| Export | Explicit authorization parameter; no destination/config path accepted | External writes unconditionally disabled; no uploads/sync/commands |

**256-file limit is not relieved.** Prepared reservations retain complete equal-revision content commitments; old backups and exact evidence preserve historical recovery. File names/age/checksum do not prove ownership or safe retirement. No reviewed compacted protocol or export destination contract exists; a scalar high-water mark is diagnostic only. Inventory provides visibility/backpressure and safe review APIs, not long-term daily-use acceptance.

Inventory caps enumeration at 1026 observations plus one lookahead, and content reads at aggregate 64 MiB (stricter than E3-2 per-collection scanning) with unchanged 8 MiB file limits. Unknown/inaccessible totals remain incomplete. Per-family quota pressure is separately reported. Ordinary diagnostics do not expose paths, filenames, content hashes, Workspace IDs, private names, URLs or JSON. Export SHA256 is an explicit local source manifest and must never be telemetry.

## Invariants and automated coverage

[Lifecycle tests](../tests/Zhuomian.Persistence.Tests/StorageLifecycleTests.cs) compare exact bytes and namespace before/after inspection, dry-run, authorization refusal and disabled Apply/Export. They retain valid main, every valid recovery generation, all prepared history, temp and valid/corrupt/unknown/partial evidence. Missing main/LKG blocks retention review. Unknown ownership blocks plans. Equal-revision conflicting or foreign-Workspace histories refuse. Recovery advances beyond prepared reservations and maintenance cannot make an old client acceptable. Save failure and simulated disk-full tests preserve previous main/assets through maintenance refusal and permit an identical safe retry.

[NTFS tests](../tests/Zhuomian.Persistence.Tests/WindowsLifecycleTests.cs) execute real hardlink/junction/alias rejection, same-byte native file replacement invalidation, share/ACL read denial, held-leaf replacement denial and pinned-directory rename denial. A repository-owned child exclusively holds workspace.lock: Save/Restore/Inventory/validation return Busy; disabled Apply cannot mutate. Killing the lock owner releases its OS handle; restart validation and exact recovery assets remain intact. Mixed simultaneous Save/Restore/Inventory/Apply tests preserve validated main, recovery selection and revision floor. Existing E3-2 Windows tests additionally retain native replacement/recovery, ACL denial, reparse roots and external resource integrity assertions.

No cleanup mutation exists, so deletion-before/during/after fault injection is **not applicable**. No interrupted deletion or reversible deletion protocol is claimed. Rollback evidence consists of exact no-mutation comparisons and original E3-2 save/restore fault tests; process termination evidence concerns the lock fixture, not power loss. Destination write failure/name collision/partial-write tests are deferred because actual external export writes are disabled and no destination argument exists.

## Commands and evidence status

Local cloud: Linux x64, .NET SDK 9.0.318. The initial persistence build caught strict analyzer errors in new constant properties; these were fixed without weakening analyzers. An initial local format invocation with an unsupported MSBuild switch was rejected; the proper Linux invocation uses EnableWindowsTargeting=true in the command environment. These are retained development failures, not passing checks. Persistence xUnit initially passed **149/149**, zero failures/skips (103 original + 46 E3-3). Six new native tests take explicit unsupported-platform assertions on Linux; those passes do not certify NTFS.

Full local restore/Release/complete format/all xUnit and final Windows workflow results will be recorded below after execution. CI retains every original documentation/performance tooling/native-exit propagation gate. The evidence collector requires exact **451 Core + 37 Launching.Windows + 149 Persistence = 637**, verifies the 103 original persistence cases independently, and requires named lifecycle/native assertions. Uploaded TRX, platform and per-test enum-only outcomes contain generated fixtures and environment metadata, not configuration payloads or private paths.

## Residual gates

- Safe authenticated/verified ownership, dependency and uniqueness proof before any deletion.
- Reservation compaction with a separately reviewed transactional disk protocol and full crash/reorder/duplicate compatibility proof, if pursued.
- Retention policy preserving historical recovery and independent generations; no quota-only history sacrifice.
- Authorized user destination selection and native export staging/permissions/collision/partial-write/rollback tests before enabling writes.
- Windows 11 real-user/default root, device/cache/filter, install/update/uninstall and physical full disk/power cut experiments.
- Product UI/E2/E4/desktop integration, long-term use/soak and user acceptance; no physical durability or usable release claim.

Revert this PR without deleting any data. Disk v1 readers and E3-2 transactions need no migration or restoration. Do not merge this Draft or promote ADR automatically.

## Full local gate

2026-10-13: solution restore with EnableWindowsTargeting=true, Release build (zero warnings/errors), complete solution format verification with EnableWindowsTargeting=true environment, and full solution xUnit all exit **0**. Test counts: **451 + 37 + 149 = 637**, zero failed/skipped; local TRX retained in the execution workspace. Local Markdown link/tab check and git diff --check also exit 0. No pre-existing C# test body or Core/Launching/Spike/solution/workflow source is changed. PowerShell documentation/performance tooling gates run on the actual Windows PR workflow, not inferred from these Linux checks.

Commands (SDK installed locally for this session):

```text
dotnet restore Zhuomian.sln -p:EnableWindowsTargeting=true
dotnet build Zhuomian.sln --no-restore -c Release -p:EnableWindowsTargeting=true
EnableWindowsTargeting=true dotnet format Zhuomian.sln --no-restore --verify-no-changes
dotnet test Zhuomian.sln --no-build -c Release -p:EnableWindowsTargeting=true --logger trx --results-directory <owned-test-results>
git diff --check
```

## Retained initial Windows failure

[Run 38414301194](https://github.com/Niceskys/zhuomian/actions/runs/38414301194), head 206f0c8ad6d261682960152c9466af67d8d50f39, passed all documentation/performance tooling/exit-code/restore/Release/format gates, build zero warnings/errors, 451 Core and 37 Launching cases. Persistence was 148/149 with zero skips: the new directory-alias assertion failed because Windows Path.GetFullPath normalized the trailing dot before the native final-path comparison. The raw storage root now refuses trailing dot/space segments **before normalization**, with explicit constructor-refusal assertions; junction and native identity assertions remain intact. No original test was weakened or skipped. The strict collector did not execute after xUnit failure; the platform upload correctly failed for missing evidence, while TRX and launch metadata uploads succeeded. This run is failure evidence, not NTFS acceptance.

Final review additionally withholds any claimed remaining byte budget for unreadable families (Unknown pressure, zero proved remainder). A sharing-denied temp explicitly reports InUse/Busy; other unreadable files remain incomplete. These behaviors are checked by the real Windows share-denial fixture.

[Intermediate run 38414724102](https://github.com/Niceskys/zhuomian/actions/runs/38414724102), head cbc176dc57391c78720622798879136e767a3f1f, also retained the same 148/149 directory-alias failure: it was queued for the quota-diagnostic patch before the root-normalization fix was committed. Original suites and all pre-test gates passed. This is not a silent retry or claimed success.
