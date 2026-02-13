# Runtime Concurrency Examples (C)

These examples exercise runtime-level concurrency features that are not yet first-class Oaf language constructs:

- lightweight threads + scheduler + channels
- task pool + async/future + parallel map/reduce helpers

## Build

```bash
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release --target oaf_example_lightweight_threads oaf_example_task_parallel
```

## Run

```bash
./build/oaf_example_lightweight_threads
./build/oaf_example_task_parallel
```
