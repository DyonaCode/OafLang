# Oaf v0.1 Compatibility Contract

This document defines what is considered stable for the `v0.1.x` line.

## Scope

The contract applies to:

- `oaf` CLI behavior for run/build/publish/package commands
- Frontend language behavior already implemented in tests/docs
- Runtime execution parity between supported compilation targets and runtimes
- Package resolution/install/verify behavior

## Stable Guarantees

1. CLI command compatibility

- Existing documented commands and flags in `docs/reference/CLIReference.md` are stable across `v0.1.x`.
- Aliases (`add`, `remove`, `restore`, `verify`) remain supported.
- `--compilation-target bytecode|mlir` remains accepted for compile/run/build/publish flows.

2. Compilation target parity

- `--compilation-target bytecode` and `--compilation-target mlir` must produce identical observable program results for supported language features.
- MLIR target is currently an internal lowering stage; output runtime artifacts are still bytecode/native.

3. Runtime parity

- VM execution and native execution must produce identical return values for deterministic programs in supported runtime subset.

4. Package manager behavior

- Manifest (`packages.txt`), lock file (`packages.lock`), and verify semantics are stable.
- Package module path convention is enforced:
  - `content/pkg/math.oaf` must declare `module pkg.math;`

## Allowed Changes in v0.1.x

- Bug fixes
- Performance improvements that preserve observable behavior
- Diagnostics improvements (wording/clarity)
- Internal refactoring that preserves contract guarantees

## Not Guaranteed in v0.1.x

- New language syntax beyond current documented behavior
- Binary/ABI compatibility for internal runtime artifacts
- External MLIR toolchain integration (current MLIR path is internal)

## Enforcement

The following must remain green for changes targeting `v0.1.x`:

- `dotnet run -- --self-test`
- Cross-target parity tests (bytecode vs mlir)
- Runtime parity tests (vm vs native when native compiler exists)
- CI benchmark reporting scripts

