# Performance evidence contract tooling

This directory contains Phase 0 P0-07 protocol tooling. It does **not** contain a production benchmark runner and it does not make any performance claim by itself.

## Validate one evidence file

```powershell
pwsh ./scripts/performance/validate-performance-evidence.ps1 -Path <evidence.json>
```

The validator enforces the machine-readable contract defined by [PERFORMANCE_BUDGET.md](../../docs/PERFORMANCE_BUDGET.md): provenance, environment/build metadata, protocol settings, raw-result references, metric summaries, frame-presentation applicability, run selection and threshold-calibration eligibility.

`machineTier: CI` and `machineTier: Exploratory` evidence may be useful for tooling or diagnostics but cannot set or freeze product thresholds. Only real `Baseline` or `Enhanced` machine evidence may set `eligibleForThresholdCalibration: true`.

Raw-result paths must be relative to the evidence file, must exist, and must not contain path traversal or durable user-home paths.

## Deterministic self-test

```powershell
pwsh ./scripts/performance/test-performance-evidence-validator.ps1
```

The self-test creates temporary fixtures only, verifies one valid contract and several required rejection paths, then deletes all fixtures. CI runs this test to prove the contract validator itself remains executable.

## Still required for P0-07

- a repeatable Release x64 collection runner for applicable Zhuomian scenarios;
- actual raw measurements on at least Baseline and Enhanced real machines;
- median/worst-run reporting under the same protocol;
- threshold calibration from reproducible data.

Do not interpret validator success as benchmark success.