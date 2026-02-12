using Oaf.Frontend.Compiler.Lexer;
using Oaf.Tests.Framework;

namespace Oaf.Tests.Unit.Lexer;

public static class LexerTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("tokenizes_keywords_operators_and_literals", TokenizesKeywordsOperatorsAndLiterals),
            ("skips_single_and_multiline_comments", SkipsComments),
            ("reports_unterminated_string", ReportsUnterminatedString),
            ("tokenizes_module_and_import_keywords", TokenizesModuleAndImportKeywords)
        ];
    }

    private static void TokenizesKeywordsOperatorsAndLiterals()
    {
        const string source = "flux total = 0x10 + 0b11; if true => return \"ok\";;";
        var lexer = new Frontend.Compiler.Lexer.Lexer(source);
        var tokens = lexer.Lex().Select(token => token.Kind).ToList();

        var expected = new[]
        {
            TokenKind.FluxKeyword,
            TokenKind.IdentifierToken,
            TokenKind.EqualsToken,
            TokenKind.NumberToken,
            TokenKind.PlusToken,
            TokenKind.NumberToken,
            TokenKind.SemicolonToken,
            TokenKind.IfKeyword,
            TokenKind.TrueKeyword,
            TokenKind.FatArrowToken,
            TokenKind.ReturnKeyword,
            TokenKind.StringToken,
            TokenKind.SemicolonToken,
            TokenKind.SemicolonToken,
            TokenKind.EndOfFileToken
        };

        TestAssertions.SequenceEqual(expected, tokens);
        TestAssertions.False(lexer.Diagnostics.HasErrors, "Lexer reported unexpected diagnostics.");
    }

    private static void SkipsComments()
    {
        const string source = "x = 1; // inline\n/# block #/ y = 2;";
        var lexer = new Frontend.Compiler.Lexer.Lexer(source);
        var tokens = lexer.Lex().Select(token => token.Kind).ToArray();

        var expected = new[]
        {
            TokenKind.IdentifierToken,
            TokenKind.EqualsToken,
            TokenKind.NumberToken,
            TokenKind.SemicolonToken,
            TokenKind.IdentifierToken,
            TokenKind.EqualsToken,
            TokenKind.NumberToken,
            TokenKind.SemicolonToken,
            TokenKind.EndOfFileToken
        };

        TestAssertions.SequenceEqual(expected, tokens);
    }

    private static void ReportsUnterminatedString()
    {
        const string source = "name = \"unterminated";
        var lexer = new Frontend.Compiler.Lexer.Lexer(source);
        _ = lexer.Lex();

        TestAssertions.True(lexer.Diagnostics.HasErrors, "Expected lexer diagnostics for unterminated string.");
    }

    private static void TokenizesModuleAndImportKeywords()
    {
        const string source = "module pkg.math; import pkg.core;";
        var lexer = new Frontend.Compiler.Lexer.Lexer(source);
        var tokens = lexer.Lex().Select(static token => token.Kind).ToArray();

        var expected = new[]
        {
            TokenKind.ModuleKeyword,
            TokenKind.IdentifierToken,
            TokenKind.DotToken,
            TokenKind.IdentifierToken,
            TokenKind.SemicolonToken,
            TokenKind.ImportKeyword,
            TokenKind.IdentifierToken,
            TokenKind.DotToken,
            TokenKind.IdentifierToken,
            TokenKind.SemicolonToken,
            TokenKind.EndOfFileToken
        };

        TestAssertions.SequenceEqual(expected, tokens);
    }
}
