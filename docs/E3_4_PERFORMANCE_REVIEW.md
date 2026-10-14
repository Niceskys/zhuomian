# PR #37 performance follow-up

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
