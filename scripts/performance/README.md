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

The Windows sampler launches one fresh directly owned root process per repetition. Defaults follow the current protocol: 60 seconds warm-up, 300 seconds measurement, 1000 ms sampling interval and 3 repetitions. Each `run-XX.csv` contains only:

- UTC timestamp and monotonic elapsed milliseconds;
- processor-count-normalized CPU percentage;
- Private Bytes and Working Set;
- handle count and thread count.

Arguments are passed through `ProcessStartInfo.ArgumentList`; the sampler does not build a shell command string. Immediately after start, the root process is assigned to a private Windows Job Object configured with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. Descendants created after successful assignment inherit normal Job containment unless they explicitly break away. Closing the Job in `finally` therefore cleans already-contained descendants even when the root process has exited early; if the root is still alive, the sampler also requires it to terminate within 5 seconds.

The containment boundary begins only after successful Job assignment. A process created in the narrow start-to-assignment window, an explicit Job-breakaway process, or a packaged/activation handoff to an independent process is not claimed as contained or measured by this generic sampler. Those activation models require future scenario-runner handling.

The returned PowerShell object may contain the resolved output directory and launched root PIDs for the invoking session, but those values are not written into the raw CSV. Durable evidence must still go through `validate-performance-evidence.ps1` and its privacy/path rules.

### Sampler smoke test

```powershell
pwsh ./scripts/performance/test-process-sampler.ps1
```

CI uses two short temporary `pwsh` root runs plus rejection/cleanup cases to verify sampling, UTC formatting, monotonic elapsed time, resource fields, root cleanup and fail-closed behavior. A regression fixture also delays long enough for Job assignment, spawns a long-lived child, lets the root exit early, and requires the contained child to disappear after Job close. The shortened smoke protocol is **tooling evidence only**. It is not a Zhuomian performance run and cannot be used for threshold calibration.

## Per-run process sample summary

```powershell
pwsh ./scripts/performance/summarize-process-samples.ps1 `
  -InputDirectory <directory-containing-run-01.csv...run-NN.csv> `
  -OutputPath <summary.json>
```

The summarizer is an intermediate deterministic tool for the generic sampler format. It requires contiguous `run-01.csv` through `run-NN.csv`, validates the expected CSV header, round-trip UTC timestamps, strictly increasing elapsed time, and finite non-negative numeric samples, then reports **per run**:

- sample count;
- Average;
- P95;
- P99;
- max;

for `cpuPercent`, `privateBytes`, `workingSetBytes`, `handleCount`, and `threadCount`.

P95/P99 use the explicit **nearest-rank** rule: sort ascending and select rank `ceil(p * N)` using a one-based rank. The output uses `summarySchemaVersion: 1` and is intentionally **not** the final performance-evidence schema. It does not choose median/worst runs, populate hardware/build metadata, measure frames/GPU, decide threshold eligibility, or freeze any budget.

### Summary self-test

```powershell
pwsh ./scripts/performance/test-process-sample-summarizer.ps1
```

CI uses synthetic temporary CSV fixtures to verify deterministic averages and nearest-rank percentiles plus rejection of run-number gaps, wrong headers, non-monotonic elapsed time and non-finite numeric data. These fixtures are mathematical/tooling evidence only and are deleted after the test.

## Still required for P0-07

- bind the generic sampler to repeatable **Release x64 Zhuomian scenario orchestration** for applicable S1-S8 cases;
- define and implement the cross-run policy that maps per-run summaries into final evidence `metrics[]` plus median/worst run selection;
- assemble complete evidence metadata around validated raw samples and selected runs;
- collect actual raw measurements on at least Baseline and Enhanced real machines;
- compare median/worst runs under the same protocol;
- calibrate and freeze or revise provisional thresholds from reproducible data.

Do not interpret validator, sampler-smoke or summary-self-test success as benchmark success.
