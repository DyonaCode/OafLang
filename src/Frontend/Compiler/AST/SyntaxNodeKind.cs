namespace Oaf.Frontend.Compiler.AST;

public enum SyntaxNodeKind
{
    CompilationUnit,
    BlockStatement,
    ExpressionStatement,
    VariableDeclarationStatement,
    AssignmentStatement,
    ReturnStatement,
    IfStatement,
    LoopStatement,
    BreakStatement,
    ContinueStatement,
    ModuleDeclarationStatement,
    ImportStatement,

    StructDeclarationStatement,
    ClassDeclarationStatement,
    EnumDeclarationStatement,
    TypeReference,
    FieldDeclaration,
    EnumVariant,

    LiteralExpression,
    NameExpression,
    CastExpression,
    UnaryExpression,
    BinaryExpression,
    ParenthesizedExpression
}
