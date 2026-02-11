# Getting Started

## Install

Use a release package and run the installer:

- macOS/Linux: `./install.sh`
- Windows PowerShell: `./install.ps1`

Then verify:

```bash
oaf --version
oaf version
```

For source-based setup, see `../DevelopmentEnvironment.md`.

## First Program

Create `main.oaf`:

```oaf
flux x = 2;
return x + 3;
```

Run:

```bash
oaf
```

Equivalent explicit form:

```bash
oaf run ./main.oaf
```

## Build and Publish

Build artifact:

```bash
oaf build ./main.oaf
```

Publish native executable:

```bash
oaf publish ./examples/applications/01_sum_accumulator.oaf
```

## Compiler Introspection

```bash
oaf run ./examples/basics/02_control_flow_if_else.oaf --ast
oaf run ./examples/basics/02_control_flow_if_else.oaf --ir
oaf run ./examples/basics/02_control_flow_if_else.oaf --bytecode
```

## Tooling Quick Hits

```bash
oaf --pkg-init packages.txt
oaf --pkg-add stdlib.core@1.0.0 packages.txt
oaf --pkg-install packages.txt
oaf --gen-docs ./examples/basics/01_hello_and_return.oaf --out ./docs/generated/hello.md
oaf --format ./examples/basics/01_hello_and_return.oaf --write
oaf --benchmark 200
```
