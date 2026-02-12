# Cross-Language Benchmark Suite (Oaf, C, Rust)

This directory provides apples-to-apples benchmark kernels implemented in:

- Oaf (`benchmarks/oaf/*.oaf`, plus generated workload variants via CLI runner)
- C (`benchmarks/native/bench_c.c`)
- Rust (`benchmarks/native/bench_rust.rs`)

Latest CI benchmark snapshot:

- `CI_BENCHMARKS.md`

The suite currently includes:

1. `sum_xor`: integer arithmetic + loop throughput
2. `prime_trial`: trial-division prime counting
3. `affine_grid`: O(n^3) arithmetic-heavy nested loops
4. `branch_mix`: branch-heavy bitwise/arithmetic control flow
5. `gcd_fold`: modulo-heavy Euclidean reduction workload
6. `lcg_stream`: dependency-chain pseudo-random recurrence

## Quick Run

```bash
./scripts/benchmark/run_c_rust_benchmarks.sh
```

This compiles/runs all three implementations (C, Rust, Oaf) and writes a combined CSV to:

- `benchmarks/results/native_compare_<timestamp>.csv`

## Custom Workload

```bash
./scripts/benchmark/run_c_rust_benchmarks.sh \
  --iterations 10 \
  --sum-n 15000000 \
  --prime-n 75000 \
  --matrix-n 64
```

Parameters:

- `--iterations`: number of repetitions per algorithm
- `--sum-n`: upper bound for `sum_xor`
- `--prime-n`: upper bound for `prime_trial`
- `--matrix-n`: grid side length for `affine_grid`
- `--oaf-mode`: `all` (default), `native`, `tiered`, `vm`, `both` (`vm` + `exe`), or `mlir` (`mlir+vm` + `mlir+exe`)
- `--oaf-cli`: optional executable/command for Oaf CLI. Default is `dotnet run --configuration Release --`.
- `--out`: explicit output CSV path

Example using installed SDK tool and including VM/native plus MLIR VM/native rows:

```bash
./scripts/benchmark/run_c_rust_benchmarks.sh \
  --iterations 5 \
  --oaf-mode all \
  --oaf-cli ./.oaf/sdk-tools/oaf
```

## Oaf-Only Kernel Run

```bash
dotnet run -- --benchmark-kernels --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
```

This prints the Oaf kernel benchmark rows in CSV format.

Use `--native` to execute via generated native binaries:

```bash
dotnet run -- --benchmark-kernels --native --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
```

Use `--tiered` to run in VM first, then switch hot iterations to native:

```bash
dotnet run -- --benchmark-kernels --tiered --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
```

Use `--compilation-target mlir` to benchmark the MLIR-targeted compilation path with the same runtime outputs:

```bash
dotnet run -- --benchmark-kernels --compilation-target mlir --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
```

## Notes for Fair Comparisons

1. Run on an idle machine and repeat at least 3 times.
2. Keep identical workload parameters across languages.
3. Use optimized builds only (`-O3` / Rust `-O`).
4. Compare `mean_ms` per algorithm, not just one aggregate number.
