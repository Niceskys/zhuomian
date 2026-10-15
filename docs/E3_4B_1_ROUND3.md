# E3-4B-1 round 3: independent reservation authority

Starting reviewed SHA: `514a5594f594728d2093cb041ed3552472384472`. Final submitted SHA is the immutable `commitSha` in the final Windows `v3-platform.json` artifact and PR delivery comment; a document cannot contain its own Git object hash. PR [#38](https://github.com/Niceskys/zhuomian/pull/38) remains Draft. [ADR-0012](ADR/0012-independent-v2-storage.md) remains **Proposed**. main and PR #31–#33 are not changed or dependencies.

## Actual implementation and compatibility

Only `WorkspaceV2Store.cs` changes production code. PhysicalStorage, NativeStorage, v1, Core, launching, application defaults and all 889 existing test bodies/assertions remain unchanged. The existing engineering factory/ForTests still use binary format 2. An explicit `CreateRecoverableEngineeringStore()` selects **binary format 3** in the independent candidate WorkspaceV2 namespace. It is not a default, upgrade, migration or released protocol. Format 3 uses the same bounded page/root layouts with a different version, and a separate `authority-v3.bin` slot. Domain schema and Workspace codec remain 1.

Format 2 data refuses the new factory's writes/initialization/repair. Format 3 and mixed authority markers refuse format 2 operations. Missing state with existing legacy, stage, orphan or unknown data cannot be initialized. No format is automatically converted; no history is deleted or renamed by maintenance. The fixture's original modes remain format 2, so its original 81 exception cuts and 81 terminated-process cuts continue exercising the original transaction order. New modes explicitly choose format 3.

`RestoreRevisionTransaction` adds observable StorageResult/CommitState alongside the existing RecoveryResult projection; the original Restore APIs retain their contracts. Unknown main publication is no longer hidden from callers that explicitly choose this detailed engineering API.

## Candidate comparison and indistinguishability

| Candidate | Coverage/failure proof | Cost/compatibility judgment |
|---|---|---|
| Continuous authority journal | Needs an independently anchored complete tail. A missing final record cannot be distinguished from no final commit by scanning surviving names. | Append/flush/torn-tail/native identity and tail-commit semantics add scope; deferred. |
| Loose reservation commitment record | Written after old A leaves an A-success/no-record crash window. Written before A either accepts earlier or is only an orphan. | Cannot retrofit unchanged format 2 semantics. |
| Redundant authority generations | Two independently replaced copies need a single acceptance definition and rules for disagreement. An apparently old valid copy is not proof of absence of a newer lost commit. | More writes/slots without a complete selector proof are not inherently safer. |
| Fully covered chain | Exact contiguous ordinals, digests and predecessor links prove coverage **to a surviving authoritative head**. | Already implemented; does not prove an absent later head never existed. |
| Separate reservation slot, main selection | A publishes the sole accepted head; B cannot replace that slot. Surviving A plus full chain proves the floor under **single-main-loss** assumptions. | Selected opt-in format 3. Same three dependency/A/B writes and same normal file-count slope. No old-format migration. |

Concrete indistinguishable format-2 histories: start at R1 and prepare conflicting same-revision X and Y by two pre-A interruptions; both raw pages and A staging roots remain. Hx then retries Y but stops before its second write (leaving a duplicate Y page stage), and retries X through A, preserving R1 and leaving a duplicate X page stage; stop before B and lose current state. Hy symmetrically retries X before its second write, then accepts Y at A, stops before B and loses state. Couple the random names of the X/Y page stages and R1 witness across histories; the final A stage disappears into the lost state in each. Every remaining byte/name is identical, including both complete branches, staging roots and R1 witness, but Hx must preserve X's commitment and Hy must preserve Y's. Neither surviving staged A root is accepted evidence. An algorithm cannot choose one pair or permit conflicting overwrite from this observation. More generally, identical surviving observations with different necessary commitments cannot be distinguished by recovery over those observations. Format 2 remains Fail Closed.

Format 3 changes the acceptance point explicitly: publishing authority is A, not replacing main. It does not promise that a lost **authority slot** can be reconstructed from prior copies. That is the same fundamental absence-of-later-authority problem in a smaller, declared failure domain.

## Authority state machine and ordered proof

Under pinned trusted NTFS ancestors and exclusive workspace.lock:

1. Validate/canonicalize the requested snapshot. CreateNew staging, split write, Flush(true), native bounded exact readback. Publish a non-overwriting immutable page, then fresh final-path readback. This is dependency creation only.
2. **A acceptance:** atomically publish format-3 `authority-v3.bin` containing Workspace ID, exact new chain count/head and the prior selected main ordinal. Replacement preserves the prior slot as a unique root witness. This is the sole revision/content acceptance boundary.
3. **B publication:** atomically publish `state-v2.bin` with the same ID/count/head and main=count. It selects the already accepted snapshot. Replacement preserves prior main bytes. B never overwrites authority.

Before each new distinct Save, normal verification requires main to match the latest accepted head; therefore the next page's prior-main selector refers to a completed B. Format 3 also validates prior-main=ordinal−1 in every page (initial page zero); this is an ordinal publication-prefix rule, not a consecutive-revision requirement. Revision gaps remain legal. A pending main requires explicit recovery, rather than accepting another generation or silently returning the older main as Normal. This stricter behavior is confined to new format 3.

| State | Authority / auxiliary / orphan evidence | Restart and missing-main behavior | Permitted operations |
|---|---|---|---|
| Uninitialized | No accepted slot; only empty coordination lock | Explicit fresh Initialize only; nonempty uncertain data refuses | InitializeNew |
| DependencyWritten / ReservationPending | Previous accepted slot remains authoritative; new page/stage is orphan, not a commitment | A pre-publication crash does not burn the new revision; full previous graph remains required | Normal old Load/Save/Restore if old B matches; partial initial store refuses |
| ReservationAccepted / MainPublicationPending | New A slot plus complete immutable chain proves accepted pair/floor; prior main/root witnesses are auxiliary corroboration | Old/missing main cannot be called latest committed; explicit recovery may publish accepted content now | RecoverAuthority; ordinary Load/Save/Restore refuse until B |
| MainCommitted | Matching A and B plus fully verified chain and every extant witness | Restart loads selected latest; main loss still leaves A proof | Load/Save/logical Backup/Restore; identical Save Unchanged |
| CommitUnknown | Submission began but exact destination/full proof cannot be read | Never reported as NotCommitted merely because an exception occurred; restart verifies fresh evidence | Refuse mutations until verification/recovery resolves |
| AuthorityDamaged / RecoveryRequired | A absent/corrupt/unsupported, chain absent/corrupt/conflicting, or contradictory witnesses/main | No maximum/recent-candidate fallback; no empty initialization | Complete refusal; preserve bytes |

DependencyWritten and ReservationPending are equivalent for commitment decisions: neither accepts new content. ReservationAccepted and MainPublicationPending are equivalent for restart decisions: latest pair is accepted but B may need publication. RecoveryRequired is a refusal outcome shared by incomplete B and damaged authority; only the former has a provable explicit repair path.

Every operation freshly traverses the entire linked chain (strictly decreasing revisions, legal gaps, exact contiguous ordinals, Workspace identity, body/whole-page hashes, previous-main transitions, exact decoded payload boundaries). Every extant witness is validated against that completed traversal. The bounded head array is derived evidence, not a persisted trusted index. A's prior-main value must equal the newest page's prior-main field. Supported valid main roots must be either that previous main or the accepted latest ordinal, and their head must be a verified chain member. Foreign/conflicting/newer/unsupported main roots refuse repair. An older main below the prior selector also refuses. No raw dependency is chosen by filename or scalar maximum.

Format 3 additionally rejects **same-generation witnesses**: A replaces only a lower generation; B replaces a lower selected main or damaged/absent main; identical B/repair retries are Unchanged. Thus a root witness at the current A ordinal means that supposedly current authority has been replaced, or publication has an unresolved partial effect. Restoring an older valid A while retaining its replacement witness cannot silently lower the floor. If every contrary witness and newer slot is also lost, no such proof remains and arbitrary authority rollback is still outside the recovery guarantee. This rule is confined to format 3; format 2 legitimately retains its current A as B's replacement witness.

## Explicit recovery boundary

`RecoverAuthority()` first proves intact format-3 A, its entire chain and all extant witnesses. It accepts absent main or checksum-damaged main **only when its version header remains readable and equals 3 and its length is exactly the supported 100 bytes**. Truncated/extended/unknown/future/legacy input refuses. Native linked/reparse/sharing/identity restrictions are unchanged. A valid but conflicting main refuses rather than being treated as disposable corruption.

If main already matches A, return Unchanged. Otherwise write/flush/readback a new B and atomically publish it. This **new** main publication deliberately selects the already accepted latest pair; it does not claim that an A-only snapshot had previously been published. Corrupt main bytes are retained as unique `damage-*` evidence; valid replaced roots remain root witnesses. The accepted slot never changes during repair. Pre-B termination leaves the same accepted floor; post-B termination leaves matching main; both allow repeat repair. Unknown publication is resolved by fresh proof/exact destination comparison or remains Unknown.

Single missing/damaged main, including A-success/B-failure, is repairable under the stated scope. Simultaneously missing/damaged A, missing/corrupt linked page, contradictory extant evidence, foreign data or future format refuses. Loss of redundant witnesses alone does not erase pairs already covered by intact A/full chain, but unreadable/conflicting surviving witnesses cannot be ignored. There is no writable candidate-only/read-only older-main fallback: ordinary Load returns no Workspace/RecoveryRequired for incomplete B, avoiding an accidental stale view consumed as Normal.

Assumptions: cooperating writers use the lock; successful same-directory NTFS namespace publication is atomic for the tested process-crash model; immutable page dependencies and A remain intact when only main is damaged. SHA256 detects accidental corruption and binds exact bytes; it is not authentication against a same-account adversary replacing an entire self-consistent graph. Older valid **authority** substitution with loss of all contrary evidence, arbitrary multi-file destruction, lost directory/device and physical power reorder remain unproved. File.Replace commits one name only. Flush(true) is an API request, not a guarantee that controller/media/directory metadata survive physical power loss. No claim of complete device durability or Windows 11 acceptance is made.

## Tests, growth and measurements

Added targeted tests cover single main loss/corruption after A-only or B, conflict/stale/identical retry and Restore above independent expected floors; incompatible/mixed/future formats; foreign/conflicting authority, missing/corrupt pages, truncated main, old valid main substitution, authority links; shared write-stage interruption; detailed Restore Unknown under real Windows sharing; ten real child terminations at initial/save A/B and recovery B, with cross-process Busy and fresh-process Load. Expected floors are specified from the injected execution history, not copied from recovery output. Original evidence is checked before subsequent intentional transactions.

New capacity/performance fixture executes **1024 distinct-content real Save calls in immutable reviewed 514a5594 format 2 and current format 3**. Current-format-2 measurement controls use exact owned copies of that actual baseline history; their 15 real timed distinct Save calls are not presented as a second sequential-capacity run. Its original separate 1024-Save capacity test remains unchanged. No copy substitutes for a format-3 capacity Save. Release baseline is built from an isolated detached shared clone of that exact commit, never the active branch/main. Decode/fixture copies are outside timed operations. At 32/128/256/512/1024, warm Load then three balanced ABC/CBA/ABC rounds measure Load on original/control roots and Save/Restore on generated clones. All latency samples disable profiling in every binary. The immutable baseline assembly/JIT is reused across clones, just as the current assembly is; loading a fresh assembly per sample would introduce an unfair cold-code penalty.

Additional separate instrumented operations attribute logical read files/bytes, page/witness read times, witness total traversal, enumeration creation/MoveNext, witness parsing/validation, native validated read opens, file data/EOF/length read, page/root SHA verification, decode, Flush(true), atomic root publication and total root publication. These nested timings overlap; summing them as independent device stages would be false. Workspace envelope SHA is inside Decode; serialization/write SHA is outside the read-checksum counter. Native open timing includes CreateFile/GetFileInformation/GetFinalPath checks; it is not isolated NTFS metadata/device time. Validated read-open count is recorded, **not** all directory/lock/write/metadata handles. Fine CPU/kernel/device attribution and root-pin/lock-open breakdown would need additional tracing and remain open.

Checkpoints 256/512/1000/1024 record success/refusal, current revision, complete history, actual files/pages/witnesses, logical bytes, per-file NTFS AllocationSize (excludes directory/MFT/journal/volume metadata), free bytes, Save/Load latency and managed allocations. Fresh-instance Load, stale/conflicting/identical retries and RestoreRevision(256)→1025 are asserted. Format 3 has n+1 pages, 2n witnesses, A slot, main and lock: **3n+4**, exactly the old slope/count, 3076 files at 1024. It replaces the initial extra format-2 witness with one fixed accepted slot. Failed/recovery transactions may retain additional stage/orphan/damage/witness evidence. No cleanup is used to manufacture counts.

This is a Persistence diagnostic microbenchmark, not product performance calibration. Format-2 before/after is same protocol; format-2/3 is a workload comparison with different acceptance semantics. Three pairs reveal medians/unfavorable samples and variability, not a universal latency guarantee. Full traversal remains O(n), cumulative Save work O(n²). Bounded multi-record segments/compact authority logs/trusted-generation indices/raw-preserving compression are deferred to E3-4B-2: exact coverage and commit proof must precede any source retirement. Packing alongside originals alone does not reduce disk use.

Completed Windows results and numeric comparisons are recorded below. Local Linux results cannot substitute for NTFS. The original round-2 collector stays strict and describes format 2; the additive format-3 collector requires its own real NTFS capacity, 135 latency samples, 30 profiles and targeted recovery evidence. No unexecuted validation is marked passed.

### Development failures retained

Windows [38596725108](https://github.com/Niceskys/zhuomian/actions/runs/38596725108), code `3a4dd91b50ab894527497c19df636935f0e34d12`: build/format passed; 936/937 tests passed, one new killed-process fixture cleanup failed sharing on workspace.lock. [38598555408](https://github.com/Niceskys/zhuomian/actions/runs/38598555408), code `d94d4cca6f6cc942ee864b9153d131fb5d16f29f`: build/format passed; 938/940 passed, two instances of the same new cleanup failure; zero skips. Neither run is a full pass. Native proof/legacy/performance collectors correctly did not run after suite failure, and downstream missing-artifact steps also failed. No aborted-test-host or physical-power claim is inferred; the suites completed in 25m14s/19m13s respectively.

The new killed-process class is now serialized with filesystem benchmarks. It synchronizes on independently observed native exclusive mutex availability after child exit, with a bounded 5s condition wait and emitted attempt/wait metrics; no Save/Restore/test assertion is rerun. Generated fixture cleanup acquires a delete-sharing native guard before removing only its synthetic coordination file and invoking the original unmodified helper. Persistent sharing still fails. The owned-fixture benchmark avoids copying read-only Load roots and generating a redundant second format-2 sequential history; all 135 pairs, 30 profiles, original 889 tests and format-3 real 1024 Saves remain. This improves test organization/cost, not production validation strength or a claimed storage latency improvement.

## Separate acceptance gates and rollback

**A — restricted independent engineering increment:** eligible for independent review only after all retained/new tests, native/process proof, actual capacity, compatibility/isolation, measurements and unresolved-risk review pass. A provable Fail Closed boundary for authority loss can be acceptable here without automatic recovery from every single file failure, provided this opt-in interface is not consumed as production storage. Human review/merge decision remains the user's. Current evidence is pending; no gate is predeclared closed.

**B — default Workspace candidate:** not reached. Requires accepted namespace/protocol/API/recovery decisions, user-visible recovery and Unknown handling, safe authorized import/downgrade, sustainable history organization/budgets, independent physical backup/export policy and performance acceptance. Logical Backup(Unchanged) remains same-directory history, not v1's physical duplicate or device/directory redundancy.

**C — Windows 11 product:** not reached. Requires B plus real Windows 11 devices/profiles/storage/filter/full-volume/durability acceptance for declared guarantees, installation/update/rollback and all applicable E2/E4/E5 product integration gates. Windows Server CI and killed children cannot certify these.

Rollback code independently, preserve every v1/v2/format-3 file, keep v1 defaults. Old format-2 engines must refuse format-3 authority, including missing main. Do not redirect old clients, delete the new slot, copy an older witness over authority, reverse-import or initialize over evidence. Disable the engineering caller when reverting. E3-4B-2 should prioritize bounded multi-record future segments and fewer future witness opens with a complete source/coverage/publication proof; no unreviewed GC or migration.

## Completed Windows evidence (implementation 984204f)

Validated production/test SHA: `984204fa3a6df41d28e23639320e453a0a908fd0`. Windows run [38611024433](https://github.com/Niceskys/zhuomian/actions/runs/38611024433) **completed/success**. Windows Server 2025 build 10.0.26100.0, SDK 9.0.319/runtime 9.0.21, x64, fixed NTFS. Restore/Release build/full format/all xUnit/strict v2+format-3 collectors/immutable legacy compatibility/all six frozen A/B/C rounds/native v1 collector passed. Build 0 warnings/0 errors. **942/942**, 451 Core + 37 Launch + 171 v1 + 283 v2; 0 failures/skips. Original 889 remain, **53 new tests**. Local Linux full 942/942 also passed; its conditional native cases are not NTFS evidence and full format reports unloaded unchanged Windows-spike references despite exit 0.

[Raw format-3 capacity, 135 latency samples and 30 profiles](evidence/E3_4B_1/round3-windows.json) and [SHA-bound platform summary](evidence/E3_4B_1/round3-platform.json) are generated fixtures only. [v2-evidence artifact](https://github.com/Niceskys/zhuomian/actions/runs/38611024433/artifacts/11920920097), digest `5d578990f25e3857a1a664ae95c8cf7ca1bb6479e12fc604d2a8daab8ef797ee`; [full TRX](https://github.com/Niceskys/zhuomian/actions/runs/38611024433/artifacts/11920727442), digest `435e51ed8e64de9e6390606abdaa77768dae74ede5f1ad06f8ffb4f3bfa4ad92`. Raw synthetic matrix/termination/native/quiescence and original format-2 measurements remain in those artifacts. Signed artifact downloads into this executor are restricted; the generated metrics were inspected through the completed GitHub job log, not a claimed ZIP inspection.

### Actual format-3 capacity

All **1024 distinct-content real Save calls returned Committed; zero refusals**. At every point full graph verification passed. Final fresh-instance Load=1024; stale/conflicting retry rejects, exact retry Unchanged, RestoreRevision(256)=1025 with actual revision-256 content. Both intact-slot/main-loss cases before/after B restore accepted 10 and subsequent Restore uses 11. Missing/corrupt/foreign/future authority, history damage, incompatible/mixed inputs, conflicting witnesses and old A substitution with surviving contrary evidence reject. Ten real killed cuts, twelve exception rows, two actual wrong-byte A/B publications, native post-B Unknown, authority symlink/hardlink and all original NTFS tests passed. Original 81 exception/81 killed matrices plus four original killed cuts remain.

| Saves / success / revision | Rejects | Pages / witnesses / files | Logical bytes | File allocation bytes | Free bytes | Save / Load ms | Save / Load managed bytes |
|---|---:|---|---:|---:|---:|---|---|
| 32 / 32 / 32 | 0 | 33 / 64 / 100 | 92512 | 142032 | 30071762944 | 99.53 / 9.66 | 2701192 / 2631944 |
| 128 / 128 / 128 | 0 | 129 / 256 / 388 | 361754 | 555216 | 30067527680 | 88.31 / 32.61 | 10271344 / 10200832 |
| 256 / 256 / 256 | 0 | 257 / 512 / 772 | 720922 | 1106128 | 30072901632 | 219.09 / 62.29 | 20363248 / 20291408 |
| 512 / 512 / 512 | 0 | 513 / 1024 / 1540 | 1439258 | 2207952 | 30067191808 | 302.25 / 122.80 | 40547056 / 40473344 |
| 1000 / 1000 / 1000 | 0 | 1001 / 2000 / 3004 | 2808588 | 4308304 | 30059741184 | 270.42 / 241.19 | 79019984 / 78946328 |
| 1024 / 1024 / 1024 | 0 | 1025 / 2048 / 3076 | 2875980 | 4411600 | 30059544576 | 272.85 / 257.47 | 80912336 / 80839952 |

These are single sequential checkpoints, not medians. 1024 remains **3076 files / 2,875,980 logical bytes / 4,411,600 data allocation bytes**, same 3n+4 growth/count as format 2. Allocation excludes MFT/directory/journal/volume costs; free-space fluctuations include unrelated hosted-runner activity.

### Repeated latency and regressions

Values are median **[worst] ms**, three samples per cell. Same-protocol comparison is 514a5594→current format 2. Format 3 is a different-protocol workload comparison. No physical-device I/O or product threshold calibration is claimed.

| History | Operation | 514a5594 format 2 | Current format 2 | Current format 3 |
|---|---|---:|---:|---:|
| 32 | Load | 8.67 [9.20] | 9.08 [9.27] | 9.06 [9.32] |
| 32 | Save | 96.60 [257.73] | 96.78 [160.01] | 94.22 [161.36] |
| 32 | Restore | 179.51 [206.48] | 92.56 [107.39] | 77.01 [198.21] |
| 128 | Load | 32.09 [33.39] | 33.38 [33.39] | 33.57 [34.14] |
| 128 | Save | 149.53 [361.88] | 210.55 [456.35] | 163.57 [228.96] |
| 128 | Restore | 242.53 [848.09] | 212.63 [250.91] | 203.36 [1024.67] |
| 256 | Load | 64.14 [65.53] | 64.72 [67.76] | 69.04 [91.23] |
| 256 | Save | 338.59 [389.91] | 162.24 [285.79] | 187.96 [581.43] |
| 256 | Restore | 289.55 [379.66] | 319.66 [467.55] | 146.27 [962.10] |
| 512 | Load | 136.74 [144.23] | 125.89 [143.66] | 128.72 [133.95] |
| 512 | Save | 433.47 [942.29] | 812.92 [868.74] | 295.24 [474.98] |
| 512 | Restore | 340.98 [812.92] | 402.46 [420.74] | 209.34 [237.43] |
| 1024 | Load | 272.15 [290.46] | 261.33 [290.83] | 259.20 [277.32] |
| 1024 | Save | 691.24 [1200.66] | 857.75 [1640.52] | 1883.19 [1973.08] |
| 1024 | Restore | 661.80 [983.33] | 586.80 [1624.54] | 408.68 [712.91] |

**No overall speedup is established.** At 1024, same-protocol Load median improves 4.0%, Save regresses **24.1%**, Restore improves 11.3%; worst current-format-2 Save/Restore exceed 1.6s. Format-3 Save median **1883.19ms**, versus current-format-2 857.75ms (**+119.5%**); format-3 Restore median 408.68ms versus 586.80ms, and Load 259.20ms versus 261.33ms. Save variability is large: format-3 661.86–1973.08ms. Smaller scales also show mixed outcomes (e.g. format-2 Save at 512 +87.5%). Single sequential 1024 Save 272.85ms is not interchangeable with warmed cloned mutating samples. Hosted filesystem/open/Flush/publication waits are visible but causal attribution of every adverse sample is **unclosed**; it is not valid to dismiss the regression as noise or promise user latency. Three samples are diagnostic, not stable tail acceptance.

1024 managed Load medians: baseline 80,834,224 B; current format 2 80,833,808 B; format 3 80,836,072 B. This round establishes no meaningful allocation reduction. The root/page organization remains the dominant growing cost; no compaction or reduced integrity checks were used.

### Native cost attribution at 1024

Separate instrumented operations, not the latency sample above. Times overlap; for example total publication contains root Flush/atomic work, all-write Flush also includes the dependency, and read-open timing is part of page/witness reads.

| Format / operation | Files / bytes / enumerations | Page read / witness read ms | Enum / witness validation ms | Validated native open / data read ms | Read SHA / decode ms | Root publication / all Flush / atomic root ms |
|---|---|---|---|---|---|---|
| 2 / Load | 3075 / 2875980 / 1 | 74.73 / 111.05 | 3.66 / 2.56 | 133.62 / 30.41 | 5.92 / 58.56 | 0.00 / 0.00 / 0.00 |
| 2 / Save | 3081 / 2881596 / 1 | 79.19 / 115.20 | 4.18 / 2.66 | 139.18 / 28.27 | 6.19 / 64.77 | 1086.18 / 916.00 / 413.39 |
| 2 / Restore | 3081 / 2881590 / 1 | 258.21 / 119.68 | 3.77 / 2.70 | 322.58 / 32.40 | 6.34 / 63.84 | 15.93 / 175.47 / 1.40 |
| 3 / Load | 3076 / 2876080 / 1 | 79.65 / 115.41 | 3.70 / 2.63 | 140.57 / 32.62 | 5.99 / 60.06 | 0.00 / 0.00 / 0.00 |
| 3 / Save | 3083 / 2881796 / 1 | 239.06 / 112.29 | 3.61 / 2.61 | 295.11 / 32.50 | 6.46 / 68.51 | 23.61 / 37.40 / 1.43 |
| 3 / Restore | 3083 / 2881790 / 1 | 98.29 / 114.19 | 272.64 / 2.66 | 157.50 / 29.73 | 6.27 / 73.11 | 168.92 / 699.77 / 1.41 |

Fresh opens/metadata validation dominate steady Load's measured I/O portion (3076 validated reads; native open 140.57ms versus data/EOF 32.62ms), with 60.06ms payload decode and 5.99ms page/root SHA. Enumeration can also stall (instrumented format-3 Restore enum 272.64ms versus ~3.7ms Load). Mutating waits vary sharply (format-2 instrumented Save Flush 916.00ms/atomic root 413.39ms versus format-3 Save Flush 37.40ms/atomic root 1.43ms). These separate samples expose bottlenecks/variation, not an explanation that proves the 1883ms paired median harmless. Native syscall CPU/disk decomposition, all pin/lock/write handle counts and repeat-host tail attribution remain open. Full verification is O(n) per operation and cumulative sequential Save work O(n²).

### Final review judgment and open work

**A:** scoped recovery/commitment/capacity/native/process/compatibility evidence is sufficient to submit this restricted, unenabled increment to independent code review. Fail Closed for authority loss is an explicit boundary, not an unsafe fallback. **Unconditional independent merge readiness is not declared:** independent review and explicit acceptance of the measured performance regression/remaining I/O attribution are still pending. The user decides merge; PR remains Draft. This distinguishes permission to review from a product-release or automatic merge recommendation.

**B/C:** default storage and Windows 11 product gates are not met. Open: authority-slot loss/all-contrary-evidence rollback, directory/device loss and actual full-device/power/filter guarantees; pending-data publication UX/API acceptance; safe authorized old-format import/downgrade; sustained bounded future segment/witness organization and performance/tail acceptance; independent physical backup/export design; Windows 11 real-profile/device plus applicable E2/E4/E5 integration/installation/rollback acceptance. The candidate never enables Apply/export, migration/default/UI or external Item writes. Recovery cannot restore a previous user selection while skipping a pending reservation's B in format 3; that proposed prefix semantics needs explicit default/API acceptance.

E3-4B-2 should first reproduce and attribute adverse NTFS mutating waits, then design bounded multi-record future pages/compact authority evidence with an exact coverage/source/commit/restart proof. Reduce future metadata opens without hiding historical promises. No automatic migration, source retirement, Witness deletion or GC is authorized. Revert code independently, disable the engineering caller, retain all version-2/version-3/v1 data; old clients must refuse incompatible records, never delete authority or initialize over them.
