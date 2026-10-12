# Zhuomian Diagnostic Contract

> Phase 0A baseline. This defines observability fields, not a logging implementation.

## Event envelope

Every structured diagnostic event should provide:

- `timestampUtc`
- `eventName`
- `category`
- `severity`
- `component`
- `correlationId`
- `outcome`
- `reasonCode`
- optional `durationMs`
- allowlisted structured `fields`

Initial categories are `DesktopHost`, `Interaction`, `Rendering`, `Media`, `Persistence`, `Discovery`, `Launch`, `Performance` and `Lifecycle`.

## Stable reason codes

Human messages may change or be localized. Tests, telemetry exports and support procedures must use stable reason codes such as `host_lost`, `stale_event_discarded`, `focus_denied_external_foreground`, `material_degraded` and `migration_rollback`.

## Privacy rules

- Do not log tokens, environment dumps, command lines or arbitrary configuration payloads.
- Absolute personal paths are redacted or represented by a non-reversible diagnostic identifier.
- URL query strings and fragments are omitted.
- Item names and filenames are omitted by default.
- Exception data is filtered before export.
- Diagnostic export is explicit and local by default.

## Phase 0 evidence

Each Spike records environment, version, selected strategy, transitions, failure reason, timing and fallback outcome. It must not rely on screenshots alone.

## Release behavior

Debug overlays and verbose traces are disabled by default in Release builds. A future safe mode must be able to disable video, enhanced backdrop and animation without requiring configuration file edits.

## Expansion and configuration diagnostics

Future production events must report the A/B mode, actual B animation tier (B0/B1/B2), Reduced Motion selection, fallback reason, interrupted generation and material/asset fallback without serializing private configuration. Candidate stable reasons: `animation_degraded`, `preview_click_consumed`, `drop_rejected`, `layout_restore_failed`. These are specification fields, not an existing logging implementation.

Safe mode for the core internal build must disable costly material/animation and invalid managed assets while preserving resource access, persistence and source-file safety; video controls apply only when video exists.
