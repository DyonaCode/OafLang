# Oaf Execution Roadmap

This is the active delivery plan after `v0.1` packaging and CLI stabilization.

## Milestone 1: Contract + Parity (In Progress)

Goals:

- Lock a `v0.1` compatibility contract
- Enforce parity across:
  - compilation targets (`bytecode` vs `mlir`)
  - runtimes (VM vs native)

Work items:

- [x] Add compatibility contract document
- [x] Add differential parity tests in self-test suite
- [x] Partition compiler cache by compilation target
- [x] Add CI summary output for parity checks

## Milestone 2: Benchmark Quality (In Progress)

Goals:

- Make benchmark outputs consistently comparable
- Include Oaf VM/native + MLIR VM/native comparisons in default runs

Work items:

- [x] Cross-language script defaults to full Oaf mode matrix
- [x] Add Oaf-only comparison table in benchmark script output
- [x] Include MLIR rows/columns in CI benchmark report
- [x] Add median/p95 aggregation mode for repeated benchmark runs

## Milestone 3: Optimization Work (Completed)

Goals:

- Improve VM throughput and native parity
- Reduce regressions via perf gates

Work items:

- [x] Optimize VM dispatch/constant handling hot paths
- [x] Add copy-prop/dead-store IR passes
- [x] Add benchmark thresholds per algorithm (not just aggregate)

## Milestone 4: SDK Workflow + Releases (Completed)

Goals:

- Improve day-1 UX while keeping the language surface stable

Work items:

- [x] `oaf new` project scaffolding
- [x] `oaf test` workflow for examples/packages
- [x] Release smoke validation matrix improvements
- [x] Reproducibility checks for release artifacts
