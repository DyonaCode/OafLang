# Compiler and Tooling Guide

## Compilation Pipeline

`CompilerDriver` executes the following stages:

1. Lexing (`Lexer`)
2. Parsing (`Parser`)
3. Type checking (`TypeChecker`)
4. Ownership analysis (`OwnershipAnalyzer`)
5. IR lowering (`IrLowerer`)
6. IR optimization (`ConstantFoldingPass`, `DeadTemporaryEliminationPass`)
7. Bytecode generation (`BytecodeGenerator`)

### Caching

`CompilerDriver` supports source-level compilation result caching (enabled by default) to reduce repeated compile overhead in tooling and tests.

## Running Bytecode

The bytecode VM can execute the generated entry function:

```bash
oaf run ./examples/applications/01_sum_accumulator.oaf --run-bytecode
```

## Tooling Modules

### Package Management

- Manifest format: `name@version` per line in `packages.txt`
- Commands:
  - `--pkg-init`
  - `--pkg-add`
  - `--pkg-remove`
  - `--pkg-install`
  - `--pkg-verify`

- Optional source index file: `packages.sources` (one index path per line; `#` comments allowed)
- Optional env override: `OAF_PACKAGE_INDEX=/absolute/path/to/index.json`
- Source index entries define `name`, `version`, `artifact`, `sha256`
- Source index entries can include transitive `dependencies` as `name@selector` strings
- Artifacts (`.zip`, `.nupkg`, `.oafpkg`) are hash-verified then extracted under `.oaf/packages/<name>/<version>/content`
- Package module files must declare a module that matches their content-relative path (`content/pkg/math.oaf` => `module pkg.math;`)
- Manifest selectors support exact/range syntax (`1.2.3`, `^1.0.0`, `~1.4.0`, `>=1.0.0 <2.0.0`, `1.5.*`)
- Installer resolves transitive dependencies and fails when constraints conflict
- Compile/run/build/publish compose only explicitly imported package modules (plus transitive imports) from the nearest `packages.lock` context

### Documentation Generation

`--gen-docs` can target a file or directory of `.oaf` files and emits markdown docs.

### Formatting

`--format` applies deterministic spacing/indenting rules for the implemented syntax subset.

## Benchmarking

`--benchmark` runs lexer, compiler-pipeline, and bytecode VM benchmarks and reports:

- Oaf timings
- C# baseline timings
- mean ratio (`oaf/csharp`) per benchmark group

You can enforce a regression gate with:

```bash
oaf --benchmark 200 --max-mean-ratio 5.0 --fail-on-regression
```

## Native Language Comparison Benchmarks

For direct Oaf vs C vs Rust algorithm comparisons, run:

```bash
./scripts/benchmark/run_c_rust_benchmarks.sh --oaf-mode tiered
```

This runs identical kernels in all three languages (`sum_xor`, `prime_trial`, `affine_grid`, `branch_mix`, `gcd_fold`, `lcg_stream`) and writes a combined CSV under `benchmarks/results/`.

To run Oaf kernels only:

```bash
oaf --benchmark-kernels --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
```

To run Oaf kernels as generated native binaries:

```bash
oaf --benchmark-kernels --native --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
```

To run Oaf kernels with tiered VM-to-native promotion:

```bash
oaf --benchmark-kernels --tiered --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
```
