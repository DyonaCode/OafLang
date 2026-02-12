using Oaf.Frontend.Compiler.Lexer;

namespace Oaf.Frontend.Compiler.AST;

public sealed class BlockStatementSyntax : StatementSyntax
{
    public BlockStatementSyntax(IReadOnlyList<StatementSyntax> statements, SourceSpan span)
        : base(span)
    {
        Statements = statements;
    }

    public IReadOnlyList<StatementSyntax> Statements { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.BlockStatement;
}

public sealed class ExpressionStatementSyntax : StatementSyntax
{
    public ExpressionStatementSyntax(ExpressionSyntax expression, SourceSpan span)
        : base(span)
    {
        Expression = expression;
    }

    public ExpressionSyntax Expression { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.ExpressionStatement;
}

public sealed class VariableDeclarationStatementSyntax : StatementSyntax
{
    public VariableDeclarationStatementSyntax(string identifier, bool isMutable, ExpressionSyntax initializer, SourceSpan span, TypeReferenceSyntax? declaredType = null)
        : base(span)
    {
        Identifier = identifier;
        IsMutable = isMutable;
        Initializer = initializer;
        DeclaredType = declaredType;
    }

    public string Identifier { get; }

    public bool IsMutable { get; }

    public ExpressionSyntax Initializer { get; }

    public TypeReferenceSyntax? DeclaredType { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.VariableDeclarationStatement;
}

public sealed class AssignmentStatementSyntax : StatementSyntax
{
    public AssignmentStatementSyntax(string identifier, TokenKind operatorKind, ExpressionSyntax expression, SourceSpan span)
        : base(span)
    {
        Identifier = identifier;
        OperatorKind = operatorKind;
        Expression = expression;
    }

    public string Identifier { get; }

    public TokenKind OperatorKind { get; }

    public ExpressionSyntax Expression { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.AssignmentStatement;
}

public sealed class ReturnStatementSyntax : StatementSyntax
{
    public ReturnStatementSyntax(ExpressionSyntax? expression, SourceSpan span)
        : base(span)
    {
        Expression = expression;
    }

    public ExpressionSyntax? Expression { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.ReturnStatement;
}

public sealed class IfStatementSyntax : StatementSyntax
{
    public IfStatementSyntax(ExpressionSyntax condition, StatementSyntax thenStatement, StatementSyntax? elseStatement, SourceSpan span)
        : base(span)
    {
        Condition = condition;
        ThenStatement = thenStatement;
        ElseStatement = elseStatement;
    }

    public ExpressionSyntax Condition { get; }

    public StatementSyntax ThenStatement { get; }

    public StatementSyntax? ElseStatement { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.IfStatement;
}

public sealed class LoopStatementSyntax : StatementSyntax
{
    public LoopStatementSyntax(bool isParallel, ExpressionSyntax iteratorOrCondition, string? iterationVariable, StatementSyntax body, SourceSpan span)
        : base(span)
    {
        IsParallel = isParallel;
        IteratorOrCondition = iteratorOrCondition;
        IterationVariable = iterationVariable;
        Body = body;
    }

    public bool IsParallel { get; }

    public ExpressionSyntax IteratorOrCondition { get; }

    public string? IterationVariable { get; }

    public StatementSyntax Body { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.LoopStatement;
}

public sealed class BreakStatementSyntax : StatementSyntax
{
    public BreakStatementSyntax(SourceSpan span)
        : base(span)
    {
    }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.BreakStatement;
}

public sealed class ContinueStatementSyntax : StatementSyntax
{
    public ContinueStatementSyntax(SourceSpan span)
        : base(span)
    {
    }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.ContinueStatement;
}

public sealed class ModuleDeclarationStatementSyntax : StatementSyntax
{
    public ModuleDeclarationStatementSyntax(string moduleName, SourceSpan span)
        : base(span)
    {
        ModuleName = moduleName;
    }

    public string ModuleName { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.ModuleDeclarationStatement;
}

public sealed class ImportStatementSyntax : StatementSyntax
{
    public ImportStatementSyntax(string moduleName, SourceSpan span)
        : base(span)
    {
        ModuleName = moduleName;
    }

    public string ModuleName { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.ImportStatement;
}
