# E3-4B-1 independent v2 engineering evidence

Status: transaction-core increment, acceptance incomplete; Draft only. Base main b3b278b0759d5c0af55e224b250c27c33a6bbb10 includes E3-4A. [ADR-0012](ADR/0012-independent-v2-storage.md) is Proposed. This implements actual Initialize/Save/Load/Backup/RestoreRevision, not a codec-only or architecture-only deliverable. v1 production source and original test bodies remain unchanged. E2 Drafts #31/#32/#33 have no dependency or write from this work.

## Reproduction and artifacts

Run `dotnet restore`, `dotnet build -c Release`, `dotnet format --verify-no-changes`, `dotnet test -c Release --no-build`. Windows CI uses its existing full solution commands and strict exit gates. Local Linux cross-compilation additionally needs `-p:EnableWindowsTargeting=true`; plain restore was attempted and refused NETSDK1100 for existing Windows spikes. Linux cannot certify NTFS. SDK installation first failed for the latest-version discovery endpoint and /opt permissions; explicit SDK 9.0.315 installed into the workspace succeeded. No secrets were read.

`V2StoreTests.Continuous1024RealSavesRetainVerifiedHistoryAndRestartRecovery` initializes a synthetic revision 0, then calls the real Save method 1024 times with distinct revisions. Every Save must succeed/Committed. Load and Backup each freshly validate the whole linked graph. Checkpoints 32/128/256/512/1000/1024 record success/refusal counts, selected revision, real file count/bytes/free space, Save/Load/Backup/Restore times, refused Save time, managed Save/Load allocation and complete-history outcome. Restore timing at checkpoints uses only copied generated files in separate owned roots, normal Restore validation/publication and no alteration of the original Save sequence. Cloning never substitutes for Save. Final recreation loads 1024, rejects stale/equal-conflicting input, accepts Unchanged identical retry and restores the actually published revision 256 as new revision 1025.

Set `E3_V2_EVIDENCE_DIR` to a trusted evidence directory to retain `v2-capacity.json`. Windows workflow sets it under RUNNER_TEMP and uploads only generated JSON as `v2-evidence`; complete TRX remains in `test-results`. No raw Workspace or personal paths enter artifacts. Tests run on owned generated temporary roots, not the user's default root. Timing is single-run checkpoint sampling, not repeated statistical acceptance or a v1/v2 performance-improvement claim.

## Actual development results and repairs

First Linux Release build failed two new-test nullable/options analyzer checks (zero warnings, two errors); fixed without weakening tests. First v2 run: 28 passed, two failed, zero skipped. Both failures exposed orphan immutable-page collision after pre-reservation crash. The fix freshly native-reads and compares a colliding page, reuses only exact bytes, keeps staging and still requires ordinary reservation publication. Focused fault rerun: 29/29 passed. The capacity case separately completed all 1024 real Saves on Linux, zero refusals, final 1024 loaded and RestoreRevision(256) produced 1025. This first run is preliminary Linux logic evidence; final post-fix full results and Windows results are recorded below when available.

## Failure and security coverage

Deterministic split writes, Flush request, exact readback, before/after reservation and main root publication; structured simulated disk-full and I/O failure. Post-main unreadable verification yields Unknown; fresh restart resolves actual committed state. Burned reservation rejects conflicting content and raises Restore floor. Gaps, foreign IDs, equal retry/conflict, revision overflow, checksummed but contradictory graph selectors/ordinals/GUIDs, future formats and missing/damaged dependencies refuse mutation.

Owned child process is actually terminated at four cut points (before/after A and B). A second new process loads the durable state; the parent tests Busy for Save and Restore while the child owns the lock. Windows-only additions exercise native hard-linked root/lock/history page, real junction, dangerous aliases, directory rename denial and release, state replacement sharing denial and actual write ACL denial with fixture ACL restoration. Symbolic leaf/root tests execute on each platform. Original legacy-executable isolation and all existing native tests continue in CI unchanged.

These tests do not cover every possible namespace replacement interleaving or real hardware failure. No physical power-cut or full-device experiment was performed.

## Open blocking gates and rollback

- Damaged/missing authoritative-root recovery from witnesses with complete reservation proof: **not implemented**. Preserve all files and refuse; never select an older root by filename or fall back to v1.
- Exhaustive initialization/Restore crash cuts, every native identity substitution interleaving, physical disk-full, real device/cache/filter durability and Windows 11 user-device acceptance: **unverified**.
- Repeated full performance protocol and bottleneck profiling: **incomplete**. Full graph scans are linear per operation, quadratic across sequential saves. At 1024 transient managed allocation is substantial; bounded live memory is not low cumulative allocation. Backup is verified Unchanged, not a newly created independent physical backup. Do not mix it with v1 committed Backup timing.
- Future user migration, damaged-root repair, default namespace acceptance, product deployment, UI and independent security acceptance: **not enabled or closed**.

Rollback: revert this independent code/test/CI addition while preserving all v1/v2 files. Keep v1 defaults unchanged. No data deletion, reverse migration, redirection or root-witness copying is a supported rollback step. No automatic merge or product-release claim. The full E3-4B-1 core success condition and independent engineering merge gate are **not declared reached** while recovery and evidence gates remain open.

## First Windows run and native alias repair

Run [38550676853](https://github.com/Niceskys/zhuomian/actions/runs/38550676853) at f4845cc: restore, full Release build (0 warnings/0 errors), full format, docs/performance tooling succeeded. Tests: 693 passed, one failed, zero skipped (451 Core, 37 Launch, 205/206 Persistence). The 1024-Save case passed on actual NTFS and emitted checkpoint evidence. One new native alias assertion failed because the internal v2 factory normalized the root before PhysicalStorage could inspect its raw trailing dot/space spelling. Changed it to pass the original root through the existing raw validation, matching the v1 factory. Assertions are unchanged. Later evidence uploads for legacy/performance/native summaries failed because their generating steps were correctly not run after the test failure; no empty artifact was synthesized. The failed run and successful capacity evidence are retained, not treated as an overall CI pass.

Post-fix additions include genuine Windows post-main sharing denial/Unknown, nonempty coordination-file initialization refusal, and a strict Windows collector requiring all 696 tests, 37 v2 cases, four terminated-child cuts and verified NTFS 1024/checkpoints. Final verification remains pending below.

## Local full-suite verification

Post-fault-repair full run: 696/696 passed, zero failures/skips (451 Core, 37 Launch, 208 Persistence; original 659 plus 37 v2 cases). Release build: zero warnings/errors. [Generated Linux raw checkpoint measurements](evidence/E3_4B_1/linux-capacity.json) include all successful/refused operation cells, bytes/free space and allocations; native conditional cases are not Linux NTFS evidence. Full solution format was invoked and exited zero but warned that references for the unchanged Windows spikes did not load on Linux; Windows full format is the authoritative gate. The raw-root factory repair is Windows-specific; its unchanged native assertions still require the post-fix Windows run. Signed artifact download into this executor was denied by the environment proxy (403); no bypass or false artifact-inspection claim was made. Generated measurement JSON is also printed by the strict Windows collector so connected GitHub logs provide an inspectable supported evidence path.

## Parser review repair

Final code review found BinaryReader.ReadBytes can return fewer bytes than the declared payload length. The segment parser now requires exact length before Decode, with an additional internally checksummed truncated-length negative fixture. New immutable dependencies are also freshly native-read at their final segment path before reservation publication. Existing test expectations remain unchanged. Current suite expectation is original 659 plus 38 v2 cases (697 total); prior 696 results remain attributed to their earlier tree.

## Independent v2 test assembly

The existing native evidence collector intentionally requires exactly 171 v1 Persistence cases. Rather than changing that baseline assertion, v2 cases now reside in a separate `Zhuomian.Persistence.V2.Tests` project, compiled with linked unmodified generated-fixture helpers. Every original test project file, test body and v1 collector is byte-identical to main. The solution still runs 451 Core + 37 Launch + 171 v1 Persistence + 38 v2 = 697. The v2 collector separately verifies its assembly and all 1024 checkpoint evidence. Earlier runs with combined test assemblies are retained as historical development results, not current final-assembly evidence.

The final v2 fixture also changes a synthetic Space name with each revision, so the 1024 Saves bind different user content as well as different revision integers. Historical Restore asserts the actual revision-256 name, and reserved-but-unpublished recovery asserts the revision-1 name. No original test body is changed. Earlier raw measurements used the prior constant-name fixture and are explicitly preliminary; the final Windows artifact/log collector identifies its exact source commit.

Final measurement review separates fixture construction from real Save timing/allocation, reports initialization/first Save/initial Load/Backup/Restore explicitly, and stops final Restore timing before restart Load. Checkpoint Restore clones exclude copy and factory construction from timing. This corrects benchmark attribution without changing the transaction engine or any original assertion. Earlier samples are retained as earlier-protocol development data; no before/after speedup is claimed.

The capacity case explicitly disposes its first engine, verifies use-after-close refusal, constructs a new instance and loads the final revision. Per-call native handles are already released on every outcome; Dispose waits for an active call and never modifies disk evidence. Native disk-full CreateNew errors are classified as DiskFull as well as IOException-based Flush/write failures, with the existing v2 simulated failure assertion exercising the native exception branch.
