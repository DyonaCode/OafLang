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
- `oaf new [path] [--force]`
  Create a project scaffold (main file, tests, examples, manifest, README).
- `oaf test [path] [-r vm|native] [--compilation-target bytecode|mlir]`
  Discover `.oaf` files in `tests/` + `examples/` (or an explicit file/directory), compile, and execute them.
- `oaf run <file-or-source> [-r vm|native] [--compilation-target bytecode|mlir]`
  Compile and execute source.
- `oaf build <file-or-source> [-o <output-path>] [-r vm|native] [--compilation-target bytecode|mlir]`
  Build artifacts (default flow: bytecode/native artifacts; MLIR target uses the same output formats).
- `oaf publish <file-or-source> [-o <output-path>] [-r native] [--compilation-target bytecode|mlir]`
  Publish native executable output.
- `oaf clean [-o <path>]`
  Remove build output.
- `oaf --self-test`
  Run managed self-test suite.

Useful examples:

```bash
oaf new my-app
oaf test
oaf test ./examples --compilation-target mlir
oaf run ./examples/basics/01_hello_and_return.oaf -r vm
oaf build ./examples/basics/01_hello_and_return.oaf -o ./out/oaf
oaf publish ./examples/applications/01_sum_accumulator.oaf -o ./out/oaf-publish
oaf clean -o ./out/oaf
```

Optional inspection flags for compile/run commands:

- `--ast`
- `--ir`
- `--bytecode`
- `--mlir`
- `--compilation-target bytecode|mlir`
- `--run-bytecode`

Notes:

- `--compilation-target mlir` currently uses an internal MLIR lowering stage and still emits the same runnable bytecode/native outputs as `bytecode`.
- `oaf test` verifies packages only when a `packages.lock` is present next to the nearest `packages.txt`.

## Package Manager

- `--pkg-init [manifestPath]`
- `--pkg-add <name@version> [manifestPath]`
- `--pkg-remove <name> [manifestPath]`
- `--pkg-install [manifestPath]`
- `--pkg-verify [manifestPath]`
- `add [package] <name@version> [manifestPath]` (alias of `--pkg-add`)
- `remove [package] <name> [manifestPath]` (alias of `--pkg-remove`)
- `restore [manifestPath]` (alias of `--pkg-install`)
- `verify [manifestPath]` (alias of `--pkg-verify`)

Source-backed installs:

- Optional `packages.sources` next to `packages.txt` (or `OAF_PACKAGE_INDEX` env var) points to one or more JSON package indexes.
- Index schema:

```json
{
  "source": "localrepo",
  "packages": [
    {
      "name": "core.math",
      "version": "1.0.0",
      "artifact": "./core.math-1.0.0.oafpkg",
      "sha256": "...",
      "dependencies": ["core.runtime@^1.0.0", "core.text@>=2.0.0 <3.0.0"]
    }
  ]
}
```

- `--pkg-install` verifies artifact SHA256 and extracts `.zip`/`.nupkg`/`.oafpkg` into `.oaf/packages/<name>/<version>/content`.
- `packages.txt` selectors can be exact or ranges (`1.2.3`, `^1.2.0`, `~2.4.0`, `>=1.0.0 <2.0.0`, `1.3.*`).
- Resolver includes transitive dependencies and fails install on version conflicts.
- Package module files must match their content-relative module path (`content/pkg/math.oaf` must declare `module pkg.math;`).
- `oaf run`, `oaf build`, `oaf publish`, and direct compile mode compose only explicitly imported package modules (including transitive imports) from nearest `packages.lock` and `.oaf/packages/**/content`.

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
oaf --benchmark 200 --comparison-statistic mean --max-ratio 5.0 --fail-on-regression
oaf --benchmark 200 --comparison-statistic p95 --max-ratio 3.0 --max-ratio-for lexer=5.0 --fail-on-regression
```

Notes:
- `--comparison-statistic` accepts `mean`, `median`, or `p95`.
- `--max-ratio-for` can be repeated to override thresholds per benchmark.
- `--max-mean-ratio` remains supported as a legacy alias for `--max-ratio`.

Run kernel benchmarks:

```bash
oaf --benchmark-kernels --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
oaf --benchmark-kernels --native --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
oaf --benchmark-kernels --tiered --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
oaf --benchmark-kernels --compilation-target mlir --iterations 5 --sum-n 5000000 --prime-n 30000 --matrix-n 48
```

Native comparison script:

```bash
./scripts/benchmark/run_c_rust_benchmarks.sh --iterations 5 --oaf-mode all --oaf-cli oaf
```
