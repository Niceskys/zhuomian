# ADR-0009: Workspace persistence, revision reservations and preserved recovery evidence

- Status: Proposed
- Date: 2026-10-13
- Owners: Niceskys (acceptance), AI implementation (code and evidence)
- Supersedes: none
- Superseded by: none

## Context

Main b8bfeff169663fc9958edb701213efb5793d1059 contains E1 immutable Workspace domain schema 1 and E3-1 safe launch. PRs #31–#33 remain independent E2 Drafts. No released disk configuration history exists. A persistence module must preserve IDs, roots, ordered references, RemovedItems/RemovedSpaces, provenance, availability and asset references without adding launch authority or fictional UI configuration.

## Decision drivers

Atomic save, stale-write refusal across processes, recoverable failure, bounded untrusted parsing, no silent empty reset, retained evidence, no source-resource access and independently revertible scope. Real Windows native validation is required; Linux/in-memory results cannot certify Windows replacement.

## Options considered

Direct WriteAllText, a process-only lock, overwriting a single backup during corrupt recovery, deserializing arbitrary runtime types and dropping unknown payloads are rejected. A database/journal plus UI recovery manager is beyond this independently testable concern. The selected candidate is explicit v1 DTOs and a synchronous NTFS file transaction, with conservative immutable revision reservations and recovery assets.

## Decision

Implement one independent Zhuomian.Persistence production module plus its tests and owned lock fixture. ADR stays Proposed; no production integration, UI or launch route is enabled.

### Schema and migration

Disk formatVersion starts at 1, distinct from Workspace.CurrentSchemaVersion (currently 1). DTOs persist only current E1 fields; placement/appearance/globalSettings remain future boundaries. SHA256 covers exact payload bytes and is corruption detection, not a signature. Duplicate JSON properties/IDs and unsafe root structure reject; invalid objects, unknown kinds and dangling references isolate with private raw evidence. Unknown fields or quarantine prohibit rewrite, retaining exact original disk bytes; future versions return UnsupportedVersion rather than downgrade. Consumers receive unsupported evidence separately from the closed four-kind E1 model. Default migration registry has no historical steps; bounded sequential pure functions and idempotence checks are tested with explicitly synthetic future fixtures. A future real migration needs disk-reader integration, fixtures and explicit promotion; there is no product v0 migration.

### Root, paths and transaction

Default is current-user LocalApplicationData/Zhuomian/Workspace, fixed NTFS only. No disk-configurable root. Owned test roots and faults are internal test composition. Names: workspace.json, workspace.lock, random temp-GUID.json, backup-GUID.json, prepared-GUID.json, evidence-GUID.json. No user resource names are used. Windows non-reparse ancestor handles pin directories, deny write/delete sharing, and final-path/kind/single-link checks reject common aliases, junctions, symbolic links and hardlinks. Temp CreateNew prevents truncating an existing name. Cooperating processes exclusively hold workspace.lock; conflict returns Busy without retry or mutation.

Protocol: validate immutable snapshot → archive valid predecessor → flush validated revision reservation → same-directory temp CreateNew/write/Flush(true)/bounded readback → File.Replace or initial non-overwriting Move. Original remains until atomic publication. Restore uses atomic Replace with unique evidence for old main, so failed restore leaves main intact. AfterReplace errors resolve by checking actual committed bytes. Handles are disposed on all outcomes. The first candidate is retained as backup before initial publication because no predecessor exists; an initial failed publish may still yield that valid candidate for explicit recovery.

NTFS/Windows API namespace guarantees do not establish absolute power-loss durability. Directory metadata is not explicitly flushed, .NET/device/cache/filter behavior may vary, and no real power-cut experiment exists. Atomicity evidence applies to the tested runner/API, not all Windows 11 hardware or filesystems. Privileged/kernel attackers or malicious same-account concurrent mutation are not an isolation guarantee; configuration validation and independent E3-1 approval remain necessary even when checksum matches.

### Revision, retention and recovery

A prepared record reserves ID/revision/canonical content before publication. Revision gaps are allowed after failures. Stale content and equal-revision conflicts refuse; identical retry is allowed. Corrupt reservation blocks mutation rather than guessing its high-water mark. Restore advances beyond valid current, backup and reserved revisions. Old sessions become stale. Normal valid main is authoritative; backups are fully verified before selection. Newer schemas never auto-restore older data. Load is read-only recovery and never creates an empty Workspace. Restore is an explicit caller command, without UI in this scope.

Backups are immutable unique valid snapshots, not a rolling overwrite slot. LKG is the highest valid fully understood backup; prepared data is only a conservative revision watermark, not automatic user recovery. Exact old main remains unique evidence, corrupt or valid. Partial/new-format data is protected from Save/Backup/Restore rewrite. No automatic deletion, TTL, GC or ownership inference. Parsing/recovery quotas produce TooLarge backpressure before retention becomes unbounded work. This initial conservative retention needs explicit export/cleanup/ownership policy before daily long-term integration; no permission to delete assets is introduced.

### Threat and privacy review

Untrusted JSON can attack memory/depth/counts, type confusion, duplicate IDs, references, unsafe resource syntax and capability injection; bounded parser/DTO/domain validation plus quarantine/read-only addresses these. Checksums reject accidental modifications, not authenticated adversarial edits. Paths inside Items are data only and never opened; provenance/Available cannot authorize application execution. Persistence neither references Windows Launching nor calls a launcher. File/Folder/URL real dispatch remains disabled and application approval catalog remains empty in E3-1.

Storage namespace attacks include reparse ancestors, linked leaves, aliases, temp collisions, sharing and ACL denial. Native tests exercise the owned root without modifying outside targets. Cross-process handle locking is tested with a repository-owned child. Logs expose only StorageOperation/StorageError enums, never exception messages, item names, URLs, roots or raw payloads. Recovery payloads stay local/private; no networking, uploads or telemetry is added to production.

## Consequences

A usable persistence API and genuine disk transactions exist independently of E2/E4. A partially recovered view remains readable but cannot be lossily saved. Recovery assets consume disk; quota exhaustion is an explicit refusal, not cleanup. There is no full UI-layout persistence, import UI, recovery/export UI, launched user app or accepted production deployment in this change.

## Validation and rollback

[Evidence and command results](../E3_2_PERSISTENCE_EVIDENCE.md) track all existing gates, added xUnit cases, Windows native operations, failures, TRX and platform artifacts. Current v1 fixture round-trips; migration harness does not claim shipped older versions. Existing 488 tests remain unchanged. Windows 11 real-user profile/installation/storage-device/power-loss acceptance and future migration release fixtures remain open. Cloud CI does not close them. This ADR must not be promoted automatically.

Revert the PR/module/solution/CI additions independently. Leave existing owned configuration/backup/evidence files intact; earlier main cannot read the new format and must not initialize over it. A future application integration must disable storage rather than silently rewrite. To recover this format, use this module's explicit Restore and export preserved evidence when a reviewed UI exists. Never roll back by deleting user configuration or resource assets.

## References

- [Architecture](../ARCHITECTURE.md)
- [Security](../../SECURITY.md)
- [Reliability](../RELIABILITY_TEST_PLAN.md)
- [Module contract](../../src/Zhuomian.Persistence/README.md)
- [FileStream.Flush](https://learn.microsoft.com/dotnet/api/system.io.filestream.flush)
- [File.Replace](https://learn.microsoft.com/dotnet/api/system.io.file.replace)
- [ReplaceFileW](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-replacefilew)
- [CreateFileW](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew)
