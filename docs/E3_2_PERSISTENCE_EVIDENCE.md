# E3-2 implementation and evidence

Baseline main: b8bfeff169663fc9958edb701213efb5793d1059 (PR #34 squash merge confirmed). Independent feature/e3-2-workspace-persistence; no E2 branch dependencies or changes. #31/#32/#33 remain open Drafts at 06d6f14520ebc1e7cc6486a4b3cdd90f11d7a678 / 5ea708e0e1b7f760816380dbf56f303d899cc8e6 / 9227f5d0ab6d7924c10cdc3bf61806dbde11262d. ADR-0009 is Proposed.

## Actual scope

[Module/API and disk schema](../src/Zhuomian.Persistence/README.md), [v1 fixture](../tests/Zhuomian.Persistence.Tests/Fixtures/workspace-v1.json) and [ADR/threat review](ADR/0009-workspace-persistence.md). All tests use explicitly owned temporary roots; no default production root or user resources are exercised. Fault seam injects failures before write/flush/replace or after publication; it is not a physical full disk or power-cut experiment. Linux tests use real files but do not establish Windows native guarantees.

## Evidence status

Local cloud: Debian 13 x64, SDK 9.0.318. Final full xUnit: **451 Core + 37 Launching.Windows + 103 Persistence = 591**, zero failures/skips. Full solution restore/Release build (EnableWindowsTargeting=true) and complete format verification pass, exit 0 and build zero warnings/errors. Seven Windows-only native assertions use explicit unsupported-platform branches locally; Linux counts do not certify those native operations. Local Markdown link check and git diff --check pass.

Real Windows gates passed in [run 38404265282](https://github.com/Niceskys/zhuomian/actions/runs/38404265282), documented below. All original PowerShell documentation, performance tooling, exit-code propagation, restore/Release/complete format/all xUnit gates are retained. The collector additionally validates exact original 451/37 and new 103 test counts, zero skipped/failed, NTFS and required native assertions. No existing source/test assertion is deleted, skipped or modified.


## Residual production gates

- Real Windows 11 user-profile/default root, install/update/rollback and device/cache/filter behavior.
- Physical disk-full and real power-cut/device durability experiments; no absolute power-loss claim.
- Long-term backup/reservation ownership, export/cleanup UI and quotas; no auto deletion is authorized.
- Real released migration fixtures when a second disk format exists; currently only v1 exists.
- Partial/unknown payload recovery editing and safe lossless rewrite; present behavior is read-only refusal.
- E2/E4/UI/desktop integration and E3-1 unavailable user resource launch domains remain separate.

## First Windows run (retained failure evidence)

[Run 38398746420](https://github.com/Niceskys/zhuomian/actions/runs/38398746420), head 01304a4ef63cc98b296693afa75176fe492f84f1: all original docs/performance tooling/exit propagation/restore/Release/format gates passed, build 0 warnings/errors; 451 Core + 37 Launching.Windows passed, persistence 94/95 passed, zero skipped. The sole failure compared pre-application SDDL with Windows' post-application auto-inherited AI flag. Native denial and main-integrity assertions passed. The fixture now compares actual applied permissions before/after the transaction, exact DACL bytes and protection, and restored original DACL/protection. No assertion, original test or failure gate is removed. Added serialization preflight/bounded stream and two deterministic quota tests will also run in the next full Windows run.

## Successful Windows native run

[Run 38399514325](https://github.com/Niceskys/zhuomian/actions/runs/38399514325), head 6d9a35116738d7715ff044cdb8bfa279e7a7cba0, passed **all original gates** and **451 Core + 37 Launching.Windows + 97 Persistence**, zero failures/skips, build zero warnings/errors. Windows Server 2025 build 10.0.26100.0, SDK 9.0.318, X64, NTFS. Native replacement/recovery, parent pinning, ACL/share denial, links, lock fixture and locked external-resource assertions executed on Windows. Collector reported 97/97 and no power-cut claim. Uploaded [TRX](https://github.com/Niceskys/zhuomian/actions/runs/38399514325/artifacts/11814273035), [persistence platform/fault evidence](https://github.com/Niceskys/zhuomian/actions/runs/38399514325/artifacts/11814692840) and [launch metadata](https://github.com/Niceskys/zhuomian/actions/runs/38399514325/artifacts/11814662885); API and job logs verified. Artifact download returned HTTP 403; no local inspection of downloaded Windows TRX/JSON is claimed.

A final root guard additionally inspects dangling directory reparse entries before creating any directory, with an explicit no-external-target-creation test. The evidence collector now enforces exact existing suite counts (451/37) and 98 persistence cases, zero skipped/failed. Its successful final Windows run is recorded below.

## Historical 98-case Windows gate

[Run 38400180664](https://github.com/Niceskys/zhuomian/actions/runs/38400180664) succeeded 2026-10-13 12:51:55 UTC. Immutable code head **c952d5075597f9447035d8304010cb1e22fe97ef**, PR merge test SHA **a59fb81e55487adf9250af7558dc0fbed2d01fc7**. All original quality gates passed; **451 + 37 + 98 = 586** xUnit, zero failures/skips, Release build zero warnings/errors. Real Windows Server 2025 build 10.0.26100.0, SDK 9.0.318, X64, NTFS. The collector strictly verified all three complete suites and required disk/atomic/restore/reparse/ACL/cross-process/external-source checks.

Uploaded [TRX](https://github.com/Niceskys/zhuomian/actions/runs/38400180664/artifacts/11814903752), [persistence platform and per-test fault outcomes](https://github.com/Niceskys/zhuomian/actions/runs/38400180664/artifacts/11814594090), [retained launch metadata](https://github.com/Niceskys/zhuomian/actions/runs/38400180664/artifacts/11814928925). Successful job steps, test counts and artifact identities/sizes were inspected through API/logs. The earlier download attempt returned 403, so evidence does not assert local Windows artifact-content inspection. Production never uploads configuration evidence; these artifacts contain generated fixture results/environment only.

Final source audit: all pre-existing Core/Launching C# and original test sources unchanged; three E2 solution snapshots compared, with no added project/folder name, path or GUID collision. Main remains the requested PR #34 squash baseline; ADR-0009 allocation rechecked against main. #31–#33 remain independent open Drafts. The final documentation-only commit records this run; its own full CI is linked in Draft PR #35 after verification. No PR merge or ADR acceptance is performed.

Passed data gates: v1 full round-trip, shared/order/removed records, unknown-data read-only preservation, strict parsing/count/string/byte limits, stale/conflicting/duplicate revision policy, real same-volume Replace/initial Move, pre-flush/pre-replace rollback and post-replace verification, valid backup selection/corruption evidence, conservative recovered revision high-water mark, cross-process exclusion, filesystem alias/ACL refusal and source integrity. Still open: Windows 11/default-profile/device/installation acceptance, real physical full disk/power cut, long-term retention/export/cleanup ownership, future real release migrations and lossless recovery editing. No full-layout, power-loss-level, user-launch or usable desktop-product acceptance claim.

## Post-submission ambiguity protection

A further contract review found that post-replacement read denial cannot justify reporting NotCommitted. StorageResult now exposes CommitState.Unknown when OS submission returned an error and readback is blocked. An owned Windows fixture holds the new main exclusively after Replace, verifies Unknown, releases the obstruction, then verifies the saved revision and an identical Unchanged retry. Local persistence count is 103 with zero failures/skips; the complete Windows gate passed below. Malformed single-Space itemIds type is also isolated with raw evidence; global parse budgets and root collection validation still reject whole documents. This is additional outcome accuracy, not a relaxation of existing atomic/recovery assertions.

Known format/domain version headers are now checked before applying v1 field semantics: a future schema containing a field v1 forbids must remain UnsupportedVersion, not be misclassified as recoverable v1 corruption and downgraded by Restore. Both disk and domain version cases preserve exact source bytes in negative tests. File-byte, JSON-depth and duplicate/header structural budgets still apply before any returned model.

## Final implementation gate: 103 new tests

[Run 38404265282](https://github.com/Niceskys/zhuomian/actions/runs/38404265282) passed at 2026-10-13 13:24:07 UTC. Immutable implementation head **ddebd6eee8a5a5b15af79147f75bd1373a0c0ac9**, PR merge test SHA **d44524548a5e22c394990f058d8ea5a7d50b3e5a**. **All original docs/performance tooling/native-exit-propagation/restore/Release/complete-format gates passed; 451 Core + 37 Launching.Windows + 103 Persistence = 591**, zero failed/skipped, build zero warnings/errors. Environment remains Windows Server 2025 build 10.0.26100.0, SDK 9.0.318, X64, NTFS.

The final tests include real post-Replace read obstruction and Unknown commit recovery, isolated malformed Space reference arrays, future disk/domain header protection before v1 semantics, and refusal of invalid UTF-16 paths before JSON can silently substitute characters. All native assertions execute on the actual Windows runner. The collector requires exact original/new counts and validates the named native cases from real TRX.

Uploaded [final implementation TRX](https://github.com/Niceskys/zhuomian/actions/runs/38404265282/artifacts/11816881972), [persistence environment/fault outcomes](https://github.com/Niceskys/zhuomian/actions/runs/38404265282/artifacts/11817056971), [launch metadata](https://github.com/Niceskys/zhuomian/actions/runs/38404265282/artifacts/11816996891). Job logs/API verified all steps, counts, metadata and artifact identities. Artifact payload downloads remain unavailable in this environment (earlier 403), so no local Windows payload-inspection claim is made. Final PR head/full CI is linked in Draft PR #35 after the documentation-only evidence commit.

Main rechecked at b8bfeff169663fc9958edb701213efb5793d1059; original C# and test bodies remain unchanged. No E2 branch modification/dependency/merge; no E3-1 permission expansion; no user-source resource access or deletion. The private evidence store has no automatic cleanup, and failed temporary files can accumulate: aggregate disk retention/export/ownership handling remains a daily-use integration gate rather than an implied garbage-collection permission. Seven native cases take unsupported-platform assertions on Linux; local 591 passes are not a substitute for Windows evidence. Windows 11, physical disk-full/power-cut/device guarantees, real old-release migrations and lossless unknown-data editing remain open exactly as listed above.
