# Tutorial 01: Your First Oaf Program

## Goal

Create and run a minimal program that returns a value.

## Source

```oaf
message = "Hello, Oaf";
return message;
```

Saved example: `../basics/01_hello_and_return.oaf`

## Run

```bash
dotnet run -- ./examples/basics/01_hello_and_return.oaf --run-bytecode
```

## Inspect Compiler Stages

```bash
dotnet run -- ./examples/basics/01_hello_and_return.oaf --ast
dotnet run -- ./examples/basics/01_hello_and_return.oaf --ir
dotnet run -- ./examples/basics/01_hello_and_return.oaf --bytecode
```
