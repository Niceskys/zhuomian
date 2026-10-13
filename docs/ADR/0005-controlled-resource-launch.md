# ADR-0005: Controlled resource launch and evidence boundaries

- Status: Proposed
- Date: 2026-10-13
- Owners: Niceskys (acceptance), AI implementation (evidence)
- Supersedes: none
- Superseded by: none

## Context

E3-1 needs executable launch code independently of the unmerged E2 Draft stack. Main at `d994b0184fa2e097ad34d10eb7f28a1c506c8c7a` has four closed Item types, canonical lexical identities, an immutable root ItemStore, and E1's string-only IInteractionLauncher. Availability, preview visibility, provider strings and path syntax are not authorization. SECURITY.md remains unchanged. This proposal does not accept discovery, UI, packaging or a general Windows association launcher.

## Decision drivers

- One identified app; no command/script/interpreter, arbitrary arguments, PATH search, custom protocol or elevation.
- Recheck latest reference, exact snapshot and input generation before OS submission.
- Reject ambiguity rather than use unreviewed ShellExecute fallback.
- Deterministic tests and genuine Windows filesystem/CreateProcess evidence must be distinguished.
- Do not modify, merge or depend on Draft PRs #31–#33.

## Options considered

Generic Process.Start/ShellExecute on Item strings would bypass the launch domains and is rejected. A complete discovery/association broker would cross the present scope and requires its own evidence. The implemented option is a small explicit approval catalog, resource leases, a one-shot launch service and a gated Windows executor; unreviewed launch domains remain disabled.

## Decision

This ADR remains Proposed. Two production modules are necessary for one revertible launch boundary: Core/Launching owns admission and lifecycle; Platform.Windows/Launching owns real filesystem handles and execution. Existing WorkspaceModel and Interaction code are unchanged. Tests and a no-I/O executable fixture accompany these modules.

### Application identity and resolver trust

ApplicationIdentity is an opaque dictionary key, not an executable path, command, authority or credential. WindowsLaunchResolver accepts only exact identity plus exact ApplicationProvenance from an immutable-in-practice code-composed ApprovedApplication catalog. Unknown providers and mismatched sources return NoTrustedApplication. It does not instantiate providers using Item strings. The default catalog is empty.

An approval binds an absolute `.exe` path and a reviewed deployment SHA256; there is no argument, shell verb or custom working-directory input. Known interpreter/loader/installer host names are additionally denied. A hash matching a file does **not** independently prove that an app is safe: catalog entries and their complete deployment/dependencies must be reviewed by trusted composition, never derived automatically from imported configuration, shortcut output or a click-time hash. A renamed interpreter is not made safe by a new name; it must never receive catalog approval. No user-app approvals ship here. Only the repository-built no-I/O fixture is approved inside controlled Windows tests.

Packaged activation, Start Menu discovery, shortcuts, user-selected executable enrollment, publisher validation, dependency manifests and application uninstall/reinstall reconciliation are not implemented. They need a separate E3-1 follow-up before UI can offer those launches. Arbitrary explicit selection alone is not permission.

### File and folder boundary

WindowsPath remains a lexical identity. LaunchPolicy rejects UNC, reserved device names, suspicious Unicode, overlong targets and non-data File extensions. The positive data extension list is `.txt`, `.pdf`, `.png`, `.jpg`, `.jpeg`; this is eligibility for further review, never association trust. Scripts, installers, executables, shortcuts, HTML/SVG and macro-enabled/unknown types are denied. A harmless-looking extension with a dangerous association still cannot execute because no association path is enabled.

WindowsResourceLease checks DriveType.Fixed, opens the drive root, each ancestor and the leaf with OPEN_REPARSE_POINT, rejects every observed reparse point (including junction/mount-point links), validates file/directory kind and rejects multi-link leaves. GetFinalPathNameByHandle must exactly match the requested canonical DOS path, rejecting substitution/alias redirection. Read-only leases deny sharing for write/delete and retain handles through submission. Volume serial/file index and the approved executable hash are rechecked; optional saved File/Folder identity must match. Missing, denied access and sharing violations become MissingOrInaccessible; replacement becomes Denied/ResourceReplaced.

This closes ordinary user-mode rename/write/link races during the synchronous lease; it does not prove immunity to kernel/admin attackers, filesystem/filter-driver anomalies or changes to an app's other dependencies. Ancestor handles temporarily restrict changes by other processes; the service is synchronous, hashing is cancellation-aware and bounded to 128 MiB. No filesystem handle is retained in Workspace or a request. Locks must not remain open across UI awaits.

File/Folder actual launch returns Unsupported/UnreviewedAssociation after inspection. File associations may invoke command hosts, user-modifiable registered commands, DDE/COM delegates or installers. Shortcut resolution and shell namespace parsing are not silently trusted. Asynchronous Shell opening may reopen a path after a lease is released, so lexical validation or a PIDL alone is insufficient TOCTOU evidence. Folder opening needs a reviewed filesystem-only broker/PIDL lifetime and race test; file opening needs reviewed association identity, dispatch and safe failure behavior. Network/removable targets, reparse targets and unknown filesystem safety stay denied. There is no delete/move/write source API.

### URL boundary

Only canonical HTTP(S) without credentials is eligible. Launch-time checks reject hidden controls/format characters, backslashes, nested escape markers and encoded authority delimiters, loopback/IP literals, local/single-label names and trailing-dot ambiguity. Unicode IDN/path cases have deterministic tests. This conservative policy is a launch eligibility rule; HttpUrl's existing identity normalization is reused unchanged.

URL dispatch remains Unsupported/UnreviewedAssociation: default protocol handlers, actual browser identity, redirect behavior and Windows URL delivery have not been reviewed. No raw URL is concatenated into a command or submitted to an OS API here, and tests never access a website. DNS/redirects can change the actual destination; lexical URL validation does not prove a remote endpoint's trust. These limitations cannot be erased by fake executor success.

### Request, cancellation, ownership and feedback

Prepare produces Accepted, not OS success. LaunchRequest has an internal constructor, owner identity, exact Workspace snapshot and Item object, Space binding, context version, InputGeneration, timestamp and atomic consumption. No request cache grows indefinitely. Snapshot changes (including same-revision rehydration), unbinding/deletion, gate changes and generation changes invalidate old requests. Context updates cannot regress revision/generation or change Workspace session. Requests expire under an injected monotonic clock; cancellation is checked before resolution and before OS dispatch. Every submit attempt consumes the request, including failure/cancellation.

The service serializes context updates and synchronous submission. Resolver/lease/executor are trusted code injected at composition, not a security sandbox for hostile in-process plugins. OS callbacks and UI integration must use the single input dispatcher. External foreground already present at down is compatible with explicit Preview clicks; new foreground identity, Suspended, HostLost, Drag, menu/modal and IME must be handled by E1 priority/generation rules. PreviewLaunchAdapter never creates a fresh request from TryLaunch's string ID. It consumes a pinned admission and refreshes controller gates; multiple pending pins are ambiguous and fail closed. Cancel/failed clicks remain Item clicks and never become blank expansion. A replacement controller/session requires a new bridge and launch service. The string-only boundary cannot recover gesture/Space from an ID; an adapter must pin the exact down gesture, cancel retired pins, and refresh snapshots before Drain. This is an integration seam, not an implemented Windows UI input router.

WindowsLaunchExecutor accepts only the private WindowsResourceLease for an Application target. ProcessStartInfo uses an absolute FileName, UseShellExecute=false, zero arguments, and an executable-parent working directory; no PATH, shell command, silent elevation, download or Windows prompt suppression. It owns only its temporary Process wrapper and disposes that handle; it never owns/kills the user's application. Cancellation after dispatch cannot retract a submitted OS operation. SubmittedToOperatingSystem means CreateProcess submission returned successfully, not application-ready/file-open/exit success. Require-elevation and invalid executable failures are reported, never retried using runas.

Feedback is enum-only LaunchResult, with separate Accepted, SubmittedToOperatingSystem, MissingOrInaccessible, NoTrustedApplication, Unsupported, Denied, OperatingSystemFailure, Cancelled, Expired and Duplicate. Exceptions are converted to fixed reason codes; exception text, provider payloads, personal paths, filenames, URL query/fragment and secrets are never attached. No logging/telemetry sink is introduced. Payload-bearing helper types override ToString to return type names; callers must not serialize their raw public destination fields as diagnostics.

## Consequences

Four resource domains have deterministic admission, refusal and failure behavior, but this is not a four-domain usable internal release. Real application execution has a deliberately narrow Windows fixture route; all three association routes remain disabled. The lack of a Windows desktop does not block cloud implementation/testing, and cloud CI does not satisfy desktop acceptance.

## Validation and rollback

Commands, test counts, support matrix and CI references are maintained in [E3-1 evidence](../E3_1_LAUNCH_EVIDENCE.md). Windows CI runs the entire existing suite plus real private-filesystem tests and only the owned zero-argument fixture. There are no tests opening user apps/websites/associations. Linux branches report unsupported-platform behavior, not Windows validation. Windows-specific assertions are compulsory on the Windows runner and are not skipped/retried.

Still open: real user app deployment/publisher/dependency trust; user-selected enrollment; packaged activation; safe association and folder broker; browser/handler dispatch; removable/network device evidence beyond fail-closed policy; actual desktop focus/UAC/SmartScreen/session/IME integration and 100 real Preview clicks; mixed-DPI/UI/packaging acceptance. Default catalogs and all association paths remain disabled until corresponding evidence/review exists. No Proposed ADR is promoted to Accepted here.

Rollback: revert this PR; alternatively inject an empty WindowsLaunchResolver catalog to immediately disable app submissions. Remove only the owned modules/test fixture and solution entries. Existing Items, Workspace, E1 contracts, user resources and future configuration remain intact; there is no persistence migration or source cleanup. No E3-2/E4 work is started.

## References

- [Security baseline](../../SECURITY.md)
- [Interaction contract](../INTERACTION_SPEC.md)
- [Architecture](../ARCHITECTURE.md)
- [Application discovery proposal](0003-application-discovery-reuse.md)
- [Incremental gates](0004-incremental-delivery-gates.md)
- [CreateFileW and sharing/reparse semantics](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew)
- [GetFileInformationByHandle](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getfileinformationbyhandle)
- [GetFinalPathNameByHandleW](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew)
- [ProcessStartInfo.UseShellExecute](https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.useshellexecute)
