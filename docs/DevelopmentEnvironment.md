# Development Environment

## Prerequisites

- .NET SDK 10.0 or newer
- CMake 3.25 or newer
- A C compiler for runtime work (Clang or GCC)

## Build and Test

```bash
dotnet build
oaf --self-test
```

## Install Local CLI (Release Package)

```bash
./install.sh
oaf --version
oaf version
```

For full end-user setup and first program workflow, see `guides/GettingStarted.md`.

## CMake Runtime Targets

```bash
cmake -S . -B out/cmake-build
cmake --build out/cmake-build
./out/cmake-build/oaf_error_smoke
./out/cmake-build/oaf_memory_smoke
./out/cmake-build/oaf_runtime_smoke
./out/cmake-build/oaf_types_smoke
./out/cmake-build/oaf_concurrency_smoke
./out/cmake-build/oaf_ffi_smoke
./out/cmake-build/oaf_collections_smoke
./out/cmake-build/oaf_stdlib_smoke
./out/cmake-build/oaf_advanced_concurrency_smoke
```

## Repository Conventions

- Frontend compiler code lives under `src/Frontend/Compiler/`
- Runtime C code lives under `src/Runtime/`
- Self-tests can be run via `oaf --self-test`

## Tooling Commands

```bash
oaf --pkg-init packages.txt
oaf --pkg-add core.math@1.0.0 packages.txt
oaf --pkg-install packages.txt
oaf --gen-docs ./sample.oaf --out ./sample.md
oaf --format ./sample.oaf --write
oaf --benchmark 200
oaf --benchmark-kernels --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
oaf --benchmark-kernels --tiered --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
oaf --benchmark-kernels --native --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
./scripts/benchmark/run_c_rust_benchmarks.sh --iterations 5 --oaf-mode both --oaf-cli oaf
```
