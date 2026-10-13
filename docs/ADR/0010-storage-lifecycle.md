# ADR-0010: Conservative storage inventory, retention and export planning

- Status: Proposed
- Date: 2026-10-13
- Owners: Niceskys (acceptance), AI implementation (code and automated evidence)
- Supersedes: none
- Superseded by: none

## Context

Baseline main 6687bc39d060029e8f0a692419694ffa3c5a7a29 includes squash-merged PR #35. [ADR-0009](0009-workspace-persistence.md) is Proposed and its existing invariants remain unchanged. E3-2 stores immutable backup/prepared snapshots, failed temporary files and exact replaced-main evidence. Its recovery scan limit is 256 files and 64 MiB per scanned collection. File age, name or highest revision alone cannot prove safe deletion. This incremental engineering API does not add product UI or enable the deferred product import/export feature.

ADR-0010 allocation was checked against all fetched remote branches, including read-only trees of #31–#33. None used this number. Those Drafts remain independent and unchanged.

## Decision drivers

Inventory without private data disclosure; bounded untrusted reads; exact recovery preservation; conflict and rollback prevention; explicit, fresh authorization before any future destructive transaction; real NTFS evidence; no speculative migration, database, new disk format or background cleanup.

## Options considered

Age/TTL deletion, deleting all but the newest backup, dropping prepared snapshots below a maximum integer and treating GUID filenames as proof of ownership are rejected. A transactional compacted index could eventually permit reservation compression, but requires a separate disk protocol and crash proof. Implementing it here would redesign E3-2 beyond the requested boundary. Selected: a usable conservative inventory/health/plan/revalidation API, with Apply and external export writing unconditionally disabled.

## Decision

### Classification, ownership and privacy

Only workspace.json, workspace.lock and exact lower-case backup/prepared/temp/evidence-GUID(N).json spellings are classified. Classification is not authenticated ownership. GUID names and valid checksums can be forged by the same account. Unknown names, directories, links, inaccessible entries, malformed documents, future schemas and partially quarantined/read-only snapshots block planning and remain untouched. No resource paths in Items, external icons or managed assets are accessed. No source files/folders/apps/images/assets are deleted.

Inventory returns random per-scan entry IDs, kind, byte length where safely available, validation/error/risk/reason enums and revisions. No filenames, paths, Workspace IDs, Item names, URLs, JSON or content fingerprints are exposed in diagnostics. Internally, plans bind the file namespace, NTFS volume/file ID/write stamp, length and exact-byte SHA256; influencing snapshots are also canonically compared by Workspace ID and revision. Fingerprints stay private. Export source SHA256 is an explicit local-review manifest, not telemetry or ordinary diagnostics.

The existing root/ancestor pinning and exclusive workspace.lock serialize cooperating processes across Save, Restore and Inventory. Inventory never creates missing directories; on an existing owned root, it may create the coordination lock with OpenOrCreate, as E3-2 does. It never deletes, moves, overwrites or repairs data. Raw Windows root segments ending with dot/space are refused before Path.GetFullPath can erase alias spelling. Native read handles are held for the entire scan and deny write/delete sharing. Entry names are re-enumerated before handles are released. Non-cooperating creation makes the inventory stale; failed reads leave incomplete diagnostics, not permission to clean.

### Bounds and health

Enumeration stops after 1026 observed entries (4 x 256 plus main/lock), with one lookahead. Excess entries produce TooLarge and explicitly incomplete totals. Content reads have a conservative **aggregate** 64 MiB budget across all inventory families, plus the unchanged 8 MiB per-file parser budget. This is stricter than E3-2's individual collection scans. Per-family quotas report observed counts/bytes, remaining collection budget and Normal/ApproachingLimit/AtLimit/Exceeded/Unknown pressure. Unknown or inaccessible totals are never represented as complete; an unreadable family has Unknown pressure and zero proved remaining byte budget. Unknown names block proved remaining budgets for all families, because E3-2 wildcard scans can count non-GUID names that inventory cannot safely attribute. Metadata sizes of safely opened over-budget files are available without reading their contents. The health result separately gives the remaining inventory read budget, main validation, fully validated backup count, distinct revision generations, observed revision range and prepared high-water revision.

The high-water integer is a **diagnostic only**. It is never consumed to replace reservation evidence or authorize Save/Restore. Health marks recovery selection proven only for a complete, fully understood, conflict-free inventory with a validated backup. Missing main prevents a reviewable retention plan; an export source plan can still describe a valid backup when main is absent and all remaining history is fully understood. Quota warnings are conservative and do not reserve room or guarantee a subsequent Save will succeed. No timers or automatic maintenance run in Load/Save.

### Retention invariants and recovery generations

Every entry is retained. Valid main remains authoritative. All fully verified backup generations stay available, including the last-known-good and older distinct revisions; two revisions are reported as two logical recovery generations, not independent devices or physical failure domains. Duplicate snapshots also remain: byte equality does not prove that an external actor owns neither copy or that an independently recoverable asset may be removed.

Prepared snapshots remain immutable, preserving complete Workspace/revision/content conflict evidence. Same-revision differing content or foreign Workspace histories block planning. Temp is unfinished-operation evidence, including valid snapshots; native sharing violations explicitly report InUse/Busy for temp; other unreadable temp remains conservatively incomplete. Valid evidence is still unconfirmed evidence, and corrupt evidence may be the only exact damaged source. No confirmation/deletion UI exists in this scope. There are **no eligible eviction candidates** under this implementation. Reclaim estimate is exactly zero.

A future backup eviction policy must prove retention of valid main, last fully validated recovery snapshot and any required independent older generations; preserve historical user recovery rather than merely pass a quota test; prove ownership, dependencies and identity under mutation; and keep unknown/partial/corrupt/unreadable/unconfirmed assets. User-approved history loss must be separately specified, never inferred from a retention plan.

### Reservation compression conditions

A scalar maximum revision is insufficient. A separately reviewed compacted protocol must durably bind Workspace identity, covered reservation set, canonical content commitments for equal-revision conflict decisions, generation/coverage/completeness, integrity checks and format/version protections. It must atomically publish verified replacement evidence before retiring any covered source; readers must reject mixed generations, truncation, malicious links, missing coverage or conflict. It must define identical retries, gaps, restore revision advancement, stale clients and compatibility with old readers. Crash/reorder/repeat tests at every publication and retirement stage, rollback and native identity tests are prerequisites. No such format or migration is introduced here.

### Plan authorization, freshness and rollback

DryRun produces immutable in-process review artifacts containing each retention reason and zero benefit. Incomplete, unsafe, conflicting or recovery-deficient inventories are refused. ValidatePlan and ValidateExport reacquire the existing mutex/root boundary and re-scan all relevant state; changed namespace, bytes, NTFS file identity, length, revision or recovery capability refuses the plan. Same bytes under a replaced NTFS file ID invalidate it. Store-instance ownership prevents cross-store or restart replay. These ephemeral plans are intentionally not durable serialized approvals.

Apply(plan, explicitlyAuthorized) and Export(plan, explicitlyAuthorized) return AuthorizationRequired when false and MaintenanceDisabled when true. They perform **no filesystem operations**. Approval is necessary but cannot enable an unproven implementation. Validation is read-only and provides no lease spanning the return to a future mutation; any future Apply must combine final validation and mutation in one locked transaction, define a reversible commit and crash recovery, and test interruption before/during/after each stage. There is no deletion transaction here, so cleanup crash/rollback stages are inapplicable. Exact before/after asset tests and a killed lock-owner/restart test establish the actual no-mutation behavior. Reverting this PR leaves all v1 data intact and E3-2 readers unchanged.

### Backup export boundary

PlanBackupExport fully validates a conflict-free bounded inventory, chooses the highest verified backup revision and returns an opaque source ID, exact SHA256, disk v1 and domain schema 1. It copies/moves nothing. ValidateExport binds the entire inventory, not just a remembered filename. No destination path is accepted from config or a caller, no external filesystem writing, commands, cloud upload, network synchronization or sharing exist.

External writes require a separate caller authorization and trusted user path-selection contract: pin and validate destination ancestors and leaves against reparse/junction/symlink/hardlink/alias/replacement attacks; reject existing names; use exclusive CreateNew staging, bounded copy/flush/readback/hash and no overwrite; handle permission denial, full disk and partial writes without altering original recovery assets; define residual temp evidence and rollback. Until that contract and native failure tests exist, destination permission/name/partial-write operations are **unsupported**, not claimed tested export behavior.

### NTFS and residual risks

Native namespace atomic replacement from E3-2 is unchanged. Flush(true), File.Replace and directory pins do not prove physical power-cut durability or device/cache/filter guarantees. No real power-cut or physical disk-full experiment is claimed. Faults use generated owned temp roots and simulated HRESULTs. CI on Windows Server is not Windows 11 user-profile/device/product acceptance.

Same-account malicious or privileged/kernel concurrent mutation is not an isolation guarantee. Scan holds deny-sharing handles for opened leaves but cannot atomically freeze all potential future names. The final name check, cooperating mutex and plan revalidation reduce races; Apply remains disabled. Linux tests do not prove hardlink identity or NTFS semantics. Inventory may refuse earlier than Save due to its aggregate budget. Backup/prepared growth still reaches E3-2 quotas: this PR makes pressure inspectable and maintenance refusal explicit, **does not solve long-term storage exhaustion**. Daily-use integration remains blocked on safe lifecycle/ownership policy, tested compaction if chosen, reviewed export destination semantics and user acceptance.

## Validation and rollback

[Evidence/support matrix](../E3_3_STORAGE_LIFECYCLE_EVIDENCE.md) records original 591 tests, additional 46 tests, full restore/Release/format/xUnit/documentation/performance-tool/exit-code gates, NTFS runner, TRX and enum-only invariant outcomes. It distinguishes actual failures from passed reruns. No original test is reduced or skipped and E3-1 authority is unchanged. ADR stays Proposed.

Rollback: revert only this lifecycle/API/native capture/evidence addition. Keep all existing configuration, backups, reservations, temps and evidence untouched; no disk format changes or schema migrations need undoing. E3-2 Save/Load/Backup/Restore remain operational exactly as before. UI/E2/E4/default-root/Windows 11/installation/physical device acceptance remain separate gates.
