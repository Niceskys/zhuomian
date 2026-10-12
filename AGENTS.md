# AGENTS.md

These instructions apply to the entire repository.

## Read before changing code

Read, in order:

1. `docs/PRODUCT_SPEC.md`
2. `docs/INTERACTION_SPEC.md`
3. `docs/ARCHITECTURE.md`
4. `SECURITY.md`
5. the relevant performance, reliability, roadmap and ADR documents

If a request conflicts with them, stop production implementation and identify the conflict. An explicitly authorized specification-only task may reconcile the conflict in a separate reviewable spec PR; it must preserve unresolved safety/platform gates and identify unconfirmed choices. Do not quietly change user-visible behavior because an implementation is difficult.

## Scope and PR size

One PR must address one independently testable and revertible concern. A normal PR should:

- change one primary module or one specification concern;
- avoid combining architecture, feature, cleanup and formatting work;
- state explicit out-of-scope items;
- include tests and evidence in the same PR;
- remain reviewable without reconstructing unrelated changes.

If a change crosses more than two production modules or changes product behavior plus architecture, split it or explain why an ADR and coordinated PR are necessary.

## Specification governance

- Product behavior changes require a specification PR that can be reviewed independently from implementation.
- An implementation author may propose a spec change, but cannot use an unreviewed self-authored spec change as proof that the implementation is compliant.
- Desktop Hosting, persistence, packaging, update, discovery, plugin or media-pipeline changes require an ADR.
- Spike results are evidence, not production code.

## Required evidence

- State changes: deterministic external-behavior tests.
- UI changes: real screenshots or recordings, including Reduced Motion where relevant.
- Performance changes: same-protocol before/after data.
- Data changes: migration fixtures, failure rollback and compatibility tests.
- Security-boundary changes: threat review and negative tests.
- Platform changes: Windows build, display/DPI and packaging details.

## Prohibited shortcuts

- Do not delete, skip or weaken tests to make CI pass.
- Do not introduce continuous render loops in idle states.
- Do not make WorkerW, real Blur or one packaging mode an implicit dependency.
- Do not turn `ApplicationItem` into an unrestricted command runner.
- Do not persist runtime interaction state.
- Do not expose secrets or private paths in durable artifacts.

## Definition of done

Follow `docs/DEVELOPMENT_PLAN.md`, the PR template and all applicable quality gates. Documentation and implementation must agree before merge.

## AI development and release scope

- AI owns implementation, documentation, automated tests and diagnosis; the user owns product decisions and Windows acceptance, not handwritten code.
- Use the E1–E5 incremental roadmap. Domain-only work can proceed without accepting a platform adapter. Controlled engineering packages gather evidence; they are not usable internal releases.
- Per-Space Hover/Click means A in-place expansion / B centered expansion. Preview click races, drag suppression, safe return and theme inheritance require the updated specification tests before claiming completion.
- Do not claim B0 continuous animation when only B1/B2 is available; record actual degradation, cause and evidence.
- Windows 11 is first priority. Real display/DPI, Win+D, icon coexistence and session evidence must match support claims.
- Free and future fully open source are product goals. Do not claim an existing open-source license or vendor/library compatibility without the release license/NOTICE audit.
- Dock/video are optional follow-up. Existing CI, tests and Proposed ADRs remain; document changes do not close their gates.
