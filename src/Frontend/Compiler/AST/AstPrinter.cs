using System.Text;
using Oaf.Frontend.Compiler.Lexer;

namespace Oaf.Frontend.Compiler.AST;

public static class AstPrinter
{
    public static string Print(SyntaxNode node)
    {
        var builder = new StringBuilder();
        WriteNode(builder, node, string.Empty, isLast: true);
        return builder.ToString();
    }

    private static void WriteNode(StringBuilder builder, SyntaxNode node, string indent, bool isLast)
    {
        builder.Append(indent);
        builder.Append(isLast ? "`--" : "|--");
        builder.Append(node.Kind);

        switch (node)
        {
            case LiteralExpressionSyntax literal:
                builder.Append(" ");
                builder.Append(literal.Value ?? "null");
                break;
            case NameExpressionSyntax name:
                builder.Append(" ");
                builder.Append(name.Identifier);
                break;
            case CastExpressionSyntax cast:
                builder.Append(" ");
                builder.Append(TypeToText(cast.TargetType));
                break;
            case VariableDeclarationStatementSyntax declaration:
                builder.Append(declaration.IsMutable ? " flux " : " ");
                builder.Append(declaration.Identifier);
                if (declaration.DeclaredType is not null)
                {
                    builder.Append(" : ");
                    builder.Append(TypeToText(declaration.DeclaredType));
                }

                break;
            case AssignmentStatementSyntax assignment:
                builder.Append(" ");
                builder.Append(assignment.Identifier);
                builder.Append(" ");
                builder.Append(TokenToText(assignment.OperatorKind));
                break;
            case UnaryExpressionSyntax unary:
                builder.Append(" ");
                builder.Append(TokenToText(unary.OperatorKind));
                break;
            case BinaryExpressionSyntax binary:
                builder.Append(" ");
                builder.Append(TokenToText(binary.OperatorKind));
                break;
            case LoopStatementSyntax loop:
                builder.Append(loop.IsParallel ? " paralloop" : " loop");
                if (!string.IsNullOrWhiteSpace(loop.IterationVariable))
                {
                    builder.Append(" ");
                    builder.Append(loop.IterationVariable);
                }

                break;
            case ModuleDeclarationStatementSyntax moduleDeclaration:
                builder.Append(" ");
                builder.Append(moduleDeclaration.ModuleName);
                break;
            case ImportStatementSyntax importStatement:
                builder.Append(" ");
                builder.Append(importStatement.ModuleName);
                break;
            case TypeReferenceSyntax typeReference:
                builder.Append(" ");
                builder.Append(TypeToText(typeReference));
                break;
            case FieldDeclarationSyntax field:
                builder.Append(" ");
                builder.Append(field.Name);
                break;
            case EnumVariantSyntax variant:
                builder.Append(" ");
                builder.Append(variant.Name);
                break;
            case TypeDeclarationStatementSyntax typeDeclaration:
                builder.Append(" ");
                builder.Append(typeDeclaration.Name);
                if (typeDeclaration.TypeParameters.Count > 0)
                {
                    builder.Append("<");
                    builder.Append(string.Join(", ", typeDeclaration.TypeParameters));
                    builder.Append(">");
                }

                break;
        }

        builder.AppendLine();

        var children = GetChildren(node).ToList();
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var childIsLast = i == children.Count - 1;
            WriteNode(builder, child, indent + (isLast ? "   " : "|  "), childIsLast);
        }
    }

    private static IEnumerable<SyntaxNode> GetChildren(SyntaxNode node)
    {
        return node switch
        {
            CompilationUnitSyntax compilation => compilation.Statements,
            BlockStatementSyntax block => block.Statements,
            ExpressionStatementSyntax expression => [expression.Expression],
            VariableDeclarationStatementSyntax declaration when declaration.DeclaredType is not null => [declaration.DeclaredType, declaration.Initializer],
            VariableDeclarationStatementSyntax declaration => [declaration.Initializer],
            AssignmentStatementSyntax assignment => [assignment.Expression],
            ReturnStatementSyntax returnStatement when returnStatement.Expression is not null => [returnStatement.Expression],
            IfStatementSyntax ifStatement when ifStatement.ElseStatement is not null => [ifStatement.Condition, ifStatement.ThenStatement, ifStatement.ElseStatement],
            IfStatementSyntax ifStatement => [ifStatement.Condition, ifStatement.ThenStatement],
            LoopStatementSyntax loop => [loop.IteratorOrCondition, loop.Body],
            ModuleDeclarationStatementSyntax => [],
            ImportStatementSyntax => [],
            StructDeclarationStatementSyntax structDeclaration => structDeclaration.Fields,
            ClassDeclarationStatementSyntax classDeclaration => classDeclaration.Fields,
            EnumDeclarationStatementSyntax enumDeclaration => enumDeclaration.Variants,
            FieldDeclarationSyntax field => [field.Type],
            EnumVariantSyntax enumVariant when enumVariant.PayloadType is not null => [enumVariant.PayloadType],
            TypeReferenceSyntax typeReference => typeReference.TypeArguments,
            CastExpressionSyntax cast => [cast.TargetType, cast.Expression],
            UnaryExpressionSyntax unary => [unary.Operand],
            BinaryExpressionSyntax binary => [binary.Left, binary.Right],
            ParenthesizedExpressionSyntax parenthesized => [parenthesized.Expression],
            _ => []
        };
    }

    private static string TypeToText(TypeReferenceSyntax typeReference)
    {
        if (typeReference.TypeArguments.Count == 0)
        {
            return typeReference.Name;
        }

        return $"{typeReference.Name}<{string.Join(", ", typeReference.TypeArguments.Select(TypeToText))}>";
    }

    private static string TokenToText(TokenKind kind)
    {
        return kind switch
        {
            TokenKind.PlusToken => "+",
            TokenKind.MinusToken => "-",
            TokenKind.StarToken => "*",
            TokenKind.SlashToken => "/",
            TokenKind.PercentToken => "%",
            TokenKind.CaretToken => "^",
            TokenKind.RootToken => "/^",
            TokenKind.EqualsToken => "=",
            TokenKind.PlusEqualsToken => "+=",
            TokenKind.MinusEqualsToken => "-=",
            TokenKind.StarEqualsToken => "*=",
            TokenKind.SlashEqualsToken => "/=",
            TokenKind.EqualsEqualsToken => "==",
            TokenKind.BangEqualsToken => "!=",
            TokenKind.LessToken => "<",
            TokenKind.LessOrEqualsToken => "<=",
            TokenKind.GreaterToken => ">",
            TokenKind.GreaterOrEqualsToken => ">=",
            TokenKind.AmpersandToken => "&",
            TokenKind.PipeToken => "|",
            TokenKind.DoubleAmpersandToken => "&&",
            TokenKind.DoublePipeToken => "||",
            TokenKind.BangPipeToken => "!|",
            TokenKind.BangAmpersandToken => "!&",
            TokenKind.BangToken => "!",
            TokenKind.CaretAmpersandToken => "^&",
            TokenKind.ShiftLeftToken => "<<",
            TokenKind.ShiftRightToken => ">>",
            TokenKind.UnsignedShiftLeftToken => "+<<",
            TokenKind.UnsignedShiftRightToken => "+>>",
            _ => kind.ToString()
        };
    }
}
