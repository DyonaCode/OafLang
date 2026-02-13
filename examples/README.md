# Oaf Examples

This folder contains tested example programs for the implemented compiler/runtime subset.

## Structure

- `basics/` - small focused language examples
- `tutorials/` - guided walkthroughs
- `applications/` - larger sample applications
- `runtime/` - C runtime API examples (threads, tasks, channels, parallel helpers)

## Run Examples

```bash
dotnet run -- ./examples/basics/01_hello_and_return.oaf --run-bytecode
dotnet run -- ./examples/applications/01_sum_accumulator.oaf --run-bytecode
```

Runtime C examples:

```bash
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release --target oaf_example_lightweight_threads oaf_example_task_parallel
./build/oaf_example_lightweight_threads
./build/oaf_example_task_parallel
```

Counted managed `paralloop` example:

```bash
dotnet run -- run ./examples/applications/06_parallel_workload_demo.oaf
```
