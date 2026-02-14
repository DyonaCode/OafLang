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
            ("parses_http_intrinsic_constructor_expressions", ParsesHttpIntrinsicConstructorExpressions),
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

    private static void ParsesHttpIntrinsicConstructorExpressions()
    {
        const string source = "body = HttpGet[\"http://example.com\"]; send = HttpSend[\"http://example.com\", 1, \"name=oaf\", 5000, \"Accept: application/json\\nX-Debug: 1\"]; status = HttpLastStatus[]; error = HttpLastError[]; reason = HttpLastReason[]; ct = HttpLastContentType[]; hs = HttpLastHeaders[]; server = HttpLastHeader[\"Server\"]; hb = HttpHeader[\"\", \"Accept\", \"application/json\"]; q = HttpQuery[\"https://api.example.com/search\", \"q\", \"bakery in berlin\"]; enc = HttpUrlEncode[\"bakery in berlin\"]; lastBody = HttpLastBody[]; client = HttpClientOpen[\"https://api.example.com\"]; cfg = HttpClientConfigure[client, 9000, true, 6, \"oaf-http/1.0\"]; retryCfg = HttpClientConfigureRetry[client, 2, 100]; proxyCfg = HttpClientConfigureProxy[client, \"\"]; defs = HttpClientDefaultHeaders[client, \"Authorization: Bearer t\"]; resp = HttpClientSend[client, \"/search\", 0, \"\", \"Accept: application/json\"]; sentCount = HttpClientRequestsSent[client]; retryCount = HttpClientRetriesUsed[client]; closed = HttpClientClose[client];";
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        TestAssertions.False(parser.Diagnostics.HasErrors, "Expected HTTP intrinsic constructor expressions to parse.");
        TestAssertions.Equal(21, unit.Statements.Count);

        var bodyDecl = unit.Statements[0] as VariableDeclarationStatementSyntax;
        TestAssertions.True(bodyDecl is not null, "Expected first statement to be declaration.");
        TestAssertions.True(bodyDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpGet constructor expression.");
        var bodyCtor = (ConstructorExpressionSyntax)bodyDecl.Initializer;
        TestAssertions.Equal("HttpGet", bodyCtor.TargetType.Name);
        TestAssertions.Equal(1, bodyCtor.Arguments.Count);

        var sendDecl = unit.Statements[1] as VariableDeclarationStatementSyntax;
        TestAssertions.True(sendDecl is not null, "Expected second statement to be declaration.");
        TestAssertions.True(sendDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpSend constructor expression.");
        var sendCtor = (ConstructorExpressionSyntax)sendDecl.Initializer;
        TestAssertions.Equal("HttpSend", sendCtor.TargetType.Name);
        TestAssertions.Equal(5, sendCtor.Arguments.Count);

        var statusDecl = unit.Statements[2] as VariableDeclarationStatementSyntax;
        TestAssertions.True(statusDecl is not null, "Expected third statement to be declaration.");
        TestAssertions.True(statusDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpLastStatus constructor expression.");
        var statusCtor = (ConstructorExpressionSyntax)statusDecl.Initializer;
        TestAssertions.Equal("HttpLastStatus", statusCtor.TargetType.Name);
        TestAssertions.Equal(0, statusCtor.Arguments.Count);

        var errorDecl = unit.Statements[3] as VariableDeclarationStatementSyntax;
        TestAssertions.True(errorDecl is not null, "Expected fourth statement to be declaration.");
        TestAssertions.True(errorDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpLastError constructor expression.");
        var errorCtor = (ConstructorExpressionSyntax)errorDecl.Initializer;
        TestAssertions.Equal("HttpLastError", errorCtor.TargetType.Name);
        TestAssertions.Equal(0, errorCtor.Arguments.Count);

        var reasonDecl = unit.Statements[4] as VariableDeclarationStatementSyntax;
        TestAssertions.True(reasonDecl is not null, "Expected fifth statement to be declaration.");
        TestAssertions.True(reasonDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpLastReason constructor expression.");
        var reasonCtor = (ConstructorExpressionSyntax)reasonDecl.Initializer;
        TestAssertions.Equal("HttpLastReason", reasonCtor.TargetType.Name);
        TestAssertions.Equal(0, reasonCtor.Arguments.Count);

        var contentTypeDecl = unit.Statements[5] as VariableDeclarationStatementSyntax;
        TestAssertions.True(contentTypeDecl is not null, "Expected sixth statement to be declaration.");
        TestAssertions.True(contentTypeDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpLastContentType constructor expression.");
        var contentTypeCtor = (ConstructorExpressionSyntax)contentTypeDecl.Initializer;
        TestAssertions.Equal("HttpLastContentType", contentTypeCtor.TargetType.Name);
        TestAssertions.Equal(0, contentTypeCtor.Arguments.Count);

        var headersDecl = unit.Statements[6] as VariableDeclarationStatementSyntax;
        TestAssertions.True(headersDecl is not null, "Expected seventh statement to be declaration.");
        TestAssertions.True(headersDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpLastHeaders constructor expression.");
        var headersCtor = (ConstructorExpressionSyntax)headersDecl.Initializer;
        TestAssertions.Equal("HttpLastHeaders", headersCtor.TargetType.Name);
        TestAssertions.Equal(0, headersCtor.Arguments.Count);

        var headerDecl = unit.Statements[7] as VariableDeclarationStatementSyntax;
        TestAssertions.True(headerDecl is not null, "Expected eighth statement to be declaration.");
        TestAssertions.True(headerDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpLastHeader constructor expression.");
        var headerCtor = (ConstructorExpressionSyntax)headerDecl.Initializer;
        TestAssertions.Equal("HttpLastHeader", headerCtor.TargetType.Name);
        TestAssertions.Equal(1, headerCtor.Arguments.Count);

        var headerBuilderDecl = unit.Statements[8] as VariableDeclarationStatementSyntax;
        TestAssertions.True(headerBuilderDecl is not null, "Expected ninth statement to be declaration.");
        TestAssertions.True(headerBuilderDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpHeader constructor expression.");
        var headerBuilderCtor = (ConstructorExpressionSyntax)headerBuilderDecl.Initializer;
        TestAssertions.Equal("HttpHeader", headerBuilderCtor.TargetType.Name);
        TestAssertions.Equal(3, headerBuilderCtor.Arguments.Count);

        var queryDecl = unit.Statements[9] as VariableDeclarationStatementSyntax;
        TestAssertions.True(queryDecl is not null, "Expected tenth statement to be declaration.");
        TestAssertions.True(queryDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpQuery constructor expression.");
        var queryCtor = (ConstructorExpressionSyntax)queryDecl.Initializer;
        TestAssertions.Equal("HttpQuery", queryCtor.TargetType.Name);
        TestAssertions.Equal(3, queryCtor.Arguments.Count);

        var encodeDecl = unit.Statements[10] as VariableDeclarationStatementSyntax;
        TestAssertions.True(encodeDecl is not null, "Expected eleventh statement to be declaration.");
        TestAssertions.True(encodeDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpUrlEncode constructor expression.");
        var encodeCtor = (ConstructorExpressionSyntax)encodeDecl.Initializer;
        TestAssertions.Equal("HttpUrlEncode", encodeCtor.TargetType.Name);
        TestAssertions.Equal(1, encodeCtor.Arguments.Count);

        var lastBodyDecl = unit.Statements[11] as VariableDeclarationStatementSyntax;
        TestAssertions.True(lastBodyDecl is not null, "Expected twelfth statement to be declaration.");
        TestAssertions.True(lastBodyDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpLastBody constructor expression.");
        var lastBodyCtor = (ConstructorExpressionSyntax)lastBodyDecl.Initializer;
        TestAssertions.Equal("HttpLastBody", lastBodyCtor.TargetType.Name);
        TestAssertions.Equal(0, lastBodyCtor.Arguments.Count);

        var clientDecl = unit.Statements[12] as VariableDeclarationStatementSyntax;
        TestAssertions.True(clientDecl is not null, "Expected thirteenth statement to be declaration.");
        TestAssertions.True(clientDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpClientOpen constructor expression.");
        var clientCtor = (ConstructorExpressionSyntax)clientDecl.Initializer;
        TestAssertions.Equal("HttpClientOpen", clientCtor.TargetType.Name);
        TestAssertions.Equal(1, clientCtor.Arguments.Count);

        var configureDecl = unit.Statements[13] as VariableDeclarationStatementSyntax;
        TestAssertions.True(configureDecl is not null, "Expected fourteenth statement to be declaration.");
        TestAssertions.True(configureDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpClientConfigure constructor expression.");
        var configureCtor = (ConstructorExpressionSyntax)configureDecl.Initializer;
        TestAssertions.Equal("HttpClientConfigure", configureCtor.TargetType.Name);
        TestAssertions.Equal(5, configureCtor.Arguments.Count);

        var retryConfigDecl = unit.Statements[14] as VariableDeclarationStatementSyntax;
        TestAssertions.True(retryConfigDecl is not null, "Expected fifteenth statement to be declaration.");
        TestAssertions.True(retryConfigDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpClientConfigureRetry constructor expression.");
        var retryConfigCtor = (ConstructorExpressionSyntax)retryConfigDecl.Initializer;
        TestAssertions.Equal("HttpClientConfigureRetry", retryConfigCtor.TargetType.Name);
        TestAssertions.Equal(3, retryConfigCtor.Arguments.Count);

        var proxyConfigDecl = unit.Statements[15] as VariableDeclarationStatementSyntax;
        TestAssertions.True(proxyConfigDecl is not null, "Expected sixteenth statement to be declaration.");
        TestAssertions.True(proxyConfigDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpClientConfigureProxy constructor expression.");
        var proxyConfigCtor = (ConstructorExpressionSyntax)proxyConfigDecl.Initializer;
        TestAssertions.Equal("HttpClientConfigureProxy", proxyConfigCtor.TargetType.Name);
        TestAssertions.Equal(2, proxyConfigCtor.Arguments.Count);

        var defaultsDecl = unit.Statements[16] as VariableDeclarationStatementSyntax;
        TestAssertions.True(defaultsDecl is not null, "Expected seventeenth statement to be declaration.");
        TestAssertions.True(defaultsDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpClientDefaultHeaders constructor expression.");
        var defaultsCtor = (ConstructorExpressionSyntax)defaultsDecl.Initializer;
        TestAssertions.Equal("HttpClientDefaultHeaders", defaultsCtor.TargetType.Name);
        TestAssertions.Equal(2, defaultsCtor.Arguments.Count);

        var clientSendDecl = unit.Statements[17] as VariableDeclarationStatementSyntax;
        TestAssertions.True(clientSendDecl is not null, "Expected eighteenth statement to be declaration.");
        TestAssertions.True(clientSendDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpClientSend constructor expression.");
        var clientSendCtor = (ConstructorExpressionSyntax)clientSendDecl.Initializer;
        TestAssertions.Equal("HttpClientSend", clientSendCtor.TargetType.Name);
        TestAssertions.Equal(5, clientSendCtor.Arguments.Count);

        var requestsDecl = unit.Statements[18] as VariableDeclarationStatementSyntax;
        TestAssertions.True(requestsDecl is not null, "Expected nineteenth statement to be declaration.");
        TestAssertions.True(requestsDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpClientRequestsSent constructor expression.");
        var requestsCtor = (ConstructorExpressionSyntax)requestsDecl.Initializer;
        TestAssertions.Equal("HttpClientRequestsSent", requestsCtor.TargetType.Name);
        TestAssertions.Equal(1, requestsCtor.Arguments.Count);

        var retriesDecl = unit.Statements[19] as VariableDeclarationStatementSyntax;
        TestAssertions.True(retriesDecl is not null, "Expected twentieth statement to be declaration.");
        TestAssertions.True(retriesDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpClientRetriesUsed constructor expression.");
        var retriesCtor = (ConstructorExpressionSyntax)retriesDecl.Initializer;
        TestAssertions.Equal("HttpClientRetriesUsed", retriesCtor.TargetType.Name);
        TestAssertions.Equal(1, retriesCtor.Arguments.Count);

        var closeDecl = unit.Statements[20] as VariableDeclarationStatementSyntax;
        TestAssertions.True(closeDecl is not null, "Expected twenty-first statement to be declaration.");
        TestAssertions.True(closeDecl!.Initializer is ConstructorExpressionSyntax, "Expected HttpClientClose constructor expression.");
        var closeCtor = (ConstructorExpressionSyntax)closeDecl.Initializer;
        TestAssertions.Equal("HttpClientClose", closeCtor.TargetType.Name);
        TestAssertions.Equal(1, closeCtor.Arguments.Count);
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
