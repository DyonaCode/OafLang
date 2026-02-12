namespace Oaf.Frontend.Compiler.AST;

public abstract class SyntaxRewriter
{
    public virtual CompilationUnitSyntax RewriteCompilationUnit(CompilationUnitSyntax node)
    {
        var statements = node.Statements.Select(RewriteStatement).ToList();
        return new CompilationUnitSyntax(statements, node.Span);
    }

    public virtual StatementSyntax RewriteStatement(StatementSyntax node)
    {
        return node switch
        {
            BlockStatementSyntax block => RewriteBlockStatement(block),
            ExpressionStatementSyntax expression => RewriteExpressionStatement(expression),
            VariableDeclarationStatementSyntax declaration => RewriteVariableDeclarationStatement(declaration),
            AssignmentStatementSyntax assignment => RewriteAssignmentStatement(assignment),
            ReturnStatementSyntax returnStatement => RewriteReturnStatement(returnStatement),
            IfStatementSyntax ifStatement => RewriteIfStatement(ifStatement),
            LoopStatementSyntax loop => RewriteLoopStatement(loop),
            ModuleDeclarationStatementSyntax moduleDeclaration => RewriteModuleDeclarationStatement(moduleDeclaration),
            ImportStatementSyntax importStatement => RewriteImportStatement(importStatement),
            StructDeclarationStatementSyntax structDeclaration => RewriteStructDeclarationStatement(structDeclaration),
            ClassDeclarationStatementSyntax classDeclaration => RewriteClassDeclarationStatement(classDeclaration),
            EnumDeclarationStatementSyntax enumDeclaration => RewriteEnumDeclarationStatement(enumDeclaration),
            BreakStatementSyntax breakStatement => breakStatement,
            ContinueStatementSyntax continueStatement => continueStatement,
            _ => node
        };
    }

    public virtual ExpressionSyntax RewriteExpression(ExpressionSyntax node)
    {
        return node switch
        {
            LiteralExpressionSyntax literal => literal,
            NameExpressionSyntax name => name,
            CastExpressionSyntax cast => RewriteCastExpression(cast),
            UnaryExpressionSyntax unary => RewriteUnaryExpression(unary),
            BinaryExpressionSyntax binary => RewriteBinaryExpression(binary),
            ParenthesizedExpressionSyntax parenthesized => RewriteParenthesizedExpression(parenthesized),
            _ => node
        };
    }

    public virtual TypeReferenceSyntax RewriteTypeReference(TypeReferenceSyntax node)
    {
        var typeArguments = node.TypeArguments.Select(RewriteTypeReference).ToList();
        return new TypeReferenceSyntax(node.Name, typeArguments, node.Span);
    }

    protected virtual StatementSyntax RewriteBlockStatement(BlockStatementSyntax node)
    {
        var statements = node.Statements.Select(RewriteStatement).ToList();
        return new BlockStatementSyntax(statements, node.Span);
    }

    protected virtual StatementSyntax RewriteExpressionStatement(ExpressionStatementSyntax node)
    {
        return new ExpressionStatementSyntax(RewriteExpression(node.Expression), node.Span);
    }

    protected virtual StatementSyntax RewriteVariableDeclarationStatement(VariableDeclarationStatementSyntax node)
    {
        return new VariableDeclarationStatementSyntax(
            node.Identifier,
            node.IsMutable,
            RewriteExpression(node.Initializer),
            node.Span,
            node.DeclaredType is null ? null : RewriteTypeReference(node.DeclaredType));
    }

    protected virtual StatementSyntax RewriteAssignmentStatement(AssignmentStatementSyntax node)
    {
        return new AssignmentStatementSyntax(node.Identifier, node.OperatorKind, RewriteExpression(node.Expression), node.Span);
    }

    protected virtual StatementSyntax RewriteReturnStatement(ReturnStatementSyntax node)
    {
        return new ReturnStatementSyntax(node.Expression is null ? null : RewriteExpression(node.Expression), node.Span);
    }

    protected virtual StatementSyntax RewriteIfStatement(IfStatementSyntax node)
    {
        return new IfStatementSyntax(
            RewriteExpression(node.Condition),
            RewriteStatement(node.ThenStatement),
            node.ElseStatement is null ? null : RewriteStatement(node.ElseStatement),
            node.Span);
    }

    protected virtual StatementSyntax RewriteLoopStatement(LoopStatementSyntax node)
    {
        return new LoopStatementSyntax(
            node.IsParallel,
            RewriteExpression(node.IteratorOrCondition),
            node.IterationVariable,
            RewriteStatement(node.Body),
            node.Span);
    }

    protected virtual StatementSyntax RewriteModuleDeclarationStatement(ModuleDeclarationStatementSyntax node)
    {
        return new ModuleDeclarationStatementSyntax(node.ModuleName, node.Span);
    }

    protected virtual StatementSyntax RewriteImportStatement(ImportStatementSyntax node)
    {
        return new ImportStatementSyntax(node.ModuleName, node.Span);
    }

    protected virtual StatementSyntax RewriteStructDeclarationStatement(StructDeclarationStatementSyntax node)
    {
        var fields = node.Fields
            .Select(field => new FieldDeclarationSyntax(RewriteTypeReference(field.Type), field.Name, field.Span))
            .ToList();

        return new StructDeclarationStatementSyntax(node.Name, node.TypeParameters.ToList(), fields, node.Span);
    }

    protected virtual StatementSyntax RewriteClassDeclarationStatement(ClassDeclarationStatementSyntax node)
    {
        var fields = node.Fields
            .Select(field => new FieldDeclarationSyntax(RewriteTypeReference(field.Type), field.Name, field.Span))
            .ToList();

        return new ClassDeclarationStatementSyntax(node.Name, node.TypeParameters.ToList(), fields, node.Span);
    }

    protected virtual StatementSyntax RewriteEnumDeclarationStatement(EnumDeclarationStatementSyntax node)
    {
        var variants = node.Variants
            .Select(variant => new EnumVariantSyntax(
                variant.Name,
                variant.PayloadType is null ? null : RewriteTypeReference(variant.PayloadType),
                variant.Span))
            .ToList();

        return new EnumDeclarationStatementSyntax(node.Name, node.TypeParameters.ToList(), variants, node.Span);
    }

    protected virtual ExpressionSyntax RewriteUnaryExpression(UnaryExpressionSyntax node)
    {
        return new UnaryExpressionSyntax(node.OperatorKind, RewriteExpression(node.Operand), node.Span);
    }

    protected virtual ExpressionSyntax RewriteCastExpression(CastExpressionSyntax node)
    {
        return new CastExpressionSyntax(
            RewriteTypeReference(node.TargetType),
            RewriteExpression(node.Expression),
            node.Span);
    }

    protected virtual ExpressionSyntax RewriteBinaryExpression(BinaryExpressionSyntax node)
    {
        return new BinaryExpressionSyntax(
            RewriteExpression(node.Left),
            node.OperatorKind,
            RewriteExpression(node.Right),
            node.Span);
    }

    protected virtual ExpressionSyntax RewriteParenthesizedExpression(ParenthesizedExpressionSyntax node)
    {
        return new ParenthesizedExpressionSyntax(RewriteExpression(node.Expression), node.Span);
    }
}
