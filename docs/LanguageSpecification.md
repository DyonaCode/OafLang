# Oaf Language Specification (Implemented Subset)

This specification describes the language features currently implemented in the compiler/runtime in this repository.

## 1. Lexical Structure

### 1.1 Tokens

- Keywords: `if`, `loop`, `paralloop`, `return`, `flux`, `struct`, `class`, `enum`, `break`, `continue`, `true`, `false`
- Literals: integer, floating point, char, string, boolean
- Delimiters: `(`, `)`, `[`, `]`, `{`, `}`, `,`, `.`, `:`, `;`
- Operators:
  - arithmetic: `+`, `-`, `*`, `/`, `%`, `/^`
  - assignment: `=`, `+=`, `-=`, `*=`, `/=`
  - comparison: `==`, `!=`, `<`, `<=`, `>`, `>=`
  - bitwise/logical families implemented in lexer/parser precedence tables
  - control: `=>` (body start), `->` (else branch), `;;` (block terminator)

### 1.2 Comments

- single-line: `// ...` and `# ...`
- block: `/# ... #/`
- doc block (lexed as trivia): `@# ... #@`

## 2. Program Structure

A source file is parsed as a `CompilationUnit` containing a flat list of statements.

Supported top-level statements:

- variable declarations (typed and inferred)
- assignments
- expression statements
- `if`/`if-else`
- `loop` and `paralloop`
- `return`
- `break` / `continue`
- type declarations (`struct`, `class`, `enum`)

## 3. Type Declarations

### 3.1 Struct

```oaf
struct Point [int x, int y];
```

### 3.2 Class

```oaf
class User [string name, int age];
```

### 3.3 Enum

```oaf
enum Option<T> => Some(T), None;
```

### 3.4 Generics

Generic parameters and type arguments are parsed and type-checked for arity.

```oaf
struct Pair<T> [T left, T right];
Pair<int> p = 0;
```

## 4. Variables and Assignment

### 4.1 Immutable (default)

```oaf
count = 10;
```

### 4.2 Mutable (`flux`)

```oaf
flux total = 0;
total += 5;
```

### 4.3 Typed Declarations

```oaf
int x = 1;
float y = x;
```

## 5. Expressions

### 5.1 Primary

- literals
- identifiers
- parenthesized expressions

### 5.2 Unary

- `+`, `-`, `!`, `~`

### 5.3 Binary

Binary precedence is implemented (multiplicative > additive > shift > comparison > equality > logical families).

### 5.4 Explicit Casts

```oaf
float f = 3.9;
int i = (int)f;
```

Type checker supports explicit narrowing casts and rejects invalid casts.

## 6. Control Flow

### 6.1 If / If-Else

```oaf
if condition => statement;;
if condition => statement; -> alternative;;
```

### 6.2 Loop

```oaf
loop condition =>
    // statements
;;
```

### 6.3 Parallel Loop Syntax

```oaf
paralloop condition =>
    // statements
;;
```

`paralloop` is parsed and represented in AST; runtime-level parallel utilities are available in stdlib concurrency modules.

### 6.4 Break / Continue

`break` and `continue` are only valid inside loop contexts.

## 7. Type System Rules

### 7.1 Built-in Primitive Types

- `int`
- `float`
- `bool`
- `char`
- `string`
- `void`

### 7.2 Numeric Coercions

- implicit widening is allowed (for implemented combinations such as `char -> int`, `int -> float`, `char -> float`)
- implicit narrowing is rejected
- explicit numeric narrowing via cast is allowed

### 7.3 Generic Validation

- type definitions with generic parameters are tracked
- generic arity mismatches produce diagnostics

## 8. Ownership and Lifetime Analysis

Ownership analysis runs after type checking and enforces:

- move semantics for move types
- immutable/mutable borrow constraints
- detection of invalid use-after-move scenarios

## 9. Intermediate Representation and Bytecode

Compilation pipeline:

1. Lexing
2. Parsing
3. Type checking
4. Ownership analysis
5. IR lowering
6. IR optimization passes
7. Bytecode generation
8. Bytecode VM execution (optional)

## 10. Diagnostics

Diagnostics include:

- lexer, parser, type checker, ownership errors
- source line/column data
- explicit type/cast and control-flow validation messages

## 11. Conformance Notes

- This specification describes the current implemented subset, not all aspirational syntax in historical design docs.
- Future extensions should update this file and corresponding tests together.
