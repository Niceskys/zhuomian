# ADR-0012: Independent v2 streaming reservation and publication core

- Status: Proposed
- Date: 2026-10-14
- Owners: Niceskys (acceptance), AI implementation (code and evidence)
- Supersedes: none

## Context and scope

E3-4A main b3b278b0759d5c0af55e224b250c27c33a6bbb10 is the base. All fetched remote ADR trees were checked: 0005–0007 belong to independent E2 Drafts; 0012 is available. This independently testable Persistence increment implements a runnable transaction core, not product integration. PRODUCT_SPEC, ARCHITECTURE, SECURITY and ADR-0009/0010/0011 retain their gates. No specification conflict authorizes automatic migration or default switching.

## Proposed namespace ownership

Explicit `WorkspaceV2Store.CreateIndependentEngineeringStore()` selects current-user LocalApplicationData/Zhuomian/WorkspaceV2 on fixed local Windows NTFS. This name is an engineering candidate, not an accepted application default. `WorkspaceFileStore.CreateDefault()` is unchanged. Each root has its own workspace.lock, state, staging, immutable segments and replacement witnesses. Root injection/faults are internal owned-fixture composition only. v2 never reads, imports, moves, overwrites, deletes or falls back to v1. Old default executables only address Workspace; this is path isolation, not an ACL security boundary against malicious same-account code.

Only `InitializeNew(snapshot)` creates state. Existing entries other than the owned lock refuse initialization; missing/corrupt state never becomes an empty workspace. A partial initial transaction requires the original snapshot's explicit Save retry if a valid reservation root exists. If root is absent with staging present, automatic recovery/initialization refuses.

## Format and complete generation coverage

Binary little-endian version 2 is distinct from domain schema 1 and the existing raw snapshot envelope codec version 1. State body: version int32, Workspace GUID 16 bytes, ordinal int64, head SHA256 32 bytes, selected-main ordinal int64; SHA256 of the body follows. Raw segment body: version, Workspace GUID, ordinal, predecessor hash, previously selected main ordinal, raw snapshot byte length int32 and exact raw encoded snapshot; SHA256 follows. Segment basename derives only from SHA256 of the entire segment. Random staging and root witness names are owned protocol identifiers. No archive-controlled paths are extracted.

A segment is a bounded one-record raw history page. The linked head and exact contiguous ordinals form the complete generation manifest; there is no separately trusted index or persisted cache. The descending traversal verifies every hash, ordinal, previous-main transition, GUID, codec result and strictly descending revision. The terminal ordinal must reach one and its predecessor must be zero. Gaps in revisions are legal; gaps in generation coverage refuse. Every accepted revision has retained raw content evidence. Strict ascending new reservations make a streaming uniqueness proof possible without an unbounded revision dictionary. Same latest revision/content is idempotent; equal latest revision/different content conflicts; all older revisions refuse even if identical. Foreign IDs, future versions, partially understood payloads, missing pages and corrupt roots refuse.

Bounds: 8 MiB per physical file including headers, raw payload at most 8 MiB minus 128 bytes, 16384 linked pages, 512 MiB aggregate history reads per operation. Existing codec depth/object/string/collection budgets remain. A new save verifies existing history and refuses if its added page would exceed the aggregate budget. These finite resource limits are not infinite retention; disk can fill sooner. History files are graph dependencies rather than the v1 wildcard quota. Only the current page and constant-count Workspace snapshots are retained while scanning; no operation-local or cross-process history cache bypasses verification. Total work remains proportional to history, total allocation/work across 1024 saves quadratic. One-record pages trade more metadata operations for a small reviewable first protocol; packing several records is a future independently verified optimization.

## Two authoritative commits and failure semantics

Under pinned directories and exclusive root-local lock, verify the entire authoritative graph. Encode/validate a snapshot, CreateNew staging through native single-link/no-reparse validation, split write, Flush(true), native exact readback. Non-overwriting segment publication is dependency creation, not a revision reservation. Staging/orphans remain but are never accepted by name or maximum ordinal alone.

A single root publication selecting the new head and old main accepts the revision/content reservation (A). A second single root publication selecting that head as main commits the Workspace (B). Both publish only already verified immutable dependencies. Initial A selects zero; Load refuses until B. File.Replace preserves unique exact prior-root witnesses. Initial publication uses non-overwriting Move. All files and root witnesses remain; Backup is verified Unchanged because every published snapshot is already retained. No cleanup/export/Apply is introduced.

`NotCommitted` describes main publication, not absence of a reservation. Failure after A burns the content pair; identical retry resumes B and different content refuses. The API does not yet expose a separate reservation-result field: callers must reload/retry through verification. Post-submission errors verify the actual full graph: matching B returns Committed, matching A remains NotCommitted with error, unreadable/uncertain state returns Unknown. A clean matching main retry returns Unchanged. A failed write before submission cannot erase an already accepted reservation. Restart replays only the valid authoritative root, not the highest orphan filename.

File.Replace is one namespace publication, never multi-file atomicity. Flush(true) is an API request, not proof of device/physical power-loss durability. Directory metadata flushing, real device/cache/filter behavior and physical power cuts are unverified.

## Recovery and history retention

Load verifies all dependencies and reads the selected immutable snapshot. Restore or RestoreRevision selects only a snapshot whose main publication is evidenced by the current selector or a verified subsequent record's prior-main selector. Unpublished reservations cannot be recovery sources. Restore creates a new reservation/main pair above the latest fully verified reserved revision; long.MaxValue refuses. Original bytes remain. With missing/corrupt root or a missing/corrupt segment, no fallback can prove the latest reservation floor: mutation and automatic recovery refuse. **Recovering a damaged authoritative root from retained witnesses with complete reservation proof is not implemented and remains a blocking gate.** Operator selection of an older root would risk rollback and is not a supported repair.

## Threat and privacy review

Reuses existing native root/ancestor identity, fixed NTFS, final-path/kind/no-reparse/single-hardlink checks and cooperating-process lock. New staging opens are native CreateNew on Windows. Native reopen verifies exact bytes before publication. Links/aliases, denied permission/sharing, root rename, missing pages, future formats and deterministic transaction faults have negative tests. SHA256 detects corruption and binds content; it is not authenticated ownership. Privileged/kernel or malicious same-account graph replacement, namespace destruction and deliberate root rollback are outside the existing security guarantee. No Item resource is opened or executed. Only generated synthetic fixtures enter CI artifacts; raw private Workspace data stays local. Launch authority, UI/E2/E4, installers, updates, discovery, assets and v1 production source are unchanged.

## Import, rollback, costs and unresolved gates

Future import requires explicit authorization, exact supported/unknown/quarantined/damaged input coverage, reservation-preserving migration/rollback, compatibility fixtures and independent acceptance. No seamless downgrade is promised: v1 has its separate older state. Revert this code independently, retain all v1/v2 files, and do not redirect an old client into v2 or reconstruct v1 from an unverified subset. Removing code never permits deleting newer reservations.

Capacity/fault evidence and remaining gates are in [E3_4B_1_EVIDENCE.md](../E3_4B_1_EVIDENCE.md). Windows Server hosted NTFS is not Windows 11 real-device acceptance. Damaged-root recovery, exhaustive failure matrix, repeated performance calibration, physical disk-full/device evidence and user acceptance remain open. ADR remains Proposed; passing tests do not accept the namespace or product protocol.

## Round 2: authority loss, witness validation and safe reader optimization

Status remains **Proposed**. No namespace/default/migration/retirement decision is accepted.

The current complete graph now also validates every extant root witness: checksum, supported version, exact structure, Workspace ID, ordinal, head membership and main selector must agree with a fully verified page. An evidenced main publication cannot have a selector above the current selection (same ordinal) or the next page's prior-main selector; this rejects rolling a known B back to A or omitting B in a successor. A witness with an ordinal above the current root refuses recovery/mutation; a witness on another branch or with an invalid selection refuses. A bounded array of at most 16384 digest references and prior-main ordinals supports one graph traversal followed by one witness traversal, not repeated full-chain scans for each candidate. At most 32768 witnesses are read; unknown `root-*` entries, including directories, are not silently ignored. This increases fresh native opens to approximately three per revision and adds bounded metadata memory; it does not use an unverified index or cross-process cache.

Witnesses cannot establish absence of a later lost reservation. In particular: publish a raw page; replace the root at A, preserving the **old** root; lose the new root before B. No retained root selects the new page. Pre-A interrupted branches may have equally complete raw pages. Presence, time, file name, maximum revision or parseability cannot identify the accepted branch. If B completed, its replaced A witness usually proves the reserved content, but the protocol has no independently anchored inventory proving there was no still later A whose only authority was lost. Exhausting visible candidates is not proof that no authority was lost. The implementation therefore still refuses all missing/damaged/future-authority recovery, rather than attempting candidate selection. It does not republish old roots, initialize empty data, or authorize writes on a candidate-only view. A valid older root with a retained newer witness is detected; rollback of an A-only root without a newer retained witness is not provably detected. **Safe writable root repair remains a P0 blocker.**

A future protocol would need a separately reviewed, redundant reservation/publication evidence scheme with explicit transition order, completeness anchor, crash proof and older engineering-store handling. Adding a loose "accepted" file after A leaves a crash window; adding it before A changes the acceptance point. Neither is a backward-compatible repair by itself. This round does not silently introduce that format/acceptance change.

v2 physical reads now use one owned exact-length bounded buffer, `ReadExactly`, EOF/length verification, and the same native single-link/final-path/kind/no-reparse handle and share flags. All checksums, payload decoding, revisions, selector transitions and witness checks remain fresh on every operation. v1 reader/source are untouched. An internal owned-fixture benchmark can select the original PhysicalStorage reader with the identical round-2 protocol checks; it never selects a different production format or skips verification.

Additional write seams distinguish create, partial write, pre/post Flush, pre/post readback, segment dependency publication, A and B. These are deterministic exceptions and owned-child termination seams, not physical power-loss experiments. The full round-2 evidence and limitations are in [E3_4B_1_ROUND2.md](../E3_4B_1_ROUND2.md).

### Backup support boundary

`Backup()` verifies the same-directory immutable selected snapshot and returns Unchanged. This is **logical historical recovery**, not v1's additional physical backup copy and not an independent physical backup. Every record shares one directory and device; losing a segment blocks complete graph validation; corrupt/lost state currently blocks authority recovery even when raw snapshots remain; directory loss, device loss and physical power failure have no independent redundant recovery guarantee. Witness roots contain references, not snapshot copies. External export remains disabled. "All snapshots are retained" must not be presented as physical backup equivalence.
