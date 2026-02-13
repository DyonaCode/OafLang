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
            ("parses_constructor_expression_with_bracket_arguments", ParsesConstructorExpressionWithBracketArguments),
            ("parses_explicit_cast_expression", ParsesExplicitCastExpression),
            ("keeps_parenthesized_expression_distinct_from_cast", KeepsParenthesizedExpressionDistinctFromCast),
            ("parses_arrow_body_without_double_semicolon", ParsesArrowBodyWithoutDoubleSemicolon),
            ("parses_brace_block_arrow_body", ParsesBraceBlockArrowBody),
            ("accepts_legacy_double_semicolon_terminator", AcceptsLegacyDoubleSemicolonTerminator),
            ("parses_if_comma_separated_conditions", ParsesIfCommaSeparatedConditions),
            ("parses_jot_statement", ParsesJotStatement),
            ("parses_throw_statement", ParsesThrowStatement),
            ("parses_gc_statement", ParsesGcStatement),
            ("parses_match_statement", ParsesMatchStatement),
            ("parses_array_type_and_array_literal", ParsesArrayTypeAndArrayLiteral),
            ("parses_index_expression_and_indexed_assignment", ParsesIndexExpressionAndIndexedAssignment),
            ("parses_counted_paralloop_with_iteration_variable", ParsesCountedParalloopWithIterationVariable),
            ("parses_module_and_import_statements", ParsesModuleAndImportStatements),
            ("parses_string_literal_import_statement", ParsesStringLiteralImportStatement),
            ("parses_public_class_declaration_with_modifiers", ParsesPublicClassDeclarationWithModifiers),
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

    private static void ParsesConstructorExpressionWithBracketArguments()
    {
        const string source = "struct Point [int x, int y]; start = Point[0, 0];";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors);
        TestAssertions.Equal(2, unit.Statements.Count);

        var declaration = unit.Statements[1] as VariableDeclarationStatementSyntax;
        TestAssertions.True(declaration is not null, "Expected variable declaration.");
        TestAssertions.True(declaration!.Initializer is ConstructorExpressionSyntax, "Expected constructor expression initializer.");

        var constructor = (ConstructorExpressionSyntax)declaration.Initializer;
        TestAssertions.Equal("Point", constructor.TargetType.Name);
        TestAssertions.Equal(2, constructor.Arguments.Count);
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

    private static void ParsesIfCommaSeparatedConditions()
    {
        const string source = "if 1 < 2, 3 < 4 => return 1; -> return 0;";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Expected comma-separated if conditions to parse.");
        TestAssertions.Equal(1, unit.Statements.Count);

        var ifStatement = unit.Statements[0] as IfStatementSyntax;
        TestAssertions.True(ifStatement is not null, "Expected if statement.");
        TestAssertions.True(ifStatement!.Condition is BinaryExpressionSyntax, "Expected lowered logical-and condition.");

        var combined = (BinaryExpressionSyntax)ifStatement.Condition;
        TestAssertions.Equal(Frontend.Compiler.Lexer.TokenKind.DoubleAmpersandToken, combined.OperatorKind);
    }

    private static void ParsesJotStatement()
    {
        const string source = "flux x = 4; Jot(x + 1); return x;";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Expected Jot statement to parse.");
        TestAssertions.Equal(3, unit.Statements.Count);
        TestAssertions.True(unit.Statements[1] is JotStatementSyntax, "Expected second statement to be Jot.");
    }

    private static void ParsesThrowStatement()
    {
        const string source = "throw \"OperationFailed\", \"Division by zero\";";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Expected throw statement to parse.");
        TestAssertions.Equal(1, unit.Statements.Count);
        var throwStatement = unit.Statements[0] as ThrowStatementSyntax;
        TestAssertions.True(throwStatement is not null, "Expected throw statement.");
        TestAssertions.True(throwStatement!.ErrorExpression is not null, "Expected throw error expression.");
        TestAssertions.True(throwStatement.DetailExpression is not null, "Expected throw detail expression.");
    }

    private static void ParsesGcStatement()
    {
        const string source = "gc => { flux x = 1; }";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Expected gc statement to parse.");
        TestAssertions.Equal(1, unit.Statements.Count);
        var gcStatement = unit.Statements[0] as GcStatementSyntax;
        TestAssertions.True(gcStatement is not null, "Expected gc statement.");
        TestAssertions.True(gcStatement!.Body is BlockStatementSyntax, "Expected gc body block.");
    }

    private static void ParsesMatchStatement()
    {
        const string source = "flux value = 2; value match => 1 -> value = 10; 2 -> value = 20; -> value = 0;;; return value;";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Expected match statement to parse.");
        TestAssertions.Equal(3, unit.Statements.Count);
        var matchStatement = unit.Statements[1] as MatchStatementSyntax;
        TestAssertions.True(matchStatement is not null, "Expected match statement.");
        TestAssertions.Equal(3, matchStatement!.Arms.Count);
        TestAssertions.True(matchStatement.Arms[2].Pattern is null, "Expected third arm to be default.");
    }

    private static void ParsesArrayTypeAndArrayLiteral()
    {
        const string source = "[int] values = [1, 2, 3];";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Expected array type declaration and literal to parse.");
        TestAssertions.Equal(1, unit.Statements.Count);
        var declaration = unit.Statements[0] as VariableDeclarationStatementSyntax;
        TestAssertions.True(declaration is not null, "Expected typed variable declaration.");
        TestAssertions.True(declaration!.DeclaredType is not null, "Expected declared type.");
        TestAssertions.Equal("array", declaration.DeclaredType!.Name);
        TestAssertions.Equal(1, declaration.DeclaredType.TypeArguments.Count);
        TestAssertions.Equal("int", declaration.DeclaredType.TypeArguments[0].Name);
        TestAssertions.True(declaration.Initializer is ArrayLiteralExpressionSyntax, "Expected array literal initializer.");
    }

    private static void ParsesIndexExpressionAndIndexedAssignment()
    {
        const string source = "flux values = [1, 2, 3]; values[1] = 7; return values[1];";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Expected index syntax to parse.");
        TestAssertions.Equal(3, unit.Statements.Count);
        TestAssertions.True(unit.Statements[1] is IndexedAssignmentStatementSyntax, "Expected indexed assignment statement.");

        var indexedAssignment = (IndexedAssignmentStatementSyntax)unit.Statements[1];
        TestAssertions.True(indexedAssignment.Target is IndexExpressionSyntax, "Expected indexed assignment target expression.");

        var returnStatement = unit.Statements[2] as ReturnStatementSyntax;
        TestAssertions.True(returnStatement is not null, "Expected return statement.");
        TestAssertions.True(returnStatement!.Expression is IndexExpressionSyntax, "Expected indexed return expression.");
    }

    private static void ParsesCountedParalloopWithIterationVariable()
    {
        const string source = "flux values = [0, 0, 0, 0]; paralloop 4, i => values[i] = i + 1; return values[3];";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Expected counted paralloop syntax to parse.");
        TestAssertions.Equal(3, unit.Statements.Count);
        var loop = unit.Statements[1] as LoopStatementSyntax;
        TestAssertions.True(loop is not null, "Expected paralloop statement.");
        TestAssertions.True(loop!.IsParallel, "Expected paralloop flag to be set.");
        TestAssertions.Equal("i", loop.IterationVariable);
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

    private static void ParsesStringLiteralImportStatement()
    {
        const string source = "module pkg.math; import \"github.com/user/package/v1\"; flux x = 1;";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Expected string literal import to parse.");
        TestAssertions.Equal(3, unit.Statements.Count);
        var import = unit.Statements[1] as ImportStatementSyntax;
        TestAssertions.True(import is not null, "Expected import statement.");
        TestAssertions.Equal("github.com/user/package/v1", import!.ModuleName);
    }

    private static void ParsesPublicClassDeclarationWithModifiers()
    {
        const string source = "public class Program public, gcoff;";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Expected public class with modifiers to parse.");
        TestAssertions.Equal(1, unit.Statements.Count);
        TestAssertions.True(unit.Statements[0] is ClassDeclarationStatementSyntax, "Expected class declaration.");
        var classDeclaration = (ClassDeclarationStatementSyntax)unit.Statements[0];
        TestAssertions.Equal("Program", classDeclaration.Name);
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
