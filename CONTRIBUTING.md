# Contributing to Zhuomian

## Current project stage

The project is in Phase 0 specification and feasibility work. Independent domain work may proceed through E1; controlled engineering packages can validate E2 integration. A usable internal release and each enabled platform capability remain blocked until their applicable gates pass. See the [roadmap](docs/ROADMAP.md).

## Workflow

1. Open or select one scoped issue/decision.
2. Read `AGENTS.md` and the relevant specifications.
3. Create `docs/`, `spike/`, `feature/`, `fix/`, or `test/` branch.
4. Keep the change independently testable and revertible.
5. Run `pwsh ./scripts/validate-docs.ps1` plus all relevant project tests.
6. Open a PR using the template; attach evidence rather than only conclusions.
7. Merge only after required checks pass.

## Architecture decisions

Copy `docs/ADR/TEMPLATE.md`. ADR status progresses through Proposed, Accepted, Superseded or Rejected. Accepted decisions are immutable; later changes create a new ADR that references and supersedes the old one.

## Commit style

Use clear conventional prefixes where practical: `docs:`, `feat:`, `fix:`, `test:`, `perf:`, `refactor:`, `build:` and `ci:`.

## Branch protection

Repository administrators should configure a `main` ruleset with:

- pull requests required;
- `Documentation validation` and future build/test checks required;
- force pushes and branch deletion blocked;
- conversation resolution required;
- administrator bypass limited to emergency recovery.

This repository file cannot prove those GitHub settings are enabled; verify them in repository settings.

AI executes development, documentation, automated checks and diagnosis; the user decides requirements and performs Windows acceptance. Real-resource internal builds must precede formal public distribution. License selection and third-party compatibility remain public-release gates; no LICENSE is currently present.
