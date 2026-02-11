# Announcing Oaf v0.1.0

Oaf v0.1.0 is now available as the first consolidated milestone release.

This release delivers the language front end (lexer, parser, type checker), ownership and lifetime analysis, IR and bytecode generation, runtime subsystem scaffolding, and a broad initial standard library. It also includes practical developer tooling for package management, formatting, documentation generation, and benchmarking.

For release quality, v0.1.0 adds benchmark regression gating, cross-platform CI coverage, packaging scripts for Bash and PowerShell, and a containerized development environment for reproducible setup.

Start here:

1. Run `dotnet run -- --self-test` to validate your environment.
2. Explore examples in `examples/`.
3. Generate docs with `dotnet run -- --gen-docs <path>`.
4. Run performance checks with `dotnet run -- --benchmark --fail-on-regression`.

For full details, see `release/RELEASE_NOTES_v0.1.0.md`.
