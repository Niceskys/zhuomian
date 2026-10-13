# E3-2 implementation and evidence

Baseline main: b8bfeff169663fc9958edb701213efb5793d1059 (PR #34 squash merge confirmed). Independent feature/e3-2-workspace-persistence; no E2 branch dependencies or changes. #31/#32/#33 remain open Drafts at 06d6f14520ebc1e7cc6486a4b3cdd90f11d7a678 / 5ea708e0e1b7f760816380dbf56f303d899cc8e6 / 9227f5d0ab6d7924c10cdc3bf61806dbde11262d. ADR-0009 is Proposed.

## Actual scope

[Module/API and disk schema](../src/Zhuomian.Persistence/README.md), [v1 fixture](../tests/Zhuomian.Persistence.Tests/Fixtures/workspace-v1.json) and [ADR/threat review](ADR/0009-workspace-persistence.md). All tests use explicitly owned temporary roots; no default production root or user resources are exercised. Fault seam injects failures before write/flush/replace or after publication; it is not a physical full disk or power-cut experiment. Linux tests use real files but do not establish Windows native guarantees.

## Evidence status

Local cloud: Linux x64, SDK 9.0.318. Current 97 persistence cases passed, zero failures/skips; six Windows-only native assertions take an explicit unsupported-platform branch locally. Full solution restore/Release build with EnableWindowsTargeting=true and complete dotnet format --no-restore --verify-no-changes pass (exit 0, build 0 warnings/errors). Full suite before the last ten negative cases passed 451 Core + 37 Launching.Windows + 85 Persistence; final full suite and Windows references are recorded below after execution. Markdown link check and git diff --check pass. Exact PowerShell docs/performance tools run on Windows CI; the Linux link check is not claimed as those scripts. Existing 451 Core and 37 Launching.Windows cases are retained without changes.

Windows runner must execute original docs/performance tooling/exit propagation/restore/Release/format/all-xUnit gates plus native persistence tests and evidence extraction. TRX and persistence-platform/fault evidence artifacts must be uploaded with strict exit checking. Until a successful real runner result is linked here, Windows atomicity, recovery and ACL gates are pending.

## Residual production gates

- Real Windows 11 user-profile/default root, install/update/rollback and device/cache/filter behavior.
- Physical disk-full and real power-cut/device durability experiments; no absolute power-loss claim.
- Long-term backup/reservation ownership, export/cleanup UI and quotas; no auto deletion is authorized.
- Real released migration fixtures when a second disk format exists; currently only v1 exists.
- Partial/unknown payload recovery editing and safe lossless rewrite; present behavior is read-only refusal.
- E2/E4/UI/desktop integration and E3-1 unavailable user resource launch domains remain separate.

## First Windows run (retained failure evidence)

[Run 38398746420](https://github.com/Niceskys/zhuomian/actions/runs/38398746420), head 01304a4ef63cc98b296693afa75176fe492f84f1: all original docs/performance tooling/exit propagation/restore/Release/format gates passed, build 0 warnings/errors; 451 Core + 37 Launching.Windows passed, persistence 94/95 passed, zero skipped. The sole failure compared pre-application SDDL with Windows' post-application auto-inherited AI flag. Native denial and main-integrity assertions passed. The fixture now compares actual applied permissions before/after the transaction, exact DACL bytes and protection, and restored original DACL/protection. No assertion, original test or failure gate is removed. Added serialization preflight/bounded stream and two deterministic quota tests will also run in the next full Windows run.
