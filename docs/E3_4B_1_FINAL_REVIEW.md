# E3-4B-1 final engineering review

Starting review SHA: `a80541885608bc27652af61741ba729ad8571aa0`.
Previously validated production tree: `984204fa3a6df41d28e23639320e453a0a908fd0`.
Final reviewed/Windows-validated production and test SHA: **`2695004613d9d7e352b75f0cb1a8d2708c64bc31`**. The final evidence-only publication commit is identified by the PR head/delivery comment; its source/tests/scripts/workflow must remain byte-identical to this validated tree.
PR [#38](https://github.com/Niceskys/zhuomian/pull/38) remains Draft; no merge is authorized.
main remains `b3b278b0759d5c0af55e224b250c27c33a6bbb10`.
[ADR-0012](ADR/0012-independent-v2-storage.md) remains Proposed.

This round reviews the existing core and extends the existing performance fixture/strict evidence collector. A newly reproduced witness-budget defect is fixed in **WorkspaceV2Store.cs only**; no disk format/field semantics change. All 942 tests, original assertions, 135 latency samples, 30 separate profiles, native tests and termination matrices remain; three focused budget tests are added (945 total). No new transaction fault cuts are added. Additional measurements do not replace any of the actual 1024 sequential Saves. Completed Windows results follow; no pending measurement is a pass.

## Evidence audit and the two-second Save

All 135 latency rows and 30 profiles in [the adverse round-3 run](evidence/E3_4B_1/round3-windows.json) were reviewed. Original 1024 format-3 Save samples were **1973.0773, 661.8649, 1883.1911 ms** (median 1883.1911); their process CPU times were **250, 250, 265.625 ms**. The long elapsed interval was not accompanied by proportionally more process CPU. This supports a wait/descheduling component, not a specific device or syscall diagnosis. Process CPU includes all test-host threads, has coarse granularity and is not a wall-time decomposition.

The completed final-document [CI 38618614491](https://github.com/Niceskys/zhuomian/actions/runs/38618614491) independently repeats **the same production, tests and measurement protocol**, with **942/942**, no failures/skips, full Release/format/native/legacy checks successful. Its [complete 135 rows and 30 profiles](evidence/E3_4B_1/round3-final-ci-windows.json) are retained, not substituted for the adverse run. At 1024, format-3 Save was **366.2875, 377.7378, 351.6390 ms**, median **366.2875**. Baseline format-2 Save median **374.3423**, current format-2 **356.6344 ms**. Thus the unchanged protocol can run substantially below two seconds on hosted NTFS; neither a fixed two-second format-3 cost nor a stable speedup is established. Different runs have different hosts; these are replication observations, not a controlled code-optimization comparison.

Third-round methodology findings:

- Copies and Decode are outside the operation timer. No copy/fixture build is directly charged to Save. However, immediately preceding thousands of File.Copy creates/metadatas may affect subsequent Flush/replace waits; exclusion from the timer does not isolate filesystem state.
- Load runs on original/control roots; Save/Restore use fresh copies. The sequential 272.8549ms Save uses the growing original directory. These workloads have different metadata/cache/writeback conditions and cannot be interchanged.
- The baseline assembly/load context is reused across clones; there is no per-sample cold-assembly penalty. All variants have already executed many Saves by 1024. Load warmup precedes each checkpoint; small-checkpoint first Restore/JIT effects cannot be generalized to 1024.
- ABC/CBA/ABC has only three samples per cell and unequal first-position counts. It is diagnostic, not stable tail evidence. The serialized benchmark collection and serial solution project execution remove intentional test-suite concurrency, not hosted-runner/filter/volume contention.
- Original managed allocation capture includes Process bookkeeping; elapsed excludes it. New measurements move allocation capture after that setup. Original reflection/result assertions remain inside operation time consistently for all variants.
- Critically, the 30 stage profiles are **different operations** from the 135 latency rows. A profile with 37ms Flush does not explain a separate 1973ms Save. Other profiles expose substantial waits (format-2 Save Flush 916ms, atomic root publication 413ms; format-3 Restore enumeration 273ms and Flush 700ms), but cannot identify the original bad sample's cause.

No production integrity check was removed to improve a timing. Original adverse results remain valid observations. Exact causal assignment of their uninstrumented wait to Flush, replace, native open, GC, filesystem filter, scheduling or host resources is unavailable from those old rows.

## Repeat measurement protocol

`V3FinalReviewMeasurements` extends the existing real-history fixture: **seven paired groups at 1024**, one diagnostic control group at each of 32/128/256/512, **231 additional real operations**. Each group rotates implementation, operation and condition order. Each Load/Save/Restore has a fresh owned clone. All copies, construction, Decode, optional warmup and post-operation independent full Load/disk accounting are outside the timer. Sources were built by the existing actual sequential Save loop, not fabricated pages. Every Save must return Committed, every resulting revision is independently loaded and checked.

Three unchanged-binary variants share `uninstrumented-fresh`: immutable `514a5594` format 2, current format 2, current format 3. Current variants additionally share `profiled-fresh` and `profiled-warm`; the latter first performs a complete verified Load on the clone with a separate uninstrumented instance. Counters therefore contain only the timed operation. This is a controlled warmup condition, **not** a cold-cache reset, device barrier or proof the host is idle. Profiling conditions are reported separately; they cannot silently stand in for the uninstrumented comparison. Fine baseline metrics are unavailable and represented by null, never fabricated zero. Existing production profiler is reused unchanged.

Each row records wall time, process CPU, current-thread allocation, per-generation GC count deltas, process GC pause delta, preparation/warmup duration, same-operation profile where available, actual file count/logical bytes/per-file NTFS AllocationSize/free space and expected revision. GC counters are process-wide; they do not establish that all pauses were caused by this operation. Seven samples support descriptive median/min/max and nearest-rank P90 (rank 7, equal to max), **not a stable tail guarantee**. Smaller controls have one sample and no percentile claims. This persistence diagnostic intentionally differs from product 60s/300s calibration; no UI frames or product thresholds are certified.

Timing inclusion: page/witness reads include validated native opens and data reads; witness total includes enumeration/read/parse; root publication includes root serialization, Write/Flush/readback/atomic publication/post-read; all-Write Flush also contains the dependency flush, outside root publication. Atomic publication is nested in root publication. Read SHA excludes Workspace envelope hashing inside Decode and write serialization hashing. These fields **must not be added as disjoint durations**. Native open time includes identity/final-path checks and kernel waits, not isolated disk metadata CPU. Counts describe successful logical read opens, not every root/lock/write handle or physical device I/O. Unmeasured pin/lock/create/close/serialization and scheduling gaps remain.

## Completed same-operation NTFS replication before the budget fix

Run [38656929768](https://github.com/Niceskys/zhuomian/actions/runs/38656929768), source `4b7ce1ef720987fb3abb29967c62d970082cf461`, **success / 942 of 942**, includes full Release/format/native/legacy/ABC gates, actual 1024 format-3 Saves and all 231 additional samples. Its production tree equals the reviewed 984204f/a805 tree; this run does **not** validate the subsequent budget fix. [Complete raw capacity, original measurements and 231 same-operation samples](evidence/E3_4B_1/round4-before-windows.json) remain available for before/after comparisons using the same measurement code. Source/clock/OS/runtime/CPU-count/NTFS provenance is embedded; CPU model, device/filter trace and full volume allocation are unavailable.

At 1024, seven uninstrumented fresh-clone samples per cell, median **[min–max] ms**:

| Operation | Immutable 514a format 2 | Current format 2 | Current format 3 |
|---|---:|---:|---:|
| Load | 175.43 [159.38–224.57] | 167.54 [155.91–187.34] | 169.95 [157.50–395.61] |
| Save | 261.83 [220.05–296.60] | 328.97 [200.61–621.24] | 266.03 [213.07–1075.05] |
| Restore | 235.69 [203.38–1439.26] | 230.58 [183.40–592.42] | 320.34 [194.61–913.95] |

Current format-2 Save median is still **25.6% worse** than immutable baseline on this host; Save CPU medians are both 171.875ms. This is not an established CPU optimization, nor a basis to discard the observed wall-time regression. Format-3 Save/Restore are different-protocol workloads; their mixed medians and unfavorable samples establish no universal improvement. Seven-row nearest-rank descriptive P90 equals each listed max (rank 7); it is not stable tail latency evidence.

Same-operation attribution now localizes adverse waits: format-3 profiled-warm Save round 1 takes **1587.76ms**, including **1428.92ms in Flush(true)**, CPU **156.25ms**, GC pause **1.761ms**, atomic root calls **1.189ms**, page read **40.34ms**, witness traversal **76.73ms**, Decode **33.94ms**. Copy **1391.81ms** and warm Load **165.78ms** occur before the timer. Root publication **1066.36ms** overlaps its root Flush calls and must not be added to all-Write Flush. At least this reproducible large Save spends about 90% of wall time inside the synchronous Flush regions; it is not doubled historical decode/SHA work or two seconds of File.Replace. The timing includes buffer writes and possible thread descheduling; it does **not** identify the physical disk/filter/scheduler cause of waiting.

A format-2 profiled-warm Save round 4 instead takes **920.24ms**, with validated read-open **803.04ms**, Flush **21.42ms**, atomic root **0.969ms**, GC pause **2.379ms**. Thus one universal Flush-only explanation would also be false. Warm Load does not guarantee lower write latency: format-3 profiled-fresh Save median **250.04ms**, profiled-warm **422.32ms**, maximum **1587.76ms**; profiled-warm Restore reaches **1963.46ms**. Profiling versus uninstrumented medians cannot isolate overhead in this variable filesystem workload. Preserve all conditions and adverse rows.

The exact 1973.08ms original uninstrumented sample remains untraceable to a particular syscall. Repeated unchanged-code runs plus localized same-sample waits narrow the explanation to variable off-CPU/observed-call-region waits, not a demonstrated fixed two-second format-3 requirement. They do not prove that copying causes those waits, that CI noise is harmless, or that real-user latency is acceptable. Repeated-host/device/ETW attribution and sustainable metadata organization remain E3-4B-2 inputs.

## Transaction correctness review

The acceptance/publication proof remains the format-3 proof in [round 3](E3_4B_1_ROUND3.md), checked against actual `InitializeNew`, `Commit`, `PublishRoot`, `Verify`, `VerifyWitnesses`, `RecoverAuthority`, Load and both Restore projections.

| Durable state | Meaning and permitted behavior |
|---|---|
| No data, empty owned lock | Only explicit InitializeNew creates a Workspace. Unknown/stage/orphan data cannot initialize over it. |
| Dependency/staging only before A | New content is not accepted. Existing intact A/B can still serve old main; partial initial store refuses. |
| A accepted, B absent/old/damaged | authority-v3.bin binds exact Workspace/count/head/prior selection; the complete chain binds every accepted revision/content. Normal Load/Save/Backup/Restore refuse with no stale Workspace. Explicit repair alone may publish latest accepted content. |
| Matching A/B | Full chain and every extant Witness must verify before Normal Load, mutation, Unchanged or Backup. |
| Submission outcome uncertain | Readback/full proof/exact destination may resolve actual B as Committed; unreadable or inconsistent outcome remains Unknown. An exception is not evidence of non-commitment. |
| Authority/chain/conflicting evidence untrusted | Fail Closed; no candidate/max/time/filename selection and no empty initialization. |

**A** is one atomic namespace publication of `authority-v3.bin`; **B** independently publishes `state-v2.bin`. New dependencies are flushed/read back before A; A precedes B. No claim of multi-file atomicity. `NotCommitted` describes **main**, not absence of a burned reservation; callers must not infer revision reuse from it. Within matching valid A/B, lower revisions are stale, equal different canonical content conflicts, exact equal returns Unchanged. Verification occurs before that equality fast path. Higher revisions require prior B; the ordinal prefix rule (prior-main=ordinal−1) proves every earlier accepted generation was selected. Gaps in revision numbers remain legal. Restore creates revision latest trusted reservation+1; maximum long refuses without overflow. The detailed `RestoreRevisionTransaction` exposes Unknown; the legacy RecoveryResult projection remains insufficient for a production caller needing that distinction.

`RecoverAuthority` is always an **explicit new B transaction**, including when old A-only content has never been selected before. It does not infer or report that old B succeeded. Load and ordinary writes never call it. A/full dependencies/all extant root witnesses are proved first; matching main returns Unchanged, otherwise a new B is flushed/read back/published. A is not changed. Before-B termination retains the same accepted floor; after-B termination has matching main; repetition verifies then returns Unchanged. Corrupt old main is retained as `damage-*`; supported valid old main is retained as `root-*`. Existing real child tests, independent expected floors, exact retained bytes, real post-B sharing/Unknown and wrong-byte-publication tests exercise these paths. No new fault cut is needed merely to repeat their names.

| Evidence after failure | Recovery boundary |
|---|---|
| Only main absent | Explicit new B allowed with intact A/full chain/consistent Witnesses. |
| Main exactly 100 bytes, readable version 3, checksum bad | Same, preserving damaged bytes. Truncation/extension/unknown header is not disposable corruption. |
| A accepted/B not completed | Same; accepted snapshot becomes main **now**. User-facing confirmation/Unknown/pending-data semantics remain a default-enablement decision. |
| Valid immediately previous main | Normal calls refuse; explicit new B allowed if proven member/selector. Older-than-prior or conflicting valid main refuses. |
| A absent/corrupt, missing/corrupt page, conflicting/foreign Witness/main | No trusted Workspace returned; mutation and repair refuse. No older read-only fallback is exposed. |
| Old valid A substituted with its replacement witness surviving | Same-generation/newer witness refuses; floor cannot silently decrease. |
| Old valid A and all contrary evidence simultaneously lost/replaced | Cannot distinguish a self-consistent older graph from a genuinely old state. Outside single-main-loss proof, not authenticated anti-rollback. |
| Format 2/3 mixed, future or unsupported data | Refuse without upgrade, deletion or initialization. Actual immutable old client isolation is retained. |

There is **no generally available read-only older Workspace** when authority is unproved: API returns null/Unrecoverable/readOnly rather than pretending an old selection is authoritative. Damaged bytes remain available for future controlled forensic tooling, not current trusted Load. NTFS native identity/single-link/no-reparse/fixed-root/pinned-directory checks and the root-local cross-process lock remain unchanged. SHA binds accidental-corruption evidence, not a hostile same-account replacement of the whole graph. Process termination is not physical power testing; Flush(true), controller/media/filter/directory durability and every partial File.Replace effect are not proved.

## Growth, limits and rollback

### Discovered defect and minimal fix

The reviewed baseline bounded **existing** witnesses at 32768 but did not reserve room for witnesses generated by its own next transaction. A fully valid graph with 32767 retained prior-root witnesses allowed a distinct Save to return Committed, creating two more roots; the subsequent full Load would refuse TooLarge. At 32768, explicit repair replacing a valid old main could similarly add its own over-budget witness. All accepted content remains on disk, but a successful API call makes the store unavailable under its own read budget. This is a real availability/backpressure defect, not CI noise, data pruning or an unsafe revision fallback.

`V3WitnessBudgetTests` builds a two-revision history by real operations, then duplicates valid prior-generation witness evidence to exercise budget boundaries; these synthetic negative fixtures do **not** claim 32768 accepted revisions or capacity Saves. Against unchanged production, all three corrected tests fail because Save/repair returns None rather than TooLarge. The first fixture attempt assumed Directory.GetFiles ordering; selection was corrected to the independently specified ordinal-1 witness, and all three then reproduce the production defect. The fix returns the count from the existing complete witness scan and preflights exact worst-case growth before any transaction Write/A: distinct Save/Restore needs two slots; an identical pending format-2 retry needs one. RecoverAuthority needs one only when replacing valid old main; matching main remains Unchanged, missing or checksum-damaged main needs zero root-witness slots. Save/Restore reuse the existing scan without another file open or Flush. Repair makes a bounded valid-main check at the witness limit before publication; it does not rescan history. No quota increase, deletion, field reinterpretation or persisted counter is introduced.

Negative assertions require TooLarge/NotCommitted, byte-for-byte retention of every original file, identical namespace, still-loadable old revision, equal-content Unchanged, conflict refusal and Restore refusal before mutation. Repair also proves missing-main publication and repeated Unchanged still work at the exact limit. Before/after targeted and final Windows results are recorded below. Existing budget exhaustion caused by previously over-budget evidence remains Fail Closed; this fix does not clean or migrate it.

[Sanitized local before/after reproduction](evidence/E3_4B_1/round4-budget-reproduction.json): unchanged production fails all three corrected negatives (actual Error=None), fixed production passes 3/3. This is Linux logic evidence, not NTFS. At source `2695004613d9d7e352b75f0cb1a8d2708c64bc31`, local full restore/Release build succeeded with zero warnings/errors; full xUnit **945/945** (451 Core, 37 Launch, 171 v1, 286 v2), zero failures/skips. Full format exited zero with the unchanged Linux Windows-spike reference-loading warnings; targeted project format was clean. Windows remains authoritative for native paths and full platform format.

Normal format 3 after n distinct Saves: n+1 pages, 2n root witnesses, A/main/lock = **3n+4**, 3076 at 1024. Each operation fully traverses O(n) history/witnesses; cumulative n Saves is O(n²). About 81MB transient managed allocation at 1024 is substantial; bounded live state is not low cumulative allocation. Recovery/failure retries can add retained stage/orphan/damage/root files beyond that formula. There is no automatic cleanup and no total-volume retention quota.

Code budgets are **16384 accepted pages including initialization** (at most 16383 later distinct Saves from a new revision-0 store), **512MiB aggregate page bytes**, **8MiB per file** (payload ≤8MiB−128), **32768 enumerated Witnesses** and codec collection/depth budgets. Actual disk/ACL limits may stop earlier. New count/payload/aggregate refusals return TooLarge **before dependency/A publication**; existing oversized/invalid graph/Witness verification refuses before mutation. No rejection advances authority, deletes history or silently discards content. A later genuine IO failure after A is instead NotCommitted/Unknown under the A/B rules, not a quota refusal. Extra failure evidence can exhaust witness/disk budgets and stop service; no unlimited sustainable-capacity claim is made. Full 16384-page and exhausted-device performance/availability are not validated by the 1024 test.

Backup(Unchanged) verifies **same-directory logical history only**; it provides no independent physical copy, directory/device-loss recovery or v1 physical-backup equivalence. Main-only damage has the explicit repair scope above; authority-slot damage can still cause total service refusal despite raw history remaining. No app production source references either engineering factory outside WorkspaceV2Store itself; v1 CreateDefault is unchanged. There is no automatic format upgrade, import, v1 access, Apply, external Export, source-Item modification or deletion path.

Rollback disables engineering callers and reverts code while retaining every v1/format2/format3 file. It does not downgrade data or permit an old client to write it. Never delete authority, copy an old Witness into place, initialize over evidence, redirect the default directory or reverse-import an unverified subset. This increment is independently removable because no product/default consumer was connected.

## Decision criteria and next work

**A — conditionally recommend merging the restricted engineering increment.** The reproduced self-exhausting witness-budget defect is fixed and all 945 Windows tests/strict collectors pass. No unresolved P0 unsafe fallback, conflicting-revision acceptance, silent overwrite or destructive/migration/default path was found **within the stated cooperating-writer/process-crash/single-main-loss model**. The user must explicitly accept these concrete engineering restrictions before choosing merge: keep all production/default/UI callers disconnected; finite page/byte/witness budgets with Fail Closed availability loss; O(n)/O(n²), substantial allocation and observed variable 1–2s operations without a latency SLA; sole-authority/all-contrary-evidence rollback/device-loss limitations; logical Backup without independent physical redundancy. These are material limits, not product approval. There is no recommendation to recover arbitrary multi-file corruption by guessing, or to write a coherent older graph after loss of all contrary evidence. Independent review and the merge decision remain with the user; PR stays Draft. A cannot authorize B or C.

**B — formal default storage: not met.** Requires accepted namespace/protocol/API/version policy; pending-A/explicit recovery/Unknown UX; sustainable retention and latency/resource budgets; safe authorized migration/downgrade; physical backup and loss recovery strategy; robust quota/disk-full operations and caller integration. No self-authored Proposed ADR closes these decisions.

**C — Windows 11 product: not met.** Requires B plus real Windows 11 hardware/storage/filter/profile/lifecycle and declared durability validation, installation/update/rollback, UI/resource/focus/security integration and applicable E2/E4/E5 acceptance. Server CI/Process.Kill does not satisfy this.

E3-4B-2 inputs: retain adverse and repeat raw samples; target future metadata-open/file growth with bounded multi-record segments and compact authority evidence, exact source/coverage/commit/restart/compatibility proofs first. Measure CPU/decode/allocation and same-operation native waits, introduce explicit resource/latency backpressure policy and physical-backup strategy. No Witness deletion, hidden cache trust, weakened SHA/history check, automatic migration or unproved garbage collection is authorized.

## Completed final implementation validation

Windows [38657468128](https://github.com/Niceskys/zhuomian/actions/runs/38657468128), source **2695004613d9d7e352b75f0cb1a8d2708c64bc31**, job 116170534066: **completed/success**. Windows build 10.0.26100.0 / Server hosted runner, .NET SDK 9.0.319/runtime 9.0.21, x64, four logical processors, actual fixed NTFS. Full restore/Release build/format/all xUnit, strict v2/v3 native/capacity/crash collectors, immutable legacy executables, all six frozen A/B/C rounds and v1 native collector pass. **945/945**, zero failures/skips: 451 Core, 37 Launch, 171 v1 Persistence, **286 v2 Persistence**. Release build zero warnings/errors. The original 942 remain; only the three justified budget negatives are added. All original 81 exception/81 killed cuts, four original terminated-child tests, ten format-3 terminated cuts and twelve targeted exception rows remain covered. Repeated startup, concurrent Busy, detailed Unknown, revision conflict, format 2/3 and v1/v2 isolation are retained.

The v2 suite took **38m43s** on this run; its larger real-native budget fixtures are retained, not replaced by counters. The pre-fix full run and this run use different hosted environments; their total duration is not a controlled fixture-setup benchmark. No claim of a test-runtime improvement is made. The unchanged immutable baseline also slows materially within the final measured workload (1024 Load median 175.43→399.18ms; CPU median 171.875→390.625ms), demonstrating a substantial common cross-run factor, not proof that the constant-time budget guard caused a twofold slowdown. CPU model/device/filter/scheduling versus fixture-history effects are not separable with this metadata. Do not dismiss this as harmless noise or infer a code speedup from cross-host absolute values.

[Full raw final Windows evidence](evidence/E3_4B_1/round4-windows.json): 135 retained-method latency samples, 30 separate profiles and **231 new same-operation samples**, bound to the commit/run. [v2-evidence artifact](https://github.com/Niceskys/zhuomian/actions/runs/38657468128/artifacts/11939457043), SHA256 `392050ab02ec4a60d86c23d23b9a51187d2710c19423f091773ac8c6303838fe`; [full TRX](https://github.com/Niceskys/zhuomian/actions/runs/38657468128/artifacts/11939826092), SHA256 `1f1f8434370cdc3c1590358cffd28dee8a9e89822c6cd5dc6194b51c2fbdfef5`. Generated JSON was inspected through the completed job log; restricted signed ZIP downloads are not claimed inspected. Artifacts contain generated fixtures/metrics only.

### Actual capacity and growth after the fix

Format 3 executes **1024 consecutive distinct-content real Saves, all Committed, zero refusals**. Complete history verifies at every checkpoint. Dispose/recreate loads 1024, stale and same-revision different content reject, identical retry is Unchanged, RestoreRevision(256) publishes **1025 with revision-256 content**. Native single-main missing/damage and A-only recovery, missing/corrupt authority/page/contradictory/future refusal and raw evidence retention pass. The new budget cases pass on actual NTFS.

| Saves/success/revision | Refusals | Pages/Witnesses/files | Logical B / data allocation B | Free B | Single Save/Load ms |
|---|---:|---|---:|---:|---:|
| 256/256/256 | 0 | 257/512/772 | 720922 / 1106128 | 32761827328 | 149.71 / 95.70 |
| 512/512/512 | 0 | 513/1024/1540 | 1439258 / 2207952 | 32777981952 | 348.66 / 213.69 |
| 1000/1000/1000 | 0 | 1001/2000/3004 | 2808588 / 4308304 | 32770719744 | 407.12 / 386.78 |
| 1024/1024/1024 | 0 | 1025/2048/3076 | 2875980 / 4411600 | 32770523136 | 404.98 / 392.86 |

Single checkpoints are not repeated medians. Managed Save/Load bytes respectively: 256 **20364664/20290872**, 512 **40545656/40473488**, 1000 **79019992/78946336**, 1024 **80912264/80838688**. Normal file growth remains **3n+4**, no additional authority file or synchronous write is introduced by the guard. Allocation excludes MFT/directory/journal/volume metadata; available volume space includes unrelated runner activity. Failure/recovery evidence can grow beyond the normal formula, and no cleanup is enabled.

### Final same-protocol and cross-protocol workloads

Uninstrumented fresh-clone controls, milliseconds. At 32/128/256/512 each cell is **one control sample**, not statistical median acceptance. At 1024 each is **seven paired samples**, median **[min–max]**. Original three-round controls remain separately in the raw record. Same-protocol comparison is immutable 514a format 2 versus current format 2; format 3 is a different-protocol workload.

| History | Operation | 514a format 2 | Current format 2 | Current format 3 |
|---|---|---:|---:|---:|
| 32 | Load | 13.72 | 13.45 | 14.24 |
| 32 | Save | 67.34 | 109.49 | 91.35 |
| 32 | Restore | 77.37 | 113.94 | 89.76 |
| 128 | Load | 49.53 | 49.14 | 49.74 |
| 128 | Save | 145.90 | 93.03 | 123.77 |
| 128 | Restore | 127.26 | 124.40 | 129.44 |
| 256 | Load | 105.24 | 96.63 | 107.84 |
| 256 | Save | 201.26 | 192.78 | 172.86 |
| 256 | Restore | 233.95 | 160.61 | 209.56 |
| 512 | Load | 222.42 | 189.33 | 259.58 |
| 512 | Save | 256.11 | 422.48 | 268.97 |
| 512 | Restore | 249.71 | 217.17 | 241.00 |
| 1024 | Load | 399.18 [384.50–500.02] | 404.49 [383.46–517.83] | 410.91 [384.62–509.16] |
| 1024 | Save | 474.75 [413.67–699.26] | 462.50 [439.80–480.93] | 513.88 [412.07–654.46] |
| 1024 | Restore | 495.02 [417.28–616.66] | 447.19 [411.16–525.00] | 490.44 [435.57–850.42] |

Seven-sample nearest-rank descriptive P90 is the listed max; no stable tail guarantee. At 1024, same-protocol current format-2 Load **+1.3%**, Save **−2.6%**, Restore **−9.7%** versus immutable baseline on this run; format-3 versus current-format-2 workload Load **+1.6%**, Save **+11.1%**, Restore **+9.7%**. The pre-fix run still has +25.6% format-2 Save and adverse near-two-second operations. Smaller single controls include unfavorable values, e.g. format-2 Save at 512; they cannot establish stable regression magnitude. **No universal improvement or latency acceptance is demonstrated.** In particular, pre-fix format-3 266ms→post-fix 514ms is a cross-host workload observation, not a controlled claim that this integer guard doubles Save cost; the immutable baseline simultaneously changes 262→475ms. The guard adds no normal Save I/O or history scan.

1024 current-format-3 profiled-fresh Load/Save/Restore medians are **401.87/474.39/485.08ms**, maxima **449.39/691.53/694.55ms**. Profiled-warm medians **393.57/437.21/501.51ms**, maxima **423.57/662.08/1140.36ms**. All seven per condition and their GC/copy/CPU records remain in JSON. Warming is not consistently favorable. A final-run format-2 profiled-warm Save takes **1019.63ms**, of which **581.34ms** is Flush, CPU **421.875ms**, GC pause **3.216ms**, atomic calls **1.700ms**. A format-3 warmed Save takes **659.13ms with CPU 656.25ms**, native-read-open **354.11ms**, Decode **118.69ms**, Flush **16.15ms** and GC **4.217ms**: slow observed operations do not all have the same dominant region. Native-open intervals include kernel CPU and waits, not just device latency.

### Final attribution counters

Medians of **seven profiled-fresh operations** at 1024. Each column is an independently calculated statistic; do not add these overlapping medians into a fictional single operation. Baseline fine counters are unavailable (null), while its elapsed/allocation/CPU samples are real. Detailed per-sample page/witness/enumeration/validation/native/read/SHA/decode/publication/Flush/atomic fields for all five scales are in the raw JSON.

| Format/op | Logical files / bytes / enum | Managed B | Page read / witness traversal ms | Native open / data read ms | SHA / Decode ms | Root publication / all Flush / atomic ms |
|---|---|---:|---:|---:|---:|---:|
| 2/Load | 3075 / 2875980 / 1 | 80833744 | 116.03 / 201.84 | 212.77 / 44.51 | 10.24 / 86.75 | 0 / 0 / 0 |
| 2/Save | 3081 / 2881596 / 1 | 80984312 | 113.61 / 200.89 | 220.26 / 45.79 | 10.15 / 82.83 | 15.73 / 69.24 / 1.84 |
| 2/Restore | 3081 / 2881590 / 1 | 80984696 | 110.80 / 193.58 | 210.00 / 45.81 | 10.13 / 80.68 | 12.67 / 46.24 / 1.83 |
| 3/Load | 3076 / 2876080 / 1 | 80837024 | 113.65 / 196.19 | 209.84 / 45.43 | 10.24 / 85.32 | 0 / 0 / 0 |
| 3/Save | 3083 / 2881796 / 1 | 80991112 | 116.11 / 200.06 | 220.10 / 47.74 | 10.42 / 85.79 | 12.91 / 38.01 / 1.93 |
| 3/Restore | 3083 / 2881790 / 1 | 80991496 | 117.12 / 200.01 | 231.22 / 48.04 | 10.50 / 88.30 | 15.68 / 42.94 / 1.97 |

Format-3 Load/Save/Restore enumeration medians **5.57/6.02/5.59ms**, witness parse/validation **5.95/5.89/5.80ms**, witness read **183.94/187.62/187.49ms**. Fresh validated opens/metadata plus Decode dominate normal growing read work; 80.8–81.0MB managed allocation at 1024 is not a sustained-memory optimization. Extra validation is not removed to meet an artificial target.

## Final decision and unresolved work

**Conclusion 2: conditionally recommend merging the restricted independent engineering increment**, on the concrete scope/risk acceptance listed under A. The budget defect that would make a successful operation self-exhaust its read budget is fixed with negative byte-retention tests; native transaction/history/capacity/isolation proof is sufficient for this limited review. No automatic merge/Ready change, no Accepted ADR and no default-storage or Windows 11 acceptance is implied. PR #31/#32/#33 remain their original Draft heads, untouched and unused.

Remaining data/availability/performance limits are real: lost/corrupt sole authority or required history refuses; coherent authority rollback after loss of **all** contrary evidence cannot be distinguished; directory/device loss has no independent physical backup; physical power/cache/filter/full-device durability and every partial native replacement outcome are unproved; pending accepted content needs explicit new-publication semantics; finite retention budgets and linear verification/quadratic sequence costs remain; original 1973ms sample's exact wait cannot be reconstructed, and new tracing does not identify the underlying device/filter/scheduler cause. These prohibit formal default enablement. There is no invented automatic recovery, deletion, migration or low-latency guarantee.

E3-4B-2 should use both pre/post raw sets without selecting favorable hosts: (1) reproduce observed Flush/open/enumeration waits with process/kernel/device tracing and bounded benchmark preparation; (2) prove bounded future multi-record segments/compact authority evidence and complete coverage before retiring sources; (3) reduce full decoding/allocation without weakening semantic verification; (4) define resource/backpressure, physical-backup, pending/Unknown recovery and authorized compatibility policy. Formal default and Windows 11 product gates remain explicitly unmet. Rollback remains code-only with engineering callers disabled and every v1/v2 byte retained.
