# Contributing to Zhuomian

## Current project stage

The project is in Phase 0 specification and feasibility work. Product implementation should not begin until the applicable Phase 0 blockers are closed.

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
