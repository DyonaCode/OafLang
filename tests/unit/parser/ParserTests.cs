using Oaf.Frontend.Compiler.AST;
using Oaf.Tests.Framework;

namespace Oaf.Tests.Unit.Parser;

public static class ParserTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("respects_binary_precedence", RespectsBinaryPrecedence),
            ("recovers_after_missing_semicolon", RecoversAfterMissingSemicolon),
            ("parses_type_declarations", ParsesTypeDeclarations),
            ("parses_typed_variable_declaration", ParsesTypedVariableDeclaration),
            ("parses_explicit_cast_expression", ParsesExplicitCastExpression),
            ("keeps_parenthesized_expression_distinct_from_cast", KeepsParenthesizedExpressionDistinctFromCast),
            ("parses_arrow_body_without_double_semicolon", ParsesArrowBodyWithoutDoubleSemicolon),
            ("parses_brace_block_arrow_body", ParsesBraceBlockArrowBody),
            ("accepts_legacy_double_semicolon_terminator", AcceptsLegacyDoubleSemicolonTerminator),
            ("parses_module_and_import_statements", ParsesModuleAndImportStatements),
            ("parses_qualified_identifier_expressions", ParsesQualifiedIdentifierExpressions)
        ];
    }

    private static void RespectsBinaryPrecedence()
    {
        const string source = "value = 1 + 2 * 3;";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Parser emitted diagnostics for valid expression.");
        TestAssertions.Equal(1, unit.Statements.Count);

        var declaration = unit.Statements[0] as VariableDeclarationStatementSyntax;
        TestAssertions.True(declaration is not null, "Expected variable declaration statement.");

        var binary = declaration!.Initializer as BinaryExpressionSyntax;
        TestAssertions.True(binary is not null, "Expected binary expression initializer.");
        TestAssertions.Equal(Frontend.Compiler.Lexer.TokenKind.PlusToken, binary!.OperatorKind);
        TestAssertions.True(binary.Right is BinaryExpressionSyntax, "Expected multiplication on right side of +.");
    }

    private static void RecoversAfterMissingSemicolon()
    {
        const string source = "flux x = 1\nflux y = 2;";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.True(parser.Diagnostics.HasErrors, "Expected parser diagnostics for missing semicolon.");
        TestAssertions.Equal(2, unit.Statements.Count, "Parser should recover and parse second declaration.");
    }

    private static void ParsesTypeDeclarations()
    {
        const string source = "struct Pair<T> [T left, T right]; enum Option<T> => Some(T), None; class Person [string name];";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Expected type declarations to parse without diagnostics.");
        TestAssertions.Equal(3, unit.Statements.Count);
        TestAssertions.True(unit.Statements[0] is StructDeclarationStatementSyntax, "Expected struct declaration.");
        TestAssertions.True(unit.Statements[1] is EnumDeclarationStatementSyntax, "Expected enum declaration.");
        TestAssertions.True(unit.Statements[2] is ClassDeclarationStatementSyntax, "Expected class declaration.");
    }

    private static void ParsesTypedVariableDeclaration()
    {
        const string source = "int value = 42;";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors);
        var declaration = unit.Statements[0] as VariableDeclarationStatementSyntax;
        TestAssertions.True(declaration is not null, "Expected typed variable declaration.");
        TestAssertions.True(declaration!.DeclaredType is not null, "Expected declared type to be captured.");
        TestAssertions.Equal("int", declaration.DeclaredType!.Name);
    }

    private static void ParsesExplicitCastExpression()
    {
        const string source = "result = (int)3.5 + 1;";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors);
        var declaration = unit.Statements[0] as VariableDeclarationStatementSyntax;
        TestAssertions.True(declaration is not null, "Expected variable declaration statement.");

        var binary = declaration!.Initializer as BinaryExpressionSyntax;
        TestAssertions.True(binary is not null, "Expected binary expression.");
        TestAssertions.True(binary!.Left is CastExpressionSyntax, "Expected left operand to be cast expression.");

        var cast = (CastExpressionSyntax)binary.Left;
        TestAssertions.Equal("int", cast.TargetType.Name);
    }

    private static void KeepsParenthesizedExpressionDistinctFromCast()
    {
        const string source = "value = (1 + 2);";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors);
        var declaration = unit.Statements[0] as VariableDeclarationStatementSyntax;
        TestAssertions.True(declaration is not null, "Expected variable declaration.");
        TestAssertions.True(declaration!.Initializer is ParenthesizedExpressionSyntax, "Expected parenthesized expression, not cast.");
    }

    private static void ParsesArrowBodyWithoutDoubleSemicolon()
    {
        const string source = "flux x = 0; if true => x += 1; return x;";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Expected arrow body to parse without trailing ';;'.");
        TestAssertions.Equal(3, unit.Statements.Count);

        var ifStatement = unit.Statements[1] as IfStatementSyntax;
        TestAssertions.True(ifStatement is not null, "Expected if statement as second top-level statement.");
        TestAssertions.True(ifStatement!.ThenStatement is AssignmentStatementSyntax, "Expected single assignment then-body.");
    }

    private static void ParsesBraceBlockArrowBody()
    {
        const string source = "flux i = 0; loop i < 3 => { i += 1; } return i;";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Expected brace block loop body to parse.");
        var loop = unit.Statements[1] as LoopStatementSyntax;
        TestAssertions.True(loop is not null, "Expected loop statement.");
        TestAssertions.True(loop!.Body is BlockStatementSyntax, "Expected loop body to parse as block statement.");
    }

    private static void AcceptsLegacyDoubleSemicolonTerminator()
    {
        const string source = "if true => return 1;;; return 2;";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Expected legacy ';;' body terminator to remain supported.");
        TestAssertions.Equal(2, unit.Statements.Count);
    }

    private static void ParsesModuleAndImportStatements()
    {
        const string source = "module pkg.math; import pkg.core; flux x = 1;";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Expected module/import syntax to parse.");
        TestAssertions.Equal(3, unit.Statements.Count);
        TestAssertions.True(unit.Statements[0] is ModuleDeclarationStatementSyntax);
        TestAssertions.True(unit.Statements[1] is ImportStatementSyntax);

        var module = unit.Statements[0] as ModuleDeclarationStatementSyntax;
        var import = unit.Statements[1] as ImportStatementSyntax;
        TestAssertions.Equal("pkg.math", module!.ModuleName);
        TestAssertions.Equal("pkg.core", import!.ModuleName);
    }

    private static void ParsesQualifiedIdentifierExpressions()
    {
        const string source = "flux x = pkg.math.value; pkg.math.value = x;";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Expected qualified identifiers to parse.");
        TestAssertions.Equal(2, unit.Statements.Count);

        var declaration = unit.Statements[0] as VariableDeclarationStatementSyntax;
        TestAssertions.True(declaration is not null, "Expected declaration.");
        TestAssertions.True(declaration!.Initializer is NameExpressionSyntax, "Expected name expression initializer.");
        var initializer = (NameExpressionSyntax)declaration.Initializer;
        TestAssertions.Equal("pkg.math.value", initializer.Identifier);

        var assignment = unit.Statements[1] as AssignmentStatementSyntax;
        TestAssertions.True(assignment is not null, "Expected assignment.");
        TestAssertions.Equal("pkg.math.value", assignment!.Identifier);
    }
}
