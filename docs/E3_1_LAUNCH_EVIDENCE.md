# E3-1 launch implementation and evidence

## Baseline and isolation

Implementation starts at main `d994b0184fa2e097ad34d10eb7f28a1c506c8c7a`, on `feature/e3-1-safe-resource-launch`. At preflight, #31/#32/#33 are OPEN Draft: #31 targets main; #32 targets #31's branch; #33 targets #32's branch. None is merged, modified or used as an implementation dependency; the integration correction below reads only their metadata for collision review. Existing WorkspaceModel, E1 Interaction and SECURITY.md are unchanged. [ADR-0008](ADR/0008-controlled-resource-launch.md) remains Proposed.

## Modules and actual support

| Item | Real usable user launch | Controlled testing | Disabled / unsupported |
|---|---|---|---|
| ApplicationItem | No user-app catalog ships | Exact identity/provenance/deployment digest, native filesystem leases and OS submission of the owned no-I/O fixture on Windows CI | Unknown providers, commands/scripts/arguments, shortcuts, packaged activation, broad discovery, unreviewed explicit selection |
| FileItem | No | Four-domain fake lease/executor tests; live Windows missing/access/replacement/link checks | Every actual file association; unsafe extensions denied |
| FolderItem | No | Fake lease/executor tests; live Windows directory validation | Every actual folder Shell open; network/removable/reparse denied |
| UrlItem | No | Canonical HTTP(S), Unicode/escape and refusal tests using fake dispatch | Every actual browser/handler dispatch; custom protocols/credentials/unsafe targets denied |

`Core/Launching` adds ItemLaunchService, LaunchRequest/Admission/Result, LaunchPolicy, ILaunchIdentityResolver, ILaunchLease, ILaunchExecutor and PreviewLaunchAdapter. `Launching.Windows/Launching` adds ApprovedApplication, WindowsLaunchResolver, WindowsResourceLease (private), NativeFiles (private) and WindowsLaunchExecutor. There is no arbitrary argument or shell-command contract. Only the private pinned Windows lease can reach the real executor. No UI, persistence, full application discovery, renderer/host or packaging change is included.

File/Folder/URL association execution is explicitly split into future E3-1 work requiring its own reviewed Windows dispatch/TOCTOU evidence. These routes are implemented as observable safe refusals, not fake-supported real opens. E3-1 is not a completed four-type user resource loop or V0.1 release.

## Local verification

Cloud environment: Debian 13, Linux x64. `/workspace/.dotnet/dotnet --info` selects SDK 9.0.318; 9.0.300 is also installed. The first install attempt under `/opt` failed with permission denied; installation under the task workspace succeeded. The 9.0.300 build exposed three existing WinForms WFO1000 errors, resolved by the existing global.json latestFeature policy and SDK 9.0.318, without changing Spike code or suppressing warnings. SDK setup has finished; there are no retry loops.

The existing Windows-targeted Spikes need Linux cross-targeting. Reproducible commands:

```sh
dotnet restore -p:EnableWindowsTargeting=true
dotnet build --no-restore --configuration Release -p:EnableWindowsTargeting=true
EnableWindowsTargeting=true dotnet format Zhuomian.sln --no-restore --verify-no-changes
dotnet test --no-build --configuration Release -p:EnableWindowsTargeting=true --logger trx
```

All four commands above completed with exit code 0 on SDK 9.0.318. Release build reported 0 warnings/0 errors. xUnit passed 488 cases: 451 Core (330 unchanged existing + 121 new launch cases) and 37 new platform cases; 0 failed, 0 skipped. `git diff --check` and an equivalent Python local-Markdown-link check also passed (40 Markdown files, 0 broken links). The exact PowerShell documentation/performance gate scripts run on Windows CI, because pwsh is not installed locally. Linux does not execute Windows native paths; platform tests explicitly exercise Unsupported there. No fake result is treated as an OS launch.

## Windows CI and native boundary

The existing Documentation validation workflow runs windows-latest, all documentation/performance tooling gates, dotnet restore, the full Release solution build, full format verification and every xUnit test. The new platform tests are part of the solution and the existing strict TRX upload. Windows-specific branches must assert real native behavior on that runner, without skips/retries or launching user applications/websites. The first [Windows run](https://github.com/Niceskys/zhuomian/actions/runs/38369177582), on `ae60dc2`, passed documentation/performance/CI-failure-propagation, restore, Release build (0 warnings/errors), full format verification and all 451 Core cases. Native fixture submission and resource-denial assertions ran, but the overall run failed: 34/36 platform cases passed; ACL restoration and native junction removal failed in fixture cleanup. The fixes explicitly restore the modified DACL and remove only the owned reparse tag before directory deletion, preserving every safety assertion. The [second Windows run](https://github.com/Niceskys/zhuomian/actions/runs/38369839634), on `4a6068a`, passed all earlier gates, 451 Core and 36/37 platform cases (including MOTW refusal and native junction cleanup). Its one failure was SDDL text equality after the OS added the AI auto-inheritance control flag. The corrected assertion compares exact DACL binary content and ACL protection, plus unchanged/readable source content; no permission or launch assertion is removed. The [successful Windows run](https://github.com/Niceskys/zhuomian/actions/runs/38370746500), on head `1cc05bebfa3e251633b44d55c08d8a03b897ffd4` (PR merge test SHA `6c926eeb4f40456db612b77a963d92a58ea2880b`), completed on 2026-10-13 at 08:41:58 UTC:

| Gate | Observed result |
|---|---|
| Documentation validation | Passed |
| Performance evidence validator/sampler/summarizer/selector/assembler self-tests | All passed; tooling evidence, not a product benchmark |
| .NET CI native-exit failure propagation | Passed |
| Full solution restore | Exit 0 |
| Full Release build | Exit 0; 0 warnings, 0 errors |
| Full dotnet format --verify-no-changes | Exit 0 |
| Core xUnit | 451 passed, 0 failed, 0 skipped |
| Windows adapter xUnit | 37 passed, 0 failed, 0 skipped |
| Strict TRX and platform metadata upload | Both succeeded |

Environment: Microsoft Windows Server 2025 GitHub-hosted `windows-latest`, build `10.0.26100.0`, x64, SDK `9.0.318`, framework-dependent owned test apphost. This is server/CI evidence, not Windows 11 desktop acceptance. [TRX artifact](https://github.com/Niceskys/zhuomian/actions/runs/38370746500/artifacts/11799315440) and [platform metadata artifact](https://github.com/Niceskys/zhuomian/actions/runs/38370746500/artifacts/11799196034) were uploaded. Artifact blob download from this development environment returned HTTP 403; counts/environment/step results above were verified in the complete job log and artifact API listing, not by claiming local TRX inspection.

Total: **488 cases = 330 unchanged existing + 158 new (121 Core launch + 37 Windows adapter)**. Existing test/model source files are byte-for-byte unchanged relative to the main baseline. Native Windows cases cover real fixture CreateProcess through the executor and full service, missing/replaced/read-denied/share-denied resources, held-handle leaf write/delete and ancestor rename refusal, symbolic links, junctions, hard links, exact ACL restoration and MOTW refusal. Canonical-path and final-path checks are executed on real NTFS. Other new cases cover four-domain fake admission/submission/refusal, forbidden executable/script/shortcut/installer extensions, unknown/mismatched application provenance, URL credentials/scheme/Unicode/escape boundaries, one-shot concurrent duplicate submission, expiry/cancellation, same-revision and newer snapshots, unbinding/shared references, controller safety preemption and enum-only failure redaction. No Windows-specific assertion is skipped on this runner.

The fixture is repository-owned, zero-argument and immediately returns 0; it has no UI, external I/O, process spawning, telemetry or resource mutation. Real evidence is precisely successful CreateProcess submission of that fixture through the Windows resolver/executor/service, plus filesystem verification and handle cleanup/refusal tests. It is not evidence of desktop user-app readiness, association file open, browser navigation, focus preservation, UAC/SmartScreen, session boundaries or Windows 11 desktop acceptance. Packaging/DPI/display tests are not applicable to this headless launch module and remain open for UI integration.

## Security review and recovery

Threats and residual risks are detailed in [ADR-0008](ADR/0008-controlled-resource-launch.md). Negative tests cover command/executable/script/shortcut domains, untrusted provenance, unsafe paths/URLs, replaced identities, inaccessible/missing targets, stale snapshots/generations, duplicate/cancelled requests, executor/resolver exceptions, feedback redaction and unchanged Workspace/source resources. File extensions alone never approve associations. Approved executable hashes do not establish dependency/publisher trust, so there are no user-app approvals.

All existing tests remain unchanged. Revert this PR or inject an empty application catalog to disable dispatch; no configuration migration, deletion, move or source write is necessary. Remaining desktop and association gates stay open. No PR is merged and no E3-2/E4 work is included.


## PR #34 integration correction

This follow-up fixes collisions found after the originally verified head `b548c555a794d8972efd1ec00306f41350477a29`. It remains on the same Draft PR and does not merge main or any E2 branch. Production code is now `src/Zhuomian.Launching.Windows/Zhuomian.Launching.Windows.csproj`, namespace `Zhuomian.Launching.Windows`; tests are `tests/Zhuomian.Launching.Windows.Tests/Zhuomian.Launching.Windows.Tests.csproj`, namespace `Zhuomian.Launching.Windows.Tests`. Project references, README links and assembly names follow the move. The security launch proposal is [ADR-0008](ADR/0008-controlled-resource-launch.md), still Proposed; E2's 0005–0007 allocation is reserved without copying its documents into this branch.

| Independent E3 solution project | GUID |
|---|---|
| Zhuomian.Launching.Windows | `6F5FED9D-5948-491B-822D-078163A7FFC0` |
| Zhuomian.Launching.Windows.Tests | `0FD9D24D-C793-4F86-B6DF-2A536C9BAA9E` |
| Zhuomian.LaunchFixture (retained) | `D1B6B17A-E338-4A54-89C2-2460892A0817` |

[Machine-readable isolation review](evidence/e3-1-branch-isolation.json) records the inspected immutable E2 heads: #31 `06d6f14520ebc1e7cc6486a4b3cdd90f11d7a678`, #32 `5ea708e0e1b7f760816380dbf56f303d899cc8e6`, #33 `9227f5d0ab6d7924c10cdc3bf61806dbde11262d`. For each, the added E3 project names/paths/GUIDs and ADR number have no collision. Identical main project/folder records are common ancestry, not duplicate registrations. Each solution also has unique project declarations. The review does not accept or execute any E2 production code.

The existing CI still restores/builds/formats/tests the whole solution, discovering the new project through the solution. Its metadata labels identify the launch project and test project. Moving launch-platform upload before the unchanged test-results upload keeps E2's end-of-workflow publish additions independent. Three-way workflow text reviews against all three E2 snapshots returned exit 0 and retained one full solution test loop, one test-results artifact, one launch-platform artifact and each E2 publish step. No E2 step is copied into or executed by this branch. Future solution/ADR-index/status-document text integration still needs ordinary review; clean metadata and CI text are not proof of accepted E2 runtime integration.

Reproduce the metadata/source audit without checkout or branch merges:

```sh
git fetch origin pull/31/head:refs/review/e3-1/pr31 pull/32/head:refs/review/e3-1/pr32 pull/33/head:refs/review/e3-1/pr33
python3 scripts/review-e3-1-isolation.py
```

The audit compares all 119 C# source files against the previously verified head, allowing only the namespace/using changes required by relocation. Every test body, security assertion, launch policy, SHA256/handle/MOTW check and one-shot permission rule is unchanged; SECURITY.md is byte-identical. No test is removed or skipped. Local SDK 9.0.318 restore, full Release build, format verification and xUnit passed: 451 Core + 37 relocated Windows adapter cases = 488, 0 failed/skipped, 0 build warnings/errors. Linux does not prove native Windows success. The corrected project layout passed [Windows CI run 38373994067](https://github.com/Niceskys/zhuomian/actions/runs/38373994067), head `0f71b14e519121abc6f8897a6cb5c13b5525d6a2`, PR merge test SHA `eb4edb42cffdefa2bf4556a93d995379d48ec210`, completed 2026-10-13 09:09:31 UTC. Full restore, Release build (0 warnings/errors), format verification, all documentation/performance tooling/native-exit-propagation gates and all 488 tests passed, 0 failed/skipped. Actual output reports 451 Core cases and 37 cases in `Zhuomian.Launching.Windows.Tests.dll`. Metadata identifies `Zhuomian.Launching.Windows` and `Zhuomian.Launching.Windows.Tests`; environment is Windows Server 2025 build 10.0.26100.0, SDK 9.0.318, X64. [Renamed-project TRX](https://github.com/Niceskys/zhuomian/actions/runs/38373994067/artifacts/11800521987) and [platform metadata](https://github.com/Niceskys/zhuomian/actions/runs/38373994067/artifacts/11800467376) uploaded successfully. Job logs and the artifact API listing were inspected; this does not claim a Windows 11 desktop test or four-domain user launch support.

Support remains precisely the matrix above: default approval catalog empty, no user application approvals, File/Folder/URL dispatch disabled, and no assertion that E3 is complete. Only owned no-I/O fixture OS submission and private filesystem behavior may be verified on Windows CI. Publisher/dependency enrollment, association/folder/browser dispatch and real desktop focus/UAC/SmartScreen/session/DPI acceptance remain unresolved. Rollback of this correction restores the previous layout only; rollback of the full PR or an empty approval catalog disables launch. Neither rollback modifies user source resources.
