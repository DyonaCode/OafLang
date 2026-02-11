namespace Oaf.Frontend.Compiler.AST;

public sealed class CompilationUnitSyntax : SyntaxNode
{
    public CompilationUnitSyntax(IReadOnlyList<StatementSyntax> statements, SourceSpan span)
        : base(span)
    {
        Statements = statements;
    }

    public IReadOnlyList<StatementSyntax> Statements { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.CompilationUnit;
}
