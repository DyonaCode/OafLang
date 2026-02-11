using System.Globalization;
using System.Text;
using Oaf.Frontend.Compiler.Diagnostics;

namespace Oaf.Frontend.Compiler.Lexer;

public sealed class Lexer
{
    private readonly string _text;
    private int _position;
    private int _line = 1;
    private int _column = 1;

    public Lexer(string text)
    {
        _text = text ?? string.Empty;
    }

    public DiagnosticBag Diagnostics { get; } = new();

    public IReadOnlyList<Token> Lex()
    {
        var tokens = new List<Token>();

        while (!IsAtEnd)
        {
            if (SkipTrivia())
            {
                continue;
            }

            var token = LexToken();
            if (token.Kind != TokenKind.BadToken)
            {
                tokens.Add(token);
            }
        }

        tokens.Add(new Token(TokenKind.EndOfFileToken, string.Empty, null, _line, _column));
        return tokens;
    }

    private Token LexToken()
    {
        var start = _position;
        var line = _line;
        var column = _column;

        if (char.IsLetter(Current) || Current == '_')
        {
            return ReadIdentifierOrKeyword(start, line, column);
        }

        if (char.IsDigit(Current))
        {
            return ReadNumber(start, line, column);
        }

        if (Current == '"')
        {
            return ReadString(start, line, column, raw: false, interpolated: false);
        }

        if (Current == '@' && Peek(1) == '"')
        {
            return ReadString(start, line, column, raw: true, interpolated: false);
        }

        if (Current == '$' && Peek(1) == '"')
        {
            return ReadString(start, line, column, raw: false, interpolated: true);
        }

        if (Current == '\'')
        {
            return ReadChar(start, line, column);
        }

        if (TryReadOperator(start, line, column, out var token))
        {
            return token;
        }

        Diagnostics.ReportLexerError($"Unrecognized character '{Current}'.", line, column, 1);
        Advance();
        return new Token(TokenKind.BadToken, _text[start.._position], null, line, column);
    }

    private bool SkipTrivia()
    {
        if (char.IsWhiteSpace(Current))
        {
            Advance();
            return true;
        }

        if (Current == '/' && Peek(1) == '/')
        {
            SkipSingleLineComment();
            return true;
        }

        if (Current == '#')
        {
            SkipSingleLineComment();
            return true;
        }

        if (Current == '/' && Peek(1) == '#')
        {
            SkipBlockComment('/','#');
            return true;
        }

        if (Current == '@' && Peek(1) == '#')
        {
            SkipBlockComment('@','#');
            return true;
        }

        return false;
    }

    private void SkipSingleLineComment()
    {
        while (!IsAtEnd && Current != '\n' && Current != '\r')
        {
            Advance();
        }
    }

    private void SkipBlockComment(char openFirst, char openSecond)
    {
        var startLine = _line;
        var startColumn = _column;

        Advance(); // open first char
        Advance(); // open second char

        var closingFirst = '#';
        var closingSecond = openFirst == '/' ? '/' : '@';

        while (!IsAtEnd)
        {
            if (Current == closingFirst && Peek(1) == closingSecond)
            {
                Advance();
                Advance();
                return;
            }

            Advance();
        }

        Diagnostics.ReportLexerError("Unterminated block comment.", startLine, startColumn, 2);
    }

    private Token ReadIdentifierOrKeyword(int start, int line, int column)
    {
        while (char.IsLetterOrDigit(Current) || Current == '_')
        {
            Advance();
        }

        var text = _text[start.._position];
        var kind = KeywordLookup.GetIdentifierKind(text);
        object? value = kind switch
        {
            TokenKind.TrueKeyword => true,
            TokenKind.FalseKeyword => false,
            _ => null
        };

        return new Token(kind, text, value, line, column);
    }

    private Token ReadNumber(int start, int line, int column)
    {
        var isFloat = false;
        var isHex = false;
        var isBinary = false;

        if (Current == '0' && (Peek(1) == 'x' || Peek(1) == 'X'))
        {
            isHex = true;
            Advance();
            Advance();

            var digitStart = _position;
            while (IsHexDigit(Current))
            {
                Advance();
            }

            if (_position == digitStart)
            {
                Diagnostics.ReportLexerError("Expected hexadecimal digits after 0x prefix.", line, column, 2);
            }
        }
        else if (Current == '0' && (Peek(1) == 'b' || Peek(1) == 'B'))
        {
            isBinary = true;
            Advance();
            Advance();

            var digitStart = _position;
            while (Current == '0' || Current == '1')
            {
                Advance();
            }

            if (_position == digitStart)
            {
                Diagnostics.ReportLexerError("Expected binary digits after 0b prefix.", line, column, 2);
            }
        }
        else
        {
            while (char.IsDigit(Current))
            {
                Advance();
            }

            if (Current == '.' && char.IsDigit(Peek(1)))
            {
                isFloat = true;
                Advance(); // dot

                while (char.IsDigit(Current))
                {
                    Advance();
                }
            }
        }

        var text = _text[start.._position];
        object? value = null;

        if (isHex)
        {
            var digits = text.Length > 2 ? text[2..] : string.Empty;
            if (long.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
            }
            else
            {
                Diagnostics.ReportLexerError($"Invalid hexadecimal literal '{text}'.", line, column, text.Length);
            }
        }
        else if (isBinary)
        {
            var digits = text.Length > 2 ? text[2..] : string.Empty;
            try
            {
                value = Convert.ToInt64(digits, 2);
            }
            catch
            {
                Diagnostics.ReportLexerError($"Invalid binary literal '{text}'.", line, column, text.Length);
            }
        }
        else if (isFloat)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
            }
            else
            {
                Diagnostics.ReportLexerError($"Invalid floating point literal '{text}'.", line, column, text.Length);
            }
        }
        else
        {
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
            }
            else
            {
                Diagnostics.ReportLexerError($"Invalid integer literal '{text}'.", line, column, text.Length);
            }
        }

        return new Token(TokenKind.NumberToken, text, value, line, column);
    }

    private Token ReadString(int start, int line, int column, bool raw, bool interpolated)
    {
        if (raw)
        {
            Advance(); // @
            Advance(); // "
        }
        else if (interpolated)
        {
            Advance(); // $
            Advance(); // "
        }
        else
        {
            Advance(); // "
        }

        var builder = new StringBuilder();
        var terminated = false;

        while (!IsAtEnd)
        {
            if (Current == '"')
            {
                if (raw && Peek(1) == '"')
                {
                    builder.Append('"');
                    Advance();
                    Advance();
                    continue;
                }

                Advance();
                terminated = true;
                break;
            }

            if (!raw && Current == '\\')
            {
                Advance();
                builder.Append(ReadEscapedCharacter(line, column));
                continue;
            }

            if (Current == '\n' || Current == '\r')
            {
                break;
            }

            builder.Append(Current);
            Advance();
        }

        if (!terminated)
        {
            Diagnostics.ReportLexerError("Unterminated string literal.", line, column, Math.Max(_position - start, 1));
        }

        var text = _text[start.._position];
        return new Token(TokenKind.StringToken, text, builder.ToString(), line, column);
    }

    private Token ReadChar(int start, int line, int column)
    {
        Advance(); // opening '

        char value;
        if (IsAtEnd || Current == '\n' || Current == '\r')
        {
            Diagnostics.ReportLexerError("Unterminated char literal.", line, column, Math.Max(_position - start, 1));
            return new Token(TokenKind.CharToken, _text[start.._position], '\0', line, column);
        }

        if (Current == '\\')
        {
            Advance();
            value = ReadEscapedCharacter(line, column);
        }
        else
        {
            value = Current;
            Advance();
        }

        if (Current != '\'')
        {
            Diagnostics.ReportLexerError("Char literal must contain exactly one character.", line, column, Math.Max(_position - start, 1));
        }
        else
        {
            Advance(); // closing '
        }

        var text = _text[start.._position];
        return new Token(TokenKind.CharToken, text, value, line, column);
    }

    private char ReadEscapedCharacter(int line, int column)
    {
        if (IsAtEnd)
        {
            Diagnostics.ReportLexerError("Invalid escape sequence at end of source.", line, column, 1);
            return '\0';
        }

        var value = Current switch
        {
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            '\\' => '\\',
            '"' => '"',
            '\'' => '\'',
            _ => '\0'
        };

        if (value == '\0')
        {
            Diagnostics.ReportLexerError($"Unsupported escape sequence '\\{Current}'.", line, column, 2);
        }

        Advance();
        return value;
    }

    private bool TryReadOperator(int start, int line, int column, out Token token)
    {
        if (TryReadMultiCharacterToken(start, line, column, out token))
        {
            return true;
        }

        token = Current switch
        {
            '(' => CreateAndAdvance(TokenKind.OpenParenToken, start, line, column),
            ')' => CreateAndAdvance(TokenKind.CloseParenToken, start, line, column),
            '[' => CreateAndAdvance(TokenKind.OpenBracketToken, start, line, column),
            ']' => CreateAndAdvance(TokenKind.CloseBracketToken, start, line, column),
            '{' => CreateAndAdvance(TokenKind.OpenBraceToken, start, line, column),
            '}' => CreateAndAdvance(TokenKind.CloseBraceToken, start, line, column),
            ',' => CreateAndAdvance(TokenKind.CommaToken, start, line, column),
            '.' => CreateAndAdvance(TokenKind.DotToken, start, line, column),
            ':' => CreateAndAdvance(TokenKind.ColonToken, start, line, column),
            ';' => CreateAndAdvance(TokenKind.SemicolonToken, start, line, column),
            '+' => CreateAndAdvance(TokenKind.PlusToken, start, line, column),
            '-' => CreateAndAdvance(TokenKind.MinusToken, start, line, column),
            '*' => CreateAndAdvance(TokenKind.StarToken, start, line, column),
            '/' => CreateAndAdvance(TokenKind.SlashToken, start, line, column),
            '%' => CreateAndAdvance(TokenKind.PercentToken, start, line, column),
            '^' => CreateAndAdvance(TokenKind.CaretToken, start, line, column),
            '~' => CreateAndAdvance(TokenKind.TildeToken, start, line, column),
            '&' => CreateAndAdvance(TokenKind.AmpersandToken, start, line, column),
            '|' => CreateAndAdvance(TokenKind.PipeToken, start, line, column),
            '!' => CreateAndAdvance(TokenKind.BangToken, start, line, column),
            '=' => CreateAndAdvance(TokenKind.EqualsToken, start, line, column),
            '<' => CreateAndAdvance(TokenKind.LessToken, start, line, column),
            '>' => CreateAndAdvance(TokenKind.GreaterToken, start, line, column),
            _ => default!
        };

        return token is not null;
    }

    private bool TryReadMultiCharacterToken(int start, int line, int column, out Token token)
    {
        // Longest-match first so operators like +<< win over + and <<.
        var candidates = new (string text, TokenKind kind)[]
        {
            ("+<<", TokenKind.UnsignedShiftLeftToken),
            ("+>>", TokenKind.UnsignedShiftRightToken),
            ("=>", TokenKind.FatArrowToken),
            ("->", TokenKind.ArrowToken),
            ("<-", TokenKind.BindToken),
            ("==", TokenKind.EqualsEqualsToken),
            ("!=", TokenKind.BangEqualsToken),
            ("<=", TokenKind.LessOrEqualsToken),
            (">=", TokenKind.GreaterOrEqualsToken),
            ("<<", TokenKind.ShiftLeftToken),
            (">>", TokenKind.ShiftRightToken),
            ("+=", TokenKind.PlusEqualsToken),
            ("-=", TokenKind.MinusEqualsToken),
            ("*=", TokenKind.StarEqualsToken),
            ("/=", TokenKind.SlashEqualsToken),
            ("&&", TokenKind.DoubleAmpersandToken),
            ("||", TokenKind.DoublePipeToken),
            ("!|", TokenKind.BangPipeToken),
            ("!&", TokenKind.BangAmpersandToken),
            ("^&", TokenKind.CaretAmpersandToken),
            ("/^", TokenKind.RootToken)
        };

        foreach (var (text, kind) in candidates)
        {
            if (!Matches(text))
            {
                continue;
            }

            Advance(text.Length);
            token = new Token(kind, text, null, line, column);
            return true;
        }

        token = null!;
        return false;
    }

    private Token CreateAndAdvance(TokenKind kind, int start, int line, int column)
    {
        Advance();
        return new Token(kind, _text[start.._position], null, line, column);
    }

    private bool Matches(string text)
    {
        if (_position + text.Length > _text.Length)
        {
            return false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (_text[_position + i] != text[i])
            {
                return false;
            }
        }

        return true;
    }

    private bool IsHexDigit(char value)
    {
        return char.IsDigit(value) || (value >= 'a' && value <= 'f') || (value >= 'A' && value <= 'F');
    }

    private char Current => Peek(0);

    private char Peek(int offset)
    {
        var index = _position + offset;
        return index >= _text.Length ? '\0' : _text[index];
    }

    private bool IsAtEnd => _position >= _text.Length;

    private void Advance(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            if (IsAtEnd)
            {
                return;
            }

            if (_text[_position] == '\r')
            {
                _position++;
                if (!IsAtEnd && _text[_position] == '\n')
                {
                    _position++;
                }

                _line++;
                _column = 1;
                continue;
            }

            if (_text[_position] == '\n')
            {
                _position++;
                _line++;
                _column = 1;
                continue;
            }

            _position++;
            _column++;
        }
    }
}
