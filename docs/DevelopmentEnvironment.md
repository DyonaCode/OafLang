# Development Environment

## Prerequisites

- .NET SDK 10.0 or newer
- CMake 3.25 or newer
- A C compiler for runtime work (Clang or GCC)

## Build and Test

```bash
dotnet build
dotnet run -- --self-test
```

## Install Local SDK Alias (`oaf`)

```bash
./scripts/sdk/install_local_tool.sh 0.1.1
export PATH="$(pwd)/.oaf/sdk-tools:$PATH"
oaf run "return 42;" -r vm
oaf build "return 42;" -o ./out/oaf -r native
oaf clean -o ./out/oaf
```

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
- Self-tests currently run via `dotnet run -- --self-test`

## Tooling Commands

```bash
dotnet run -- --pkg-init packages.txt
dotnet run -- --pkg-add core.math@1.0.0 packages.txt
dotnet run -- --pkg-install packages.txt
dotnet run -- --gen-docs ./sample.oaf --out ./sample.md
dotnet run -- --format ./sample.oaf --write
dotnet run -- --benchmark 200
dotnet run -- --benchmark-kernels --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
dotnet run -- --benchmark-kernels --tiered --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
dotnet run -- --benchmark-kernels --native --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
./scripts/benchmark/run_c_rust_benchmarks.sh --iterations 5 --oaf-mode tiered
./scripts/benchmark/run_c_rust_benchmarks.sh --iterations 5 --oaf-mode both --oaf-cli ./.oaf/sdk-tools/oaf
```
