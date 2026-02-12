namespace Oaf.Frontend.Compiler.Lexer;

internal static class KeywordLookup
{
    private static readonly Dictionary<string, TokenKind> Keywords =
        new(StringComparer.Ordinal)
        {
            ["if"] = TokenKind.IfKeyword,
            ["loop"] = TokenKind.LoopKeyword,
            ["paralloop"] = TokenKind.ParalloopKeyword,
            ["return"] = TokenKind.ReturnKeyword,
            ["throw"] = TokenKind.ThrowKeyword,
            ["catch"] = TokenKind.CatchKeyword,
            ["class"] = TokenKind.ClassKeyword,
            ["public"] = TokenKind.PublicKeyword,
            ["flux"] = TokenKind.FluxKeyword,
            ["enum"] = TokenKind.EnumKeyword,
            ["struct"] = TokenKind.StructKeyword,
            ["match"] = TokenKind.MatchKeyword,
            ["actor"] = TokenKind.ActorKeyword,
            ["gc"] = TokenKind.GcKeyword,
            ["base"] = TokenKind.BaseKeyword,
            ["recurse"] = TokenKind.RecurseKeyword,
            ["break"] = TokenKind.BreakKeyword,
            ["continue"] = TokenKind.ContinueKeyword,
            ["when"] = TokenKind.WhenKeyword,
            ["module"] = TokenKind.ModuleKeyword,
            ["import"] = TokenKind.ImportKeyword,
            ["true"] = TokenKind.TrueKeyword,
            ["false"] = TokenKind.FalseKeyword
        };

    public static TokenKind GetIdentifierKind(string text)
    {
        return Keywords.TryGetValue(text, out var kind) ? kind : TokenKind.IdentifierToken;
    }
}
