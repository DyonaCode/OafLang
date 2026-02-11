namespace Oaf.Frontend.Compiler.AST;

public sealed class TypeReferenceSyntax : SyntaxNode
{
    public TypeReferenceSyntax(string name, IReadOnlyList<TypeReferenceSyntax> typeArguments, SourceSpan span)
        : base(span)
    {
        Name = name;
        TypeArguments = typeArguments;
    }

    public string Name { get; }

    public IReadOnlyList<TypeReferenceSyntax> TypeArguments { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.TypeReference;
}

public sealed class FieldDeclarationSyntax : SyntaxNode
{
    public FieldDeclarationSyntax(TypeReferenceSyntax type, string name, SourceSpan span)
        : base(span)
    {
        Type = type;
        Name = name;
    }

    public TypeReferenceSyntax Type { get; }

    public string Name { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.FieldDeclaration;
}

public sealed class EnumVariantSyntax : SyntaxNode
{
    public EnumVariantSyntax(string name, TypeReferenceSyntax? payloadType, SourceSpan span)
        : base(span)
    {
        Name = name;
        PayloadType = payloadType;
    }

    public string Name { get; }

    public TypeReferenceSyntax? PayloadType { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.EnumVariant;
}

public abstract class TypeDeclarationStatementSyntax : StatementSyntax
{
    protected TypeDeclarationStatementSyntax(string name, IReadOnlyList<string> typeParameters, SourceSpan span)
        : base(span)
    {
        Name = name;
        TypeParameters = typeParameters;
    }

    public string Name { get; }

    public IReadOnlyList<string> TypeParameters { get; }
}

public sealed class StructDeclarationStatementSyntax : TypeDeclarationStatementSyntax
{
    public StructDeclarationStatementSyntax(string name, IReadOnlyList<string> typeParameters, IReadOnlyList<FieldDeclarationSyntax> fields, SourceSpan span)
        : base(name, typeParameters, span)
    {
        Fields = fields;
    }

    public IReadOnlyList<FieldDeclarationSyntax> Fields { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.StructDeclarationStatement;
}

public sealed class ClassDeclarationStatementSyntax : TypeDeclarationStatementSyntax
{
    public ClassDeclarationStatementSyntax(string name, IReadOnlyList<string> typeParameters, IReadOnlyList<FieldDeclarationSyntax> fields, SourceSpan span)
        : base(name, typeParameters, span)
    {
        Fields = fields;
    }

    public IReadOnlyList<FieldDeclarationSyntax> Fields { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.ClassDeclarationStatement;
}

public sealed class EnumDeclarationStatementSyntax : TypeDeclarationStatementSyntax
{
    public EnumDeclarationStatementSyntax(string name, IReadOnlyList<string> typeParameters, IReadOnlyList<EnumVariantSyntax> variants, SourceSpan span)
        : base(name, typeParameters, span)
    {
        Variants = variants;
    }

    public IReadOnlyList<EnumVariantSyntax> Variants { get; }

    public override SyntaxNodeKind Kind => SyntaxNodeKind.EnumDeclarationStatement;
}
