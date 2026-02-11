namespace Oaf.Frontend.Compiler.Lexer;

public sealed class Token
{
    public Token(TokenKind kind, string text, object? value, int line, int column)
    {
        Kind = kind;
        Text = text;
        Value = value;
        Line = line;
        Column = column;
    }

    public TokenKind Kind { get; }

    public string Text { get; }

    public object? Value { get; }

    public int Line { get; }

    public int Column { get; }

    public override string ToString()
    {
        return $"{Kind} '{Text}' ({Line},{Column})";
    }
}
