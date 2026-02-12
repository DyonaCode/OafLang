# Oaf

Oaf is a dumb language for smart people with bad taste.  

This reop includes the language frontend, runtime, CLI tooling, tests, examples, and release packaging scripts.

## Overview

- `oaf` CLI for run/build/publish/test/tooling workflows
- Managed compiler pipeline (lexing, parsing, type checking, IR, bytecode)
- Bytecode VM and native runtime components (C)
- Package, formatting, docs generation, and benchmarking tools
- Cross-platform CI and release packaging

## Install

### Install from a release package (recommended)

1. Download and extract the release archive for your platform.
2. From the extracted folder:
   - macOS/Linux: `./install.sh`
   - Windows PowerShell: `./install.ps1`
3. Open a new shell, then verify:
   - `oaf --version`
   - `oaf version`

The installer keeps SDKs versioned under `~/.oaf/versions/<version>` and sets an active version pointer.

### Build from source

```bash
dotnet build
dotnet run -- --self-test
```

Runtime smoke targets (CMake):

```bash
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release
```

## Getting Started

Create `main.oaf`:

```oaf
flux total = 0;
flux i = 1;
loop i <= 10 =>
    total += i;
    i += 1;
;
return total;
```

Run:

```bash
oaf
```

Other commands:

```bash
oaf run ./examples/basics/01_hello_and_return.oaf
oaf build ./examples/basics/01_hello_and_return.oaf
// Experimental:
oaf publish ./examples/applications/01_sum_accumulator.oaf
```

Notes:
- `oaf` with no args runs `./main.oaf` when present.
- `oaf build` produces build artifacts (default: bytecode output).
- `oaf publish` targets native executable output.

## Documentation

Start with the documentation hub:

- `docs/README.md`

Key references:

- `docs/guides/GettingStarted.md`
- `docs/reference/CLIReference.md`
- `docs/LanguageSpecification.md`
- `docs/reference/RuntimeAndStdlibReference.md`
- `release/RELEASE_NOTES_v0.1.0.md`
