# CLI Reference

## Core SDK Commands

- `oaf`
  Run `./main.oaf` when present, otherwise print usage.
- `oaf --version`
  Print the active CLI version.
- `oaf version`
  List installed versions and show the active one.
- `oaf version <version>`
  Switch the active local version.
- `oaf run <file-or-source> [-r vm|native]`
  Compile and execute source.
- `oaf build <file-or-source> [-o <output-path>] [-r vm|native]`
  Build artifacts (default flow: bytecode artifact).
- `oaf publish <file-or-source> [-o <output-path>] [-r native]`
  Publish native executable output.
- `oaf clean [-o <path>]`
  Remove build output.
- `oaf --self-test`
  Run managed self-test suite.

Useful examples:

```bash
oaf run ./examples/basics/01_hello_and_return.oaf -r vm
oaf build ./examples/basics/01_hello_and_return.oaf -o ./out/oaf
oaf publish ./examples/applications/01_sum_accumulator.oaf -o ./out/oaf-publish
oaf clean -o ./out/oaf
```

Optional inspection flags for compile/run commands:

- `--ast`
- `--ir`
- `--bytecode`
- `--run-bytecode`

## Package Manager

- `--pkg-init [manifestPath]`
- `--pkg-add <name@version> [manifestPath]`
- `--pkg-remove <name> [manifestPath]`
- `--pkg-install [manifestPath]`

## Documentation Generator

- `--gen-docs <file-or-directory> [--out <outputPath>]`

## Formatter

- `--format <file-or-source> [--check] [--write]`
  - default: print formatted output
  - `--check`: non-zero exit if formatting changes are needed
  - `--write`: write formatted output back to file

## Benchmarks

Run benchmark suite:

```bash
oaf --benchmark 200 --max-mean-ratio 5.0 --fail-on-regression
```

Run kernel benchmarks:

```bash
oaf --benchmark-kernels --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
oaf --benchmark-kernels --native --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
oaf --benchmark-kernels --tiered --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
```

Native comparison script:

```bash
./scripts/benchmark/run_c_rust_benchmarks.sh --iterations 5 --oaf-mode both --oaf-cli oaf
```
