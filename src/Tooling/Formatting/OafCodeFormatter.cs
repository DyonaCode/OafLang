using System.Text;
using Oaf.Frontend.Compiler.Lexer;

namespace Oaf.Tooling.Formatting;

public static class OafCodeFormatter
{
    public static string Format(string source, int indentSize = 4)
    {
        if (source is null)
        {
            return string.Empty;
        }

        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');

        var builder = new StringBuilder();
        var indentLevel = 0;
        var previousWasBlank = false;

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0)
            {
                if (!previousWasBlank)
                {
                    builder.AppendLine();
                }

                previousWasBlank = true;
                continue;
            }

            previousWasBlank = false;

            var (codeSegment, commentSegment) = SplitInlineComment(trimmed);
            var formattedCode = FormatCodeSegment(codeSegment);
            var leadingCloseBraces = CountLeadingClosingBraces(formattedCode);
            var lineIndent = Math.Max(0, indentLevel - leadingCloseBraces);

            builder.Append(' ', lineIndent * indentSize);
            if (formattedCode.Length > 0)
            {
                builder.Append(formattedCode);
            }

            if (commentSegment.Length > 0)
            {
                if (formattedCode.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(commentSegment);
            }

            builder.AppendLine();

            indentLevel += CountOpenBraces(formattedCode) - CountCloseBraces(formattedCode);
            if (indentLevel < 0)
            {
                indentLevel = 0;
            }
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string FormatCodeSegment(string codeSegment)
    {
        if (string.IsNullOrWhiteSpace(codeSegment))
        {
            return string.Empty;
        }

        if (codeSegment.Contains("/#", StringComparison.Ordinal)
            || codeSegment.Contains("#/", StringComparison.Ordinal)
            || codeSegment.Contains("@#", StringComparison.Ordinal)
            || codeSegment.Contains("#@", StringComparison.Ordinal))
        {
            return codeSegment.Trim();
        }

        var lexer = new Lexer(codeSegment);
        var tokens = lexer.Lex()
            .Where(token => token.Kind != TokenKind.EndOfFileToken && token.Kind != TokenKind.BadToken)
            .ToArray();

        if (tokens.Length == 0)
        {
            return codeSegment.Trim();
        }

        var builder = new StringBuilder();
        Token? previous = null;
        foreach (var token in tokens)
        {
            if (previous is not null && NeedsSpaceBetween(previous, token))
            {
                builder.Append(' ');
            }

            builder.Append(token.Text);
            previous = token;
        }

        return builder.ToString().Trim();
    }

    private static bool NeedsSpaceBetween(Token previous, Token current)
    {
        if (IsNoSpaceAfter(previous.Kind))
        {
            return false;
        }

        if (IsNoSpaceBefore(current.Kind))
        {
            return false;
        }

        if (current.Kind == TokenKind.OpenParenToken
            && (previous.Kind == TokenKind.IdentifierToken
                || previous.Kind == TokenKind.CloseParenToken
                || previous.Kind == TokenKind.NumberToken
                || previous.Kind == TokenKind.StringToken
                || previous.Kind == TokenKind.CharToken))
        {
            return false;
        }

        if (IsOperator(previous.Kind) || IsOperator(current.Kind))
        {
            return true;
        }

        return true;
    }

    private static bool IsNoSpaceBefore(TokenKind kind)
    {
        return kind is TokenKind.CommaToken
            or TokenKind.SemicolonToken
            or TokenKind.ColonToken
            or TokenKind.DotToken
            or TokenKind.CloseParenToken
            or TokenKind.CloseBracketToken
            or TokenKind.CloseBraceToken;
    }

    private static bool IsNoSpaceAfter(TokenKind kind)
    {
        return kind is TokenKind.OpenParenToken
            or TokenKind.OpenBracketToken
            or TokenKind.OpenBraceToken
            or TokenKind.DotToken;
    }

    private static bool IsOperator(TokenKind kind)
    {
        return kind is TokenKind.PlusToken
            or TokenKind.MinusToken
            or TokenKind.StarToken
            or TokenKind.SlashToken
            or TokenKind.PercentToken
            or TokenKind.CaretToken
            or TokenKind.AmpersandToken
            or TokenKind.PipeToken
            or TokenKind.DoubleAmpersandToken
            or TokenKind.DoublePipeToken
            or TokenKind.BangToken
            or TokenKind.BangPipeToken
            or TokenKind.BangAmpersandToken
            or TokenKind.CaretAmpersandToken
            or TokenKind.EqualsToken
            or TokenKind.PlusEqualsToken
            or TokenKind.MinusEqualsToken
            or TokenKind.StarEqualsToken
            or TokenKind.SlashEqualsToken
            or TokenKind.EqualsEqualsToken
            or TokenKind.BangEqualsToken
            or TokenKind.LessToken
            or TokenKind.LessOrEqualsToken
            or TokenKind.GreaterToken
            or TokenKind.GreaterOrEqualsToken
            or TokenKind.ShiftLeftToken
            or TokenKind.ShiftRightToken
            or TokenKind.UnsignedShiftLeftToken
            or TokenKind.UnsignedShiftRightToken
            or TokenKind.ArrowToken
            or TokenKind.FatArrowToken
            or TokenKind.BindToken;
    }

    private static (string Code, string Comment) SplitInlineComment(string line)
    {
        var inString = false;
        var inChar = false;

        for (var i = 0; i < line.Length; i++)
        {
            var current = line[i];
            var next = i + 1 < line.Length ? line[i + 1] : '\0';

            if (current == '"' && !inChar && (i == 0 || line[i - 1] != '\\'))
            {
                inString = !inString;
                continue;
            }

            if (current == '\'' && !inString && (i == 0 || line[i - 1] != '\\'))
            {
                inChar = !inChar;
                continue;
            }

            if (inString || inChar)
            {
                continue;
            }

            if (current == '/' && next == '/')
            {
                return (line[..i].TrimEnd(), line[i..].TrimEnd());
            }

            if (current == '#')
            {
                return (line[..i].TrimEnd(), line[i..].TrimEnd());
            }
        }

        return (line.TrimEnd(), string.Empty);
    }

    private static int CountLeadingClosingBraces(string code)
    {
        var count = 0;
        foreach (var ch in code)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            if (ch == '}')
            {
                count++;
                continue;
            }

            break;
        }

        return count;
    }

    private static int CountOpenBraces(string code)
    {
        return CountBraces(code, '{');
    }

    private static int CountCloseBraces(string code)
    {
        return CountBraces(code, '}');
    }

    private static int CountBraces(string code, char target)
    {
        var count = 0;
        var inString = false;
        var inChar = false;

        for (var i = 0; i < code.Length; i++)
        {
            var ch = code[i];
            if (ch == '"' && !inChar && (i == 0 || code[i - 1] != '\\'))
            {
                inString = !inString;
                continue;
            }

            if (ch == '\'' && !inString && (i == 0 || code[i - 1] != '\\'))
            {
                inChar = !inChar;
                continue;
            }

            if (inString || inChar)
            {
                continue;
            }

            if (ch == target)
            {
                count++;
            }
        }

        return count;
    }
}
