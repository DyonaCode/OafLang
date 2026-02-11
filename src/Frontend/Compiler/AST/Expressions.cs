using Oaf.Frontend.Compiler.Lexer;

namespace Oaf.Frontend.Compiler.AST;

public sealed class LiteralExpressionSyntax : ExpressionSyntax
{
    public LiteralExpressionSyntax(object? value, SourceSpan span)
        : base(span)
    {
        Value = value;
    }

    public object? Value { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.LiteralExpression;
}

public sealed class NameExpressionSyntax : ExpressionSyntax
{
    public NameExpressionSyntax(string identifier, SourceSpan span)
        : base(span)
    {
        Identifier = identifier;
    }

    public string Identifier { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.NameExpression;
}

public sealed class CastExpressionSyntax : ExpressionSyntax
{
    public CastExpressionSyntax(TypeReferenceSyntax targetType, ExpressionSyntax expression, SourceSpan span)
        : base(span)
    {
        TargetType = targetType;
        Expression = expression;
    }

    public TypeReferenceSyntax TargetType { get; }

    public ExpressionSyntax Expression { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.CastExpression;
}

public sealed class UnaryExpressionSyntax : ExpressionSyntax
{
    public UnaryExpressionSyntax(TokenKind operatorKind, ExpressionSyntax operand, SourceSpan span)
        : base(span)
    {
        OperatorKind = operatorKind;
        Operand = operand;
    }

    public TokenKind OperatorKind { get; }

    public ExpressionSyntax Operand { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.UnaryExpression;
}

public sealed class BinaryExpressionSyntax : ExpressionSyntax
{
    public BinaryExpressionSyntax(ExpressionSyntax left, TokenKind operatorKind, ExpressionSyntax right, SourceSpan span)
        : base(span)
    {
        Left = left;
        OperatorKind = operatorKind;
        Right = right;
    }

    public ExpressionSyntax Left { get; }

    public TokenKind OperatorKind { get; }

    public ExpressionSyntax Right { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.BinaryExpression;
}

public sealed class ParenthesizedExpressionSyntax : ExpressionSyntax
{
    public ParenthesizedExpressionSyntax(ExpressionSyntax expression, SourceSpan span)
        : base(span)
    {
        Expression = expression;
    }

    public ExpressionSyntax Expression { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.ParenthesizedExpression;
}
