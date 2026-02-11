namespace Oaf.Frontend.Compiler.AST;

public readonly record struct SourceSpan(int Line, int Column, int Length)
{
    public static SourceSpan Unknown { get; } = new(0, 0, 1);
}

public abstract class SyntaxNode
{
    protected SyntaxNode(SourceSpan span)
    {
        Span = span;
    }

    public SourceSpan Span { get; }

    public abstract SyntaxNodeKind Kind { get; }
}

public abstract class StatementSyntax : SyntaxNode
{
    protected StatementSyntax(SourceSpan span)
        : base(span)
    {
    }
}

public abstract class ExpressionSyntax : SyntaxNode
{
    protected ExpressionSyntax(SourceSpan span)
        : base(span)
    {
    }
}
