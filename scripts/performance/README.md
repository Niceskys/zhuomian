# Performance protocol tooling

This directory contains Phase 0 P0-07 protocol tooling. It does **not** contain a completed Zhuomian benchmark suite and it does not make any performance claim by itself.

## Validate one evidence file

```powershell
pwsh ./scripts/performance/validate-performance-evidence.ps1 -Path <evidence.json>
```

The validator enforces the machine-readable contract defined by [PERFORMANCE_BUDGET.md](../../docs/PERFORMANCE_BUDGET.md): provenance, environment/build metadata, protocol settings, raw-result references, metric summaries, frame-presentation applicability, run selection and threshold-calibration eligibility.

`machineTier: CI` and `machineTier: Exploratory` evidence may be useful for tooling or diagnostics but cannot set or freeze product thresholds. Only real `Baseline` or `Enhanced` machine evidence may set `eligibleForThresholdCalibration: true`.

Raw-result paths must be relative to the evidence file, must exist, and must not contain path traversal or durable user-home paths.

## Evidence-contract self-test

```powershell
pwsh ./scripts/performance/test-performance-evidence-validator.ps1
```

The self-test creates temporary fixtures only, verifies one valid contract and required rejection paths, then deletes all fixtures. CI runs this test to prove the contract validator itself remains executable.

## Generic process sampler

```powershell
pwsh ./scripts/performance/collect-process-samples.ps1 `
  -ExecutablePath <target.exe> `
  -ArgumentList @(<structured arguments>) `
  -OutputDirectory <empty-output-directory>
```

The sampler launches and owns one fresh child process per repetition. Defaults follow the current protocol: 60 seconds warm-up, 300 seconds measurement, 1000 ms sampling interval and 3 repetitions. Each `run-XX.csv` contains only:

- UTC timestamp and monotonic elapsed milliseconds;
- processor-count-normalized CPU percentage;
- Private Bytes and Working Set;
- handle count and thread count.

Arguments are passed through `ProcessStartInfo.ArgumentList`; the sampler does not build a shell command string. The target must remain as the directly owned process for the measurement. Early exit, a non-empty output directory, invalid samples or cleanup failure abort the run. The sampler terminates its owned process tree after each repetition.

The returned PowerShell object may contain the resolved output directory and launched PIDs for the invoking session, but those values are not written into the raw CSV. Durable evidence must still go through `validate-performance-evidence.ps1` and its privacy/path rules.

### Sampler smoke test

```powershell
pwsh ./scripts/performance/test-process-sampler.ps1
```

CI uses two short temporary `pwsh` child runs plus rejection cases to verify sampling, UTC formatting, monotonic elapsed time, resource fields, cleanup and fail-closed behavior. The shortened smoke protocol is **tooling evidence only**. It is not a Zhuomian performance run and cannot be used for threshold calibration.

## Still required for P0-07

- bind the generic sampler to repeatable **Release x64 Zhuomian scenario orchestration** for applicable S1-S8 cases;
- assemble complete evidence metadata and per-run summaries from the raw samples;
- collect actual raw measurements on at least Baseline and Enhanced real machines;
- compare median/worst runs under the same protocol;
- calibrate and freeze or revise provisional thresholds from reproducible data.

Do not interpret validator or sampler-smoke success as benchmark success.
