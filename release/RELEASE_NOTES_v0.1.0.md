# Oaf v0.1.0 Release Notes

Release Date: 2026-02-11

## Highlights

- Completed parser, AST, type checker, ownership analysis, and IR pipeline foundations.
- Added bytecode generation and execution support with VM smoke coverage.
- Implemented runtime subsystems for memory, errors, types, concurrency, and FFI scaffolding.
- Delivered standard library modules for collections, text, IO, algorithms, serialization, and concurrency primitives.
- Added tooling commands for package management, documentation generation, formatting, and benchmarks.
- Expanded test and integration coverage, including example program validation and benchmark reporting.

## Stability and Release Readiness

- Fixed documentation generation for directory inputs with duplicate file names in different subdirectories.
- Added benchmark regression gate support (`--max-mean-ratio`, `--fail-on-regression`) for release validation.
- Added cross-platform CI workflow coverage (Linux, macOS, Windows for .NET; Linux runtime smoke binaries).
- Added release packaging scripts for Bash and PowerShell.
- Added containerized development environment (`Dockerfile`, `docker-compose.yml`).

## CLI Additions

- `--benchmark [iterations] [--max-mean-ratio <value>] [--fail-on-regression]`
- `--gen-docs <file-or-directory> [--out <outputPath>]`
- `--format <file-or-source> [--check] [--write]`
- `--pkg-init`, `--pkg-add`, `--pkg-remove`, `--pkg-install`
- `--self-test`

## Packaging

- Linux/macOS packaging script: `scripts/release/create_package.sh`
- Windows packaging script: `scripts/release/create_package.ps1`
- Generated artifacts are written to `dist/`.

## Known Limitations

- Interpreter and REPL milestones remain incomplete.
- Some 1.0 release milestones are still in progress.
