# E3-4A — history consistency protection and safe performance optimization

PR #37 acceptance is limited to independently reversible v1 history consistency protection and safe CPU/allocation optimization. E3-4B is outside this PR acceptance: the 256-file quota, 1000 successful saves, production v2, migration, archival transactions and cleanup remain unsolved. These block sustained-storage/product release claims, not completion of this scoped engineering review. No production or test code changes after fixed C are permitted in this final review.

Reviewed baseline: fa6338d662e5e59efe01a6ce01b399b3551a95ba. Same PR/branch; no main merge or #31–#33 changes. [ADR-0011](ADR/0011-safe-history-compaction.md) remains Proposed. [E3-4B protocol review](E3_4B_PROTOCOL_REVIEW.md) is a candidate only; no migration/cleanup is enabled.

## Cost analysis and implemented optimization

All operations hold the existing process gate, native root/ancestor pins and exclusive workspace.lock. PhysicalStorage performs unchanged wildcard enumeration/metadata quota checks and fresh native read opens (final path/kind/single-link, deny write/delete sharing), then bounded full file reads. No file read, handle validation, family traversal or budget is skipped.

| Operation | Existing repeated work at the reviewed SHA | Follow-up |
|---|---|---|
| Save | Main read in ReadHistory and again for predecessor; all backup/prepared decoded/canonicalized; Archive re-enumerates before adding files | Every read remains; exact duplicates reuse Decode/canonical digest inside this operation |
| Load | Primary main plus second main read in ReadHistory; both full history families | All reads/checks remain; CPU/allocation duplicates reused |
| Backup | Main decode then again in ReadHistory; both histories; archive quota enumeration | Same reads/publication; equivalent bytes reuse prior validation |
| Restore/recovered Load | Primary/backup selection, then main/backups again plus prepared; canonicalization in both passes | Both passes remain fresh; identical rereads reuse validated objects/digests |
| Inventory/plan validation | Native capture held handles, raw SHA256, Decode, canonical content hash for main/history | Same namespace/native manifest; raw SHA reused from exact-byte session Decode, canonical digest shared for duplicate bytes |

HistoryCommitments previously calls WorkspaceCodec.Encode for every observed snapshot. Encode performs snapshot validation, canonical DTO serialization, payload SHA256/envelope serialization and a complete Decode roundtrip. This is additional to the initial file Decode/checksum/JSON/tree/domain work. Backup/prepared pairs often contain exact duplicates; Restore adds a second pass. These duplicate CPU/allocations were the optimization target; filesystem I/O remains the floor. PhysicalStorage also allocated a new 64 KiB buffer for every read at the baseline; the follow-up uses ArrayPool with clearArray=true in finally on success/failure. Returned JSON is an independently owned MemoryStream copy, never the rented buffer. Count/length bounds and all reads remain unchanged; cleared return prevents private configuration residue in the pool.

HistoryReadSession is created inside each Save/Backup/Recover/Inventory invocation after locking; never stored on WorkspaceFileStore, never durable or shared across processes. It retains at most 8 MiB raw bytes and 513 distinct entries. Key: freshly computed exact-byte SHA256, followed by **SequenceEqual on full bytes**, so digest equality alone cannot authorize a hit. A retained immutable RecoveryResult reuses its canonical digest only after complete original Encode validation/roundtrip. Cache saturation merely forces full validation. No codec check is removed. Large uncached results are not retained by the digest map. Native reads happen before session lookup; failed/linked/unreadable sources cannot use an earlier result. Fingerprints/identity in plans still bind every capture separately.

Cross-operation/process/restart: new session each time, so fresh reads revalidate new bytes. Within one operation: every reread also performs native open/full read/hash/byte comparison; replacement/corruption does not match the earlier record and is fully decoded. Six new public-behavior cases cover warm-store and restarted-store corruption, identical length/time different content, foreign Workspace replacement, same-Restore corruption before reread, actual child-process history mutation, and Windows same-byte hardlink refusal. Existing same-byte native identity plan tests remain unchanged. Privileged/kernel/same-account hostile filesystem mutation is still not a new security isolation claim.

## Paired repeated measurement protocol

[Runner](../scripts/persistence/test-history-performance.ps1) builds immutable reviewed SHA fa6338d and current source against the **same** [probe](../scripts/persistence/LegacyProbe/PerformanceMatrix.cs), fixture, SDK/runtime and environment. Five paired rounds alternate before/after order. Each process executes a full warmup matrix, then twenty measured operations. Setup, snapshot creation, JIT warmup and fixture cleanup are outside intervals; actual operation includes root pin/lock/read/verification/flush/publication. Full v1 fixture has four kinds, sharing, removed records and asset references; resource references are data only.

History means total backup + prepared records: **0, 32, 128, 256**, split evenly. Add **512 total / 256 per family** for actual quota refusals. Main has revision perFamily+1; historical pairs cover revisions 1..perFamily, with exact byte duplicates; Save requests perFamily+2. Every mutation receives a fresh root, so repeated samples never accidentally change history depth. Zero-history Restore returns unchanged valid main; label it ReadOnlyOrUnchanged, not a successful restored commit. At per-family 256, Save/Backup/Restore refuse TooLarge; report these separately from successful commits. Load still validates readable history.

Raw round JSON contains fixture digest, SHA, OS/runtime/architecture, file counts/lengths, disk capacity/free space, outcome/revision/error, elapsed ms, managed thread allocations and logical read/enumeration deltas. Summary provides min/median/max per cell. The runner fails on mismatched fixture, acceptance, revision, before/after file/byte counts, logical read bytes/files or enumeration counts. There are no timing thresholds, silent retries, fake quota success or performance-related test skips. Logical lengths/reads are not device I/O or NTFS allocation, and single-run cell medians are not product percentile/hardware claims.

Existing 1000-attempt xUnit/tooling remains byte-unchanged in HistorySafetyTests: expected 256 committed / 744 refused, original assets exact, Restore TooLarge. Original 637 tests and prior 16 history cases remain unchanged; six follow-up cases give **659 = 451 + 37 + 171**. Collector separately enforces original counts, 16 existing history cases and six new safety cases. Full restore/Release/format/xUnit/Windows NTFS/doc/performance-tool/exit-code gates remain, with raw paired performance artifacts added. Windows Server CI does not close Windows 11 user/device gates.

## Results and review limits

Actual local/Windows run IDs, measured tables and retained raw samples will be recorded after execution. The earlier approximately 41% regression compared E3-3 against E3-4A; follow-up measurements compare reviewed E3-4A against its optimization. These are distinct baselines and must not be interchanged. CPU work may improve while native I/O remains unchanged; no guaranteed timing improvement is claimed before measurement.

No quota change, new format, safe history compaction, production migration, cleanup, Apply/Export permission or hardware durability is delivered. Revert the follow-up without deleting any data. Discarding all operation-local reuse must yield the same validated acceptance decisions. Root/index loss and legacy-barrier loss remain gates in the E3-4B design.

## Completed local implementation gates

2026-10-14, Debian GNU/Linux 13 x64, SDK 9.0.318: restore, complete Release solution build (zero warnings/errors), complete solution format verify and full xUnit passed **659/659 = 451 + 37 + 171**, zero failures/skips. Six focused new cases also passed; native unsupported-platform branches on Linux do not certify Windows. Additional complete probe build/format and PowerShell documentation validation pass. Original 637 test bodies and prior 16 history tests remain byte-unchanged against reviewed fa6338d.

The first performance-probe build correctly failed CA1861 for a repeatedly allocated property-name array; changed to static readonly without disabling analyzers. An initial diagnostic matrix overlapped local build/test activity and pre-commit code, so it is excluded from authoritative results. The final runner refuses dirty measured source/probe trees and binds fixture/seed digests and compiled Persistence assembly hash. Committed-source quiet-host repeated measurements and Windows final-head evidence follow in the PR/results below. Existing 1000 attempts remain 256 committed / 744 refusals; no quota relief.

## Committed-source repeated local results

Reviewed SHA **fa6338d662e5e59efe01a6ce01b399b3551a95ba** vs implementation SHA **4ccbc0e82e7d4bdc236d4ab9e1939f5786cbe403**; full fixture, same Debian GNU/Linux 13 x64 host / SDK 9.0.318, five alternating paired rounds, with one full warmup matrix per process. No concurrent local build/test was run during these authoritative intervals. [Raw summary](evidence/e3-4-performance-local/summary.json) and all ten before/after round files are retained. 100 measured samples per version; setup/cleanup excluded. All exact seed digests, outcomes/revisions and logical read/file/byte/enumeration counts match. These timings are cloud-environment observations, not Windows 11 hardware results.

Each timing cell is **min / median / max ms**, including unfavorable results. History is total records, evenly split between backup/prepared. At 512 (256 each), Save/Backup/Restore timings measure **TooLarge refusal**, not successful commits. Zero-history Restore is unchanged main, not a recovered commit.

| History | Operation | Outcome | Before ms | After ms | Median change | Managed allocation median KiB before → after |
|---:|---|---|---|---|---:|---|
| 0 | Load | ReadOnlyOrUnchanged | 0.419 / 0.542 / 0.844 | 0.554 / 0.816 / 1.073 | +50.4% | 340.6 → 153.9 |
| 0 | Save | Committed | 1.400 / 1.779 / 2.770 | 1.320 / 1.972 / 3.193 | +10.8% | 1006.9 → 627.6 |
| 0 | Backup | Committed | 0.629 / 0.717 / 1.261 | 0.600 / 1.041 / 3.523 | +45.1% | 536.6 → 286.1 |
| 0 | Restore | ReadOnlyOrUnchanged | 0.254 / 0.340 / 0.595 | 0.376 / 0.894 / 1.167 | +162.9% | 209.6 → 146.9 |
| 32 | Load | ReadOnlyOrUnchanged | 9.830 / 12.930 / 18.790 | 12.230 / 13.790 / 15.050 | +6.7% | 6949.2 → 2559.2 |
| 32 | Save | Committed | 11.229 / 14.737 / 32.472 | 9.711 / 14.735 / 16.729 | -0.0% | 7638.1 → 3064.2 |
| 32 | Backup | Committed | 11.144 / 13.044 / 21.963 | 7.625 / 12.567 / 15.225 | -3.7% | 7155.8 → 2707.2 |
| 32 | Restore | Committed | 15.934 / 17.484 / 31.123 | 8.305 / 14.669 / 30.526 | -16.1% | 10804.8 → 3031.2 |
| 128 | Load | ReadOnlyOrUnchanged | 38.307 / 42.795 / 49.289 | 36.733 / 54.548 / 56.578 | +27.5% | 26750.0 → 9780.5 |
| 128 | Save | Committed | 37.580 / 40.173 / 45.647 | 30.915 / 43.953 / 59.033 | +9.4% | 27540.0 → 10379.8 |
| 128 | Backup | Committed | 36.959 / 44.538 / 49.553 | 22.512 / 29.429 / 52.566 | -33.9% | 27008.5 → 9973.4 |
| 128 | Restore | Committed | 58.554 / 67.466 / 257.278 | 30.166 / 40.134 / 54.714 | -40.5% | 40565.5 → 10639.7 |
| 256 | Load | ReadOnlyOrUnchanged | 68.352 / 76.866 / 115.193 | 51.170 / 61.010 / 99.005 | -20.6% | 53162.2 → 19410.8 |
| 256 | Save | Committed | 79.981 / 81.892 / 89.800 | 58.126 / 62.781 / 75.856 | -23.3% | 54077.3 → 20135.2 |
| 256 | Backup | Committed | 63.835 / 78.019 / 96.544 | 44.571 / 56.899 / 65.546 | -27.1% | 53482.4 → 19665.9 |
| 256 | Restore | Committed | 89.112 / 107.135 / 152.774 | 56.651 / 65.449 / 78.698 | -38.9% | 80246.4 → 20787.0 |
| 512 | Load | ReadOnlyOrUnchanged | 114.292 / 141.452 / 299.438 | 100.501 / 117.330 / 214.077 | -17.1% | 105956.8 → 38584.9 |
| 512 | Save | Refused | 112.848 / 124.650 / 138.598 | 66.346 / 102.064 / 119.436 | -18.1% | 106280.5 → 38836.4 |
| 512 | Backup | Refused | 110.627 / 121.452 / 136.950 | 66.706 / 95.953 / 111.723 | -21.0% | 106203.7 → 38762.1 |
| 512 | Restore | Refused | 172.631 / 180.073 / 206.935 | 64.547 / 101.050 / 107.769 | -43.9% | 159176.9 → 40658.8 |

At 256 total records, median Load/Save/Backup/Restore improve about 21%/23%/27%/39%. At the 512 total-record refusal boundary they improve about 17%/18%/21%/44%. Managed allocation falls materially (roughly 63–75% in these nonzero history cells), while actual logical read work stays identical. **Small-history timings and 128-record Load regress in this run**; the corresponding ranges/raw repetitions are not omitted. There is no universal speedup or SLA claim. Fixed setup/cache-bookkeeping costs and cloud scheduling/GC/I/O variation require review; small absolute sub-ms cells should not be presented only as favorable percentages. More measurements must retain their own trials, not silently replace this one. Windows repeats are required and recorded through final PR CI.

Production migration stays disabled. The new namespace-barrier executable experiment verifies old refusal while that directory exists, with synthetic new root complete/corrupt/missing; it does not prove barrier loss, migration replay or v2 correctness. Both missing-barrier behavior and incomplete root/reservation coverage remain explicit E3-4B merge gates.

## Fixed three-version final review protocol

[Three-version runner](../scripts/persistence/test-history-three-version-performance.ps1) exports the exact immutable A `ec43366ab3b86b54bac9e311b5f7d9ebb2fa9b6d`, B `fa6338d662e5e59efe01a6ce01b399b3551a95ba`, C `b25f9cefcf15f77be82b365563e4814b528d2f40` source trees and builds the identical probe against each. Six rounds use **ABC, CBA, BCA, ACB, CAB, BAC**, balancing each version twice in each position. Same complete fixture, deterministic revision seeds, warmup and twenty cells per process as the paired protocol above: **120 measured samples per version / 360 total**. Original raw rounds and unfavorable historical measurements remain.

The final-head production and test trees must be byte-identical to C. Source commit resolution, compiled assembly digests, fixture SHA256, per-cell seed digests, environment/runtime, exact counts/revisions/errors/outcomes and B/C read metrics are verified, with strict failure exit codes. A has no read instrumentation: null means unavailable, not zero. A’s valid-main Load and Backup lack the later full-history checks; accepting the same valid fixture is not evidence of equal security or equal I/O. B/C must retain identical logical I/O. No instrumentation is retrofitted into A. All raw samples are saved before validation; failures are not silently retried. Min/median/max, allocation medians and paired B→C changes are retained. Six-value median averages the middle two values.

Compare A→B for safety cost, B→C for recovered CPU/allocation cost and A→C for net code change only within this identical workload. At 512 total / 256 per family, mutation results are quota refusal, while Load is read-only; at zero history Restore is unchanged. No 1000-success claim follows from timing quota refusal. Seed snapshots are generated through each unchanged v1 codec from the same complete domain fixture; exact encoded seed hashes must match. Raw unknown/partial/damaged v1 preservation is tested separately by the existing unchanged negative/compatibility fixtures, not inferred from these timing samples.

Windows NTFS results and a stability assessment of the previously observed +56.2% 32-history Save regression are recorded below. This is a cloud CI experiment, not Windows 11 desktop/hardware acceptance or a physical device latency bound.

### Local six-round rehearsal retained

The same pinned A/B/C sources completed the six-round protocol on Debian GNU/Linux 13 x64 / SDK 9.0.318 before the runner commit. [All 18 raw rounds and summary](evidence/e3-4-three-version-local/summary.json) are retained separately; no sample is removed. This checks the framework and is not Windows NTFS evidence. At 32-history Save medians A/B/C are **7.402 / 11.932 / 14.903 ms** (A→B +61.2%, B→C +24.9%, A→C +101.4%). At 256-history Save they are **47.624 / 78.903 / 92.700 ms** (+65.7%, +17.5%, +94.7%). These unfavorable Linux results are also retained; they cannot be substituted for the requested Windows comparison or used to declare the earlier Windows +56.2% reproducible.

## Windows NTFS six-round three-version results

[Run 38508926649](https://github.com/Niceskys/zhuomian/actions/runs/38508926649), job 115672705239, branch head 7fd3ac808dc4c2619e5323850b7df371452a64d2; native suite tested PR merge 0bb5d52c57d5beb11f54f10e25165c5f74b3d440. **Windows Server 2025 / windows-2025-vs2026**, build 10.0.26100.0, X64, SDK 9.0.319/runtime 9.0.21, NTFS. All restore/Release (zero warnings/errors)/complete format/xUnit/native/doc/tool/exit-code/legacy/capacity and immutable-protocol gates passed. **659/659 = 451 Core + 37 Launching + 171 Persistence**, zero failures/skips. Existing 637, prior 16 and six optimization cases remain unchanged. This review changes no production/test bytes relative to C.

[All 18 Windows raw rounds and summary](evidence/e3-4-three-version-windows/summary.json) are retained in the repository. They were extracted as exact JSON records from completed CI logs and reformatted; ZIPs were not unpacked. [Provenance and artifact hashes](evidence/e3-4-three-version-windows/provenance.json) bind run/head/job/platform. Uploaded history-performance contains the original raw files; TRX, native platform/fault, legacy/capacity and launch metadata are separate artifacts. Six balanced orders, 120 samples/version, matching seeds/outcomes/revisions and unchanged B/C logical I/O. Physical device I/O is not measured.

Each timing cell is **min / median / max ms**; changes compare cell medians only within this one same-environment/same-fixture experiment. No adverse sample removed. History is total records; at 512, only Load is read-only and mutations are **TooLarge refusals**. At zero, Restore is unchanged main.

| History | Operation / outcome | A ms | B ms | C ms | A→B | B→C | A→C |
|---:|---|---|---|---|---:|---:|---:|
| 0 | Load / ReadOnlyOrUnchanged | 0.996 / 1.035 / 1.117 | 1.513 / 1.603 / 2.376 | 1.391 / 1.622 / 2.581 | +54.9% | +1.1% | +56.6% |
| 0 | Save / Committed | 35.208 / 65.607 / 110.722 | 37.542 / 84.180 / 114.827 | 63.635 / 83.966 / 139.067 | +28.3% | -0.3% | +28.0% |
| 0 | Backup / Committed | 17.618 / 29.471 / 45.384 | 12.304 / 32.927 / 41.750 | 29.712 / 38.000 / 47.178 | +11.7% | +15.4% | +28.9% |
| 0 | Restore / ReadOnlyOrUnchanged | 1.015 / 1.073 / 1.182 | 1.178 / 1.241 / 1.882 | 1.089 / 1.172 / 1.232 | +15.6% | -5.6% | +9.2% |
| 32 | Load / ReadOnlyOrUnchanged | 0.925 / 0.957 / 1.055 | 12.305 / 12.838 / 17.896 | 8.634 / 8.810 / 10.155 | +1241.6% | -31.4% | +820.7% |
| 32 | Save / Committed | 43.833 / 91.496 / 102.849 | 54.105 / 88.225 / 326.434 | 76.695 / 79.628 / 97.487 | -3.6% | -9.7% | -13.0% |
| 32 | Backup / Committed | 17.192 / 24.455 / 33.110 | 28.080 / 36.000 / 49.837 | 30.162 / 37.078 / 47.064 | +47.2% | +3.0% | +51.6% |
| 32 | Restore / Committed | 36.579 / 56.401 / 81.124 | 41.343 / 68.882 / 91.427 | 46.254 / 75.712 / 131.892 | +22.1% | +9.9% | +34.2% |
| 128 | Load / ReadOnlyOrUnchanged | 0.944 / 0.998 / 1.113 | 43.178 / 44.500 / 61.147 | 30.291 / 30.530 / 34.969 | +4360.9% | -31.4% | +2960.5% |
| 128 | Save / Committed | 56.468 / 80.342 / 105.019 | 96.743 / 122.945 / 134.135 | 105.165 / 121.227 / 311.410 | +53.0% | -1.4% | +50.9% |
| 128 | Backup / Committed | 18.959 / 26.622 / 34.718 | 59.316 / 70.183 / 97.812 | 52.972 / 61.924 / 112.913 | +163.6% | -11.8% | +132.6% |
| 128 | Restore / Committed | 68.588 / 89.976 / 98.209 | 93.038 / 121.759 / 138.784 | 88.471 / 99.232 / 145.596 | +35.3% | -18.5% | +10.3% |
| 256 | Load / ReadOnlyOrUnchanged | 0.953 / 1.047 / 1.082 | 84.250 / 86.930 / 111.655 | 59.357 / 61.475 / 72.603 | +8200.8% | -29.3% | +5770.1% |
| 256 | Save / Committed | 96.573 / 112.018 / 165.433 | 167.005 / 184.266 / 295.230 | 130.559 / 162.606 / 205.793 | +64.5% | -11.8% | +45.2% |
| 256 | Backup / Committed | 21.030 / 32.747 / 36.148 | 109.065 / 118.343 / 165.829 | 86.253 / 114.900 / 431.474 | +261.4% | -2.9% | +250.9% |
| 256 | Restore / Committed | 107.014 / 126.477 / 138.919 | 183.109 / 190.585 / 209.685 | 127.305 / 143.604 / 849.770 | +50.7% | -24.7% | +13.5% |
| 512 | Load / ReadOnlyOrUnchanged | 1.059 / 1.106 / 1.762 | 174.691 / 205.112 / 273.026 | 119.809 / 123.875 / 134.272 | +18446.2% | -39.6% | +11100.7% |
| 512 | Save / Refused | 64.977 / 66.387 / 175.212 | 176.359 / 187.195 / 234.733 | 129.859 / 139.043 / 416.042 | +182.0% | -25.7% | +109.4% |
| 512 | Backup / Refused | 9.954 / 10.118 / 13.429 | 176.064 / 188.454 / 315.465 | 129.433 / 141.944 / 260.886 | +1762.5% | -24.7% | +1302.9% |
| 512 | Restore / Refused | 146.201 / 153.299 / 178.021 | 268.745 / 276.327 / 301.757 | 171.877 / 246.329 / 391.472 | +80.3% | -10.9% | +60.7% |

### Stability, costs and merge recommendation

The old Windows 32-history Save **+56.2%** observation (run 38501844109; B 84.5965 → C 132.1649 ms; C maximum 522.6159 ms) remains recorded in PR history and its artifacts. Earlier run 38501366071 had -45.4% at that cell. In this six-round comparison B→C is **-9.7%** (88.2255 → 79.62835 ms), with **3 slower / 3 faster** pairs. Individual paired changes are **-2.24%, +11.33%, -10.28%, +0.22%, -76.51%, +46.95%**. B ranges 54.105–326.434 ms, C 76.695–97.487 ms. **The specific +56.2% regression is not stably reproduced**, but speedup is not guaranteed. Ranges are observed sample ranges, not confidence intervals; cloud-host variation remains. Linux’s +24.9% at 32-history Save is retained separately and cannot be pooled with NTFS timings.

There are still adverse cells: zero-history Backup B→C **+15.4%** (5/6 pairs slower; +5.073 ms at the median), and 32-history Restore **+9.9%** (5/6 slower; +6.831 ms). Exact-byte cache/hash bookkeeping has fixed costs; measured wall time also includes native opens, lock, write/flush/publication and OS/GC scheduling. This experiment does not isolate those phases, so it cannot prove that flush or cache bookkeeping alone caused a particular tail. Preserve these costs for review rather than claiming universal speedup.

At 256 total records, B→C Load/Save/Backup/Restore median changes are **-29.3% / -11.8% / -2.9% / -24.7%** and allocation reductions **63.0% / 62.5% / 62.8% / 73.6%**. The exact logical read work remains **258/261/259/388 files** and **642462/649935/644953/966184 bytes** respectively. This supports recovered duplicate CPU/allocation cost, not skipped I/O.

Compared with main A, final C still costs **+60.43 ms Load, +50.59 ms Save, +82.15 ms Backup, +17.13 ms Restore** at 256 records (A→C **+5770.1% / +45.2% / +250.9% / +13.5%**). A returned a valid main without scanning history and Backup checked main only; B/C verify every backup/prepared identity/revision/content and canonicalize understood coverage, with additional native file handles and reads. A→B is the safety coverage cost; B→C reduces duplicate validation allocations while retaining it. A has no read counters, so no A read-volume number is fabricated. Native operation-local reuse cannot remove the remaining full-history I/O floor.

These differences are material engineering costs, not proof of unacceptable or imperceptible desktop behavior. The sampled small configuration has synchronous operations taking tens to hundreds of ms; UI/background scheduling and daily device latency are untested. Sustained daily use is independently blocked by the unchanged 256 quota. Cloud CI cannot certify Windows 11 user hardware, physical power failure, long device/filter/cache operation or UI integration.

**Recommendation: E3-4A can be reviewed for an independent engineering merge**, conditional on human review accepting the explicit extra validation cost and scoped safety contract. Its source/test freeze, reversible v1 behavior and complete native gates justify separating it from E3-4B. It is not a product/daily-use release approval, ADR acceptance or permission to merge automatically. Keep PR Draft. No new validated integrity acceptance regression was found by the retained negative tests; this is not exhaustive security certification. Legacy namespace/barrier loss, future v2/migration/archival crash proofs and quota relief remain E3-4B blockers outside this PR acceptance.

Local final-review restore initially failed NETSDK1100 on Linux because Windows-targeting was not enabled; corrected only the validation environment (`EnableWindowsTargeting=true`) and reran complete restore/Release/format/xUnit successfully, 659/659, zero warnings/errors/failures/skips. Linux native unsupported-platform branches are not Windows evidence. Final-head CI evidence is linked in PR #37 after the evidence-only commit.

