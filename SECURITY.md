# Security Policy and Design Baseline

## Supported stage

Zhuomian is currently in specification and feasibility work. There is no production release. Report security concerns through GitHub Security Advisories when available; do not include private paths, tokens, personal filenames, or exploit payloads in public issues.

## Security invariants

- Configuration is data, not executable code.
- `ApplicationItem` starts one identified application; it is not a generic command line.
- A normal add-item UI does not expose arbitrary shell, PowerShell, script host, DLL loader, or unrestricted arguments.
- Executables use an absolute validated path and structured argument APIs; no string concatenation or `PATH` lookup.
- Default URL allowlist is only `https` and `http`. Other schemes require an explicit capability decision and user confirmation.
- Imported configuration, shortcuts, network paths, removable media and custom assets are untrusted.
- Zhuomian never elevates an Item silently and does not bypass Windows security prompts.
- Logs and crash reports redact personal paths, URL query/fragment data and secrets by default.
- No application list, filename, path, wallpaper or usage telemetry is uploaded by default.

## Launch domains

### ApplicationItem

Created by a trusted discovery provider or explicit file selection. Provider provenance and executable identity are retained. Arguments, if ever required for provider parity, are structured, source-labelled and not user-editable by default.

### FileItem and FolderItem

Opened through a reviewed Windows association path. Missing, replaced, symbolic-link/reparse and network targets are handled as explicit states. Zhuomian does not interpret file content as commands.

### UrlItem

The normalized scheme is checked before launch. Unicode/display text must not hide the actual destination. Custom protocols remain disabled until separately specified.

### Future CommandItem

Out of scope for V0.1/V0.2. It requires a separate threat model, UI warning, permission model, import rules and audit trail.

## Import and persistence

- Imported schemas are size-limited and validated before migration.
- Unknown types are preserved but not executed.
- Invalid objects are quarantined; root validation failure falls back to last-known-good.
- Managed assets have file type, size, pixel/duration and decoder resource limits.
- Archive/path extraction must reject traversal and links escaping the managed asset root.

## Media

Image, icon and video decoding occurs through constrained platform codecs where possible. Decoders must handle malformed input, decompression bombs, huge dimensions and resource exhaustion. Preview generation never executes embedded content.

## Updates

Before automatic updates exist, the design must define:

- HTTPS and fixed trusted origin;
- package/publisher signature verification;
- version and anti-downgrade checks;
- atomic install and rollback;
- explicit failure reporting;
- no execution of unverified downloads.

## Security gates

Real Item launch is blocked until launch-domain tests pass. Configuration import is blocked until migration and malicious-input tests pass. Automatic update work is blocked until its threat model and signature path are accepted by ADR.
