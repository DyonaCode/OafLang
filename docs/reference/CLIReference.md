# CLI Reference

## Core Commands

### SDK-style Commands
Install local `oaf` command:

```bash
./scripts/sdk/install_local_tool.sh 0.1.1
export PATH="$(pwd)/.oaf/sdk-tools:$PATH"
```

Run/build/clean with dotnet-like command names and `-o`/`-r` options:

```bash
oaf run ./examples/basics/01_hello_and_return.oaf -r vm
oaf build ./examples/basics/01_hello_and_return.oaf -o ./out/oaf -r native
oaf clean -o ./out/oaf
```

### `--self-test`
Run all managed unit/integration tests.

```bash
dotnet run -- --self-test
```

### Compile Source
Compile inline source or file path.

```bash
dotnet run -- "flux x = 1; return x;"
dotnet run -- ./examples/basics/01_hello_and_return.oaf
```

Optional flags:

- `--ast`
- `--ir`
- `--bytecode`
- `--run-bytecode`

## Package Manager

### `--pkg-init [manifestPath]`
Create a package manifest.

### `--pkg-add <name@version> [manifestPath]`
Add or update dependency entry.

### `--pkg-remove <name> [manifestPath]`
Remove dependency by name.

### `--pkg-install [manifestPath]`
Materialize `.oaf/packages/*` and generate `packages.lock`.

## Documentation Generator

### `--gen-docs <file-or-directory> [--out <outputPath>]`
Generate markdown documentation from Oaf source.

## Formatter

### `--format <file-or-source> [--check] [--write]`

- default: print formatted output
- `--check`: fail (exit code 1) if formatting changes are needed
- `--write`: write formatted output back to file

## Benchmarks

### `--benchmark [iterations] [--max-mean-ratio <value>] [--fail-on-regression]`
Run benchmark suite and emit Oaf vs C# baseline report.

```bash
dotnet run -- --benchmark 200 --max-mean-ratio 5.0 --fail-on-regression
```

### `--benchmark-kernels [--iterations <n>] [--sum-n <n>] [--prime-n <n>] [--matrix-n <n>] [--native|--tiered]`
Run Oaf algorithm kernels (`sum_xor`, `prime_trial`, `affine_grid`) in the bytecode VM and emit CSV output.

```bash
dotnet run -- --benchmark-kernels --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
```

Use `--native` to execute kernels via generated native binaries (requires system C compiler):

```bash
dotnet run -- --benchmark-kernels --native --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
```

Use `--tiered` to start in VM and promote hot iterations to native execution:

```bash
dotnet run -- --benchmark-kernels --tiered --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
```

## Native Comparison Script

Run C, Rust, and Oaf benchmark kernels with shared workload settings:

```bash
./scripts/benchmark/run_c_rust_benchmarks.sh --iterations 5 --oaf-mode both --oaf-cli ./.oaf/sdk-tools/oaf
```
