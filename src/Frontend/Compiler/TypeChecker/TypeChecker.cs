using Oaf.Frontend.Compiler.AST;
using Oaf.Frontend.Compiler.Diagnostics;
using Oaf.Frontend.Compiler.Lexer;
using Oaf.Frontend.Compiler.Symbols;

namespace Oaf.Frontend.Compiler.TypeChecker;

public sealed class TypeChecker
{
    private enum ConversionKind
    {
        None,
        Identity,
        ImplicitNumericWidening,
        ExplicitNumeric
    }

    private readonly DiagnosticBag _diagnostics;
    private readonly Dictionary<string, UserDefinedTypeSymbol> _userDefinedTypes = new(StringComparer.Ordinal);
    private int _loopDepth;

    public TypeChecker(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public SymbolTable Symbols { get; } = new();

    public void Check(CompilationUnitSyntax compilationUnit)
    {
        CollectTypeDeclarations(compilationUnit);
        ValidateTypeDeclarations(compilationUnit);

        foreach (var statement in compilationUnit.Statements)
        {
            if (statement is TypeDeclarationStatementSyntax)
            {
                continue;
            }

            CheckStatement(statement);
        }
    }

    private void CollectTypeDeclarations(CompilationUnitSyntax compilationUnit)
    {
        foreach (var declaration in compilationUnit.Statements.OfType<TypeDeclarationStatementSyntax>())
        {
            var genericParameters = new List<GenericTypeParameterSymbol>();
            var seenNames = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0; index < declaration.TypeParameters.Count; index++)
            {
                var parameterName = declaration.TypeParameters[index];
                if (!seenNames.Add(parameterName))
                {
                    ReportTypeError($"Type '{declaration.Name}' contains duplicate generic parameter '{parameterName}'.", declaration);
                    continue;
                }

                genericParameters.Add(new GenericTypeParameterSymbol(parameterName, index));
            }

            var kind = declaration switch
            {
                StructDeclarationStatementSyntax => UserDefinedTypeKind.Struct,
                ClassDeclarationStatementSyntax => UserDefinedTypeKind.Class,
                EnumDeclarationStatementSyntax => UserDefinedTypeKind.Enum,
                _ => UserDefinedTypeKind.Struct
            };

            var symbol = new UserDefinedTypeSymbol(declaration.Name, kind, genericParameters);

            if (!Symbols.TryDeclareType(symbol))
            {
                ReportTypeError($"Type '{declaration.Name}' is already declared.", declaration);
                continue;
            }

            _userDefinedTypes[declaration.Name] = symbol;
        }
    }

    private void ValidateTypeDeclarations(CompilationUnitSyntax compilationUnit)
    {
        foreach (var declaration in compilationUnit.Statements.OfType<TypeDeclarationStatementSyntax>())
        {
            if (!_userDefinedTypes.TryGetValue(declaration.Name, out var typeSymbol))
            {
                continue;
            }

            var genericScope = typeSymbol.TypeParameters.ToDictionary(
                parameter => parameter.Name,
                parameter => (TypeSymbol)parameter,
                StringComparer.Ordinal);

            switch (declaration)
            {
                case StructDeclarationStatementSyntax structDeclaration:
                    typeSymbol.SetFields(BindFieldDeclarations(structDeclaration.Fields, genericScope));
                    break;

                case ClassDeclarationStatementSyntax classDeclaration:
                    typeSymbol.SetFields(BindFieldDeclarations(classDeclaration.Fields, genericScope));
                    break;

                case EnumDeclarationStatementSyntax enumDeclaration:
                    typeSymbol.SetVariants(BindEnumVariants(enumDeclaration.Variants, genericScope));
                    break;
            }
        }
    }

    private IReadOnlyList<FieldSymbol> BindFieldDeclarations(
        IReadOnlyList<FieldDeclarationSyntax> fieldDeclarations,
        IReadOnlyDictionary<string, TypeSymbol> genericScope)
    {
        var fields = new List<FieldSymbol>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fieldDeclaration in fieldDeclarations)
        {
            if (!seenNames.Add(fieldDeclaration.Name))
            {
                ReportTypeError($"Field '{fieldDeclaration.Name}' is declared multiple times.", fieldDeclaration);
                continue;
            }

            var fieldType = ResolveTypeReference(fieldDeclaration.Type, genericScope);
            if (IsOpenGenericDefinition(fieldType))
            {
                ReportTypeError($"Type '{fieldType.Name}' requires type arguments in this context.", fieldDeclaration.Type);
                fieldType = PrimitiveTypeSymbol.Error;
            }

            fields.Add(new FieldSymbol(fieldDeclaration.Name, fieldType));
        }

        return fields;
    }

    private IReadOnlyList<EnumVariantSymbol> BindEnumVariants(
        IReadOnlyList<EnumVariantSyntax> variants,
        IReadOnlyDictionary<string, TypeSymbol> genericScope)
    {
        var boundVariants = new List<EnumVariantSymbol>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var variant in variants)
        {
            if (!seenNames.Add(variant.Name))
            {
                ReportTypeError($"Enum variant '{variant.Name}' is declared multiple times.", variant);
                continue;
            }

            TypeSymbol? payloadType = null;
            if (variant.PayloadType is not null)
            {
                payloadType = ResolveTypeReference(variant.PayloadType, genericScope);
                if (IsOpenGenericDefinition(payloadType))
                {
                    ReportTypeError($"Type '{payloadType.Name}' requires type arguments in enum variant payload.", variant.PayloadType);
                    payloadType = PrimitiveTypeSymbol.Error;
                }
            }

            boundVariants.Add(new EnumVariantSymbol(variant.Name, payloadType));
        }

        return boundVariants;
    }

    private TypeSymbol ResolveTypeReference(
        TypeReferenceSyntax typeReference,
        IReadOnlyDictionary<string, TypeSymbol>? genericScope = null)
    {
        if (genericScope is not null && genericScope.TryGetValue(typeReference.Name, out var genericTypeParameter))
        {
            if (typeReference.TypeArguments.Count > 0)
            {
                ReportTypeError($"Generic parameter '{typeReference.Name}' cannot have type arguments.", typeReference);
                return PrimitiveTypeSymbol.Error;
            }

            return genericTypeParameter;
        }

        if (!Symbols.TryLookupType(typeReference.Name, out var resolvedType) || resolvedType is null)
        {
            ReportTypeError($"Unknown type '{typeReference.Name}'.", typeReference);
            return PrimitiveTypeSymbol.Error;
        }

        if (typeReference.TypeArguments.Count == 0)
        {
            return resolvedType;
        }

        if (resolvedType is not UserDefinedTypeSymbol genericDefinition || genericDefinition.TypeParameters.Count == 0)
        {
            ReportTypeError($"Type '{typeReference.Name}' is not generic and cannot accept type arguments.", typeReference);
            return PrimitiveTypeSymbol.Error;
        }

        if (typeReference.TypeArguments.Count != genericDefinition.TypeParameters.Count)
        {
            ReportTypeError(
                $"Type '{genericDefinition.Name}' expects {genericDefinition.TypeParameters.Count} type argument(s) but got {typeReference.TypeArguments.Count}.",
                typeReference);
            return PrimitiveTypeSymbol.Error;
        }

        var arguments = new List<TypeSymbol>();
        foreach (var argumentSyntax in typeReference.TypeArguments)
        {
            arguments.Add(ResolveTypeReference(argumentSyntax, genericScope));
        }

        return new ConstructedTypeSymbol(genericDefinition, arguments);
    }

    private void CheckStatement(StatementSyntax statement)
    {
        switch (statement)
        {
            case BlockStatementSyntax block:
                Symbols.EnterScope();
                foreach (var nested in block.Statements)
                {
                    CheckStatement(nested);
                }

                Symbols.ExitScope();
                break;

            case VariableDeclarationStatementSyntax declaration:
                CheckVariableDeclaration(declaration);
                break;

            case AssignmentStatementSyntax assignment:
                CheckAssignment(assignment);
                break;

            case ExpressionStatementSyntax expression:
                CheckExpression(expression.Expression);
                break;

            case ReturnStatementSyntax returnStatement:
                if (returnStatement.Expression is not null)
                {
                    CheckExpression(returnStatement.Expression);
                }

                break;

            case IfStatementSyntax ifStatement:
                var conditionType = CheckExpression(ifStatement.Condition);
                if (!AreSameType(conditionType, PrimitiveTypeSymbol.Bool) && !AreSameType(conditionType, PrimitiveTypeSymbol.Error))
                {
                    ReportTypeError("If condition must evaluate to bool.", ifStatement.Condition);
                }

                CheckStatement(ifStatement.ThenStatement);
                if (ifStatement.ElseStatement is not null)
                {
                    CheckStatement(ifStatement.ElseStatement);
                }

                break;

            case LoopStatementSyntax loopStatement:
                var loopExpressionType = CheckExpression(loopStatement.IteratorOrCondition);
                if (!AreSameType(loopExpressionType, PrimitiveTypeSymbol.Int)
                    && !AreSameType(loopExpressionType, PrimitiveTypeSymbol.Bool)
                    && !AreSameType(loopExpressionType, PrimitiveTypeSymbol.Error))
                {
                    ReportTypeError("Loop condition/iterator must be int or bool in current implementation.", loopStatement.IteratorOrCondition);
                }

                Symbols.EnterScope();
                _loopDepth++;

                if (!string.IsNullOrWhiteSpace(loopStatement.IterationVariable))
                {
                    Symbols.TryDeclare(new VariableSymbol(loopStatement.IterationVariable, PrimitiveTypeSymbol.Int, isMutable: false));
                }

                CheckStatement(loopStatement.Body);

                _loopDepth--;
                Symbols.ExitScope();
                break;

            case BreakStatementSyntax breakStatement:
                if (_loopDepth == 0)
                {
                    ReportTypeError("'break' can only be used inside a loop.", breakStatement);
                }

                break;

            case ContinueStatementSyntax continueStatement:
                if (_loopDepth == 0)
                {
                    ReportTypeError("'continue' can only be used inside a loop.", continueStatement);
                }

                break;

            case TypeDeclarationStatementSyntax:
                break;
        }
    }

    private void CheckVariableDeclaration(VariableDeclarationStatementSyntax declaration)
    {
        var initializerType = CheckExpression(declaration.Initializer);
        var variableType = initializerType;

        if (declaration.DeclaredType is not null)
        {
            variableType = ResolveTypeReference(declaration.DeclaredType);

            if (IsOpenGenericDefinition(variableType))
            {
                ReportTypeError($"Type '{variableType.Name}' requires explicit type arguments.", declaration.DeclaredType);
                variableType = PrimitiveTypeSymbol.Error;
            }

            var conversion = ClassifyConversion(initializerType, variableType, isExplicit: false);
            if (conversion == ConversionKind.None)
            {
                ReportTypeError(
                    $"Cannot assign value of type '{initializerType}' to variable '{declaration.Identifier}' of type '{variableType}'.",
                    declaration);
            }
        }

        if (Symbols.IsDeclaredInCurrentScope(declaration.Identifier))
        {
            ReportTypeError($"Variable '{declaration.Identifier}' is already declared in this scope.", declaration);
            return;
        }

        Symbols.TryDeclare(new VariableSymbol(declaration.Identifier, variableType, declaration.IsMutable));
    }

    private void CheckAssignment(AssignmentStatementSyntax assignment)
    {
        if (!Symbols.TryLookup(assignment.Identifier, out var symbol) || symbol is null)
        {
            ReportTypeError($"Variable '{assignment.Identifier}' is not declared.", assignment);
            return;
        }

        if (!symbol.IsMutable)
        {
            ReportTypeError($"Variable '{assignment.Identifier}' is immutable and cannot be assigned.", assignment);
            return;
        }

        var expressionType = CheckExpression(assignment.Expression);

        if (assignment.OperatorKind != TokenKind.EqualsToken)
        {
            if (!IsNumeric(symbol.Type) || !IsNumeric(expressionType))
            {
                ReportTypeError($"Operator '{assignment.OperatorKind}' requires numeric operands.", assignment);
            }

            return;
        }

        var conversion = ClassifyConversion(expressionType, symbol.Type, isExplicit: false);
        if (conversion == ConversionKind.None)
        {
            ReportTypeError($"Cannot assign value of type '{expressionType}' to variable '{symbol.Name}' of type '{symbol.Type}'.", assignment);
        }
    }

    private TypeSymbol CheckExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal => PrimitiveTypeSymbol.FromLiteral(literal.Value),
            NameExpressionSyntax name => LookupName(name),
            CastExpressionSyntax cast => CheckCastExpression(cast),
            UnaryExpressionSyntax unary => CheckUnaryExpression(unary),
            BinaryExpressionSyntax binary => CheckBinaryExpression(binary),
            ParenthesizedExpressionSyntax parenthesized => CheckExpression(parenthesized.Expression),
            _ => PrimitiveTypeSymbol.Error
        };
    }

    private TypeSymbol LookupName(NameExpressionSyntax nameExpression)
    {
        if (Symbols.TryLookup(nameExpression.Identifier, out var symbol) && symbol is not null)
        {
            return symbol.Type;
        }

        ReportTypeError($"Variable '{nameExpression.Identifier}' is not declared.", nameExpression);
        return PrimitiveTypeSymbol.Error;
    }

    private TypeSymbol CheckCastExpression(CastExpressionSyntax castExpression)
    {
        var sourceType = CheckExpression(castExpression.Expression);
        var targetType = ResolveTypeReference(castExpression.TargetType);

        if (IsOpenGenericDefinition(targetType))
        {
            ReportTypeError($"Type '{targetType.Name}' requires explicit type arguments.", castExpression.TargetType);
            return PrimitiveTypeSymbol.Error;
        }

        var conversion = ClassifyConversion(sourceType, targetType, isExplicit: true);
        if (conversion == ConversionKind.None)
        {
            ReportTypeError($"Cannot cast from '{sourceType}' to '{targetType}'.", castExpression);
            return PrimitiveTypeSymbol.Error;
        }

        return targetType;
    }

    private TypeSymbol CheckUnaryExpression(UnaryExpressionSyntax unary)
    {
        var operandType = CheckExpression(unary.Operand);

        return unary.OperatorKind switch
        {
            TokenKind.BangToken when AreSameType(operandType, PrimitiveTypeSymbol.Bool) => PrimitiveTypeSymbol.Bool,
            TokenKind.BangToken => ReportUnaryError("Logical not operator requires bool operand.", unary),

            TokenKind.PlusToken or TokenKind.MinusToken when IsNumeric(operandType) => PromoteNumeric(operandType, operandType),
            TokenKind.PlusToken or TokenKind.MinusToken => ReportUnaryError("Numeric unary operators require numeric operand.", unary),

            TokenKind.TildeToken when IsIntegerLike(operandType) => PrimitiveTypeSymbol.Int,
            TokenKind.TildeToken => ReportUnaryError("Bitwise complement requires integer operand.", unary),

            _ => PrimitiveTypeSymbol.Error
        };
    }

    private TypeSymbol CheckBinaryExpression(BinaryExpressionSyntax binary)
    {
        var leftType = CheckExpression(binary.Left);
        var rightType = CheckExpression(binary.Right);

        return binary.OperatorKind switch
        {
            TokenKind.PlusToken when AreSameType(leftType, PrimitiveTypeSymbol.String) || AreSameType(rightType, PrimitiveTypeSymbol.String) => PrimitiveTypeSymbol.String,

            TokenKind.PlusToken or TokenKind.MinusToken or TokenKind.StarToken or TokenKind.SlashToken or TokenKind.PercentToken or TokenKind.RootToken
                when IsNumeric(leftType) && IsNumeric(rightType)
                => PromoteNumeric(leftType, rightType),

            TokenKind.PlusToken or TokenKind.MinusToken or TokenKind.StarToken or TokenKind.SlashToken or TokenKind.PercentToken or TokenKind.RootToken
                => ReportBinaryError("Arithmetic operators require numeric operands.", binary),

            TokenKind.EqualsEqualsToken or TokenKind.BangEqualsToken
                when IsAssignable(leftType, rightType) || IsAssignable(rightType, leftType)
                => PrimitiveTypeSymbol.Bool,

            TokenKind.EqualsEqualsToken or TokenKind.BangEqualsToken
                => ReportBinaryError("Equality operators require compatible operand types.", binary),

            TokenKind.LessToken or TokenKind.LessOrEqualsToken or TokenKind.GreaterToken or TokenKind.GreaterOrEqualsToken
                when IsNumeric(leftType) && IsNumeric(rightType)
                => PrimitiveTypeSymbol.Bool,

            TokenKind.LessToken or TokenKind.LessOrEqualsToken or TokenKind.GreaterToken or TokenKind.GreaterOrEqualsToken
                => ReportBinaryError("Comparison operators require numeric operands.", binary),

            TokenKind.DoubleAmpersandToken or TokenKind.DoublePipeToken or TokenKind.AmpersandToken or TokenKind.PipeToken or TokenKind.BangPipeToken or TokenKind.BangAmpersandToken
                when AreSameType(leftType, PrimitiveTypeSymbol.Bool) && AreSameType(rightType, PrimitiveTypeSymbol.Bool)
                => PrimitiveTypeSymbol.Bool,

            TokenKind.DoubleAmpersandToken or TokenKind.DoublePipeToken or TokenKind.AmpersandToken or TokenKind.PipeToken or TokenKind.BangPipeToken or TokenKind.BangAmpersandToken
                => ReportBinaryError("Logical operators require bool operands.", binary),

            TokenKind.ShiftLeftToken or TokenKind.ShiftRightToken or TokenKind.UnsignedShiftLeftToken or TokenKind.UnsignedShiftRightToken or TokenKind.CaretToken or TokenKind.CaretAmpersandToken
                when IsIntegerLike(leftType) && IsIntegerLike(rightType)
                => PrimitiveTypeSymbol.Int,

            TokenKind.ShiftLeftToken or TokenKind.ShiftRightToken or TokenKind.UnsignedShiftLeftToken or TokenKind.UnsignedShiftRightToken or TokenKind.CaretToken or TokenKind.CaretAmpersandToken
                => ReportBinaryError("Bitwise operators require integer operands.", binary),

            _ => PrimitiveTypeSymbol.Error
        };
    }

    private TypeSymbol ReportUnaryError(string message, SyntaxNode node)
    {
        ReportTypeError(message, node);
        return PrimitiveTypeSymbol.Error;
    }

    private TypeSymbol ReportBinaryError(string message, SyntaxNode node)
    {
        ReportTypeError(message, node);
        return PrimitiveTypeSymbol.Error;
    }

    private bool IsAssignable(TypeSymbol target, TypeSymbol value)
    {
        return ClassifyConversion(value, target, isExplicit: false) != ConversionKind.None;
    }

    private ConversionKind ClassifyConversion(TypeSymbol source, TypeSymbol target, bool isExplicit)
    {
        if (AreSameType(source, PrimitiveTypeSymbol.Error) || AreSameType(target, PrimitiveTypeSymbol.Error))
        {
            return ConversionKind.Identity;
        }

        if (AreSameType(source, target))
        {
            return ConversionKind.Identity;
        }

        if (AreSameType(source, PrimitiveTypeSymbol.Char) && AreSameType(target, PrimitiveTypeSymbol.Int))
        {
            return ConversionKind.ImplicitNumericWidening;
        }

        if (AreSameType(source, PrimitiveTypeSymbol.Int) && AreSameType(target, PrimitiveTypeSymbol.Float))
        {
            return ConversionKind.ImplicitNumericWidening;
        }

        if (AreSameType(source, PrimitiveTypeSymbol.Char) && AreSameType(target, PrimitiveTypeSymbol.Float))
        {
            return ConversionKind.ImplicitNumericWidening;
        }

        if (isExplicit && IsNumeric(source) && IsNumeric(target))
        {
            return ConversionKind.ExplicitNumeric;
        }

        return ConversionKind.None;
    }

    private bool AreSameType(TypeSymbol left, TypeSymbol right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is ConstructedTypeSymbol leftConstructed && right is ConstructedTypeSymbol rightConstructed)
        {
            if (!AreSameType(leftConstructed.GenericDefinition, rightConstructed.GenericDefinition))
            {
                return false;
            }

            if (leftConstructed.TypeArguments.Count != rightConstructed.TypeArguments.Count)
            {
                return false;
            }

            for (var i = 0; i < leftConstructed.TypeArguments.Count; i++)
            {
                if (!AreSameType(leftConstructed.TypeArguments[i], rightConstructed.TypeArguments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        if (left is UserDefinedTypeSymbol leftUser && right is UserDefinedTypeSymbol rightUser)
        {
            return string.Equals(leftUser.Name, rightUser.Name, StringComparison.Ordinal)
                   && leftUser.TypeParameters.Count == rightUser.TypeParameters.Count;
        }

        if (left is GenericTypeParameterSymbol leftParameter && right is GenericTypeParameterSymbol rightParameter)
        {
            return string.Equals(leftParameter.Name, rightParameter.Name, StringComparison.Ordinal)
                   && leftParameter.Position == rightParameter.Position;
        }

        return false;
    }

    private bool IsOpenGenericDefinition(TypeSymbol type)
    {
        return type is UserDefinedTypeSymbol userType && userType.TypeParameters.Count > 0;
    }

    private bool IsNumeric(TypeSymbol type)
    {
        return AreSameType(type, PrimitiveTypeSymbol.Char)
               || AreSameType(type, PrimitiveTypeSymbol.Int)
               || AreSameType(type, PrimitiveTypeSymbol.Float);
    }

    private bool IsIntegerLike(TypeSymbol type)
    {
        return AreSameType(type, PrimitiveTypeSymbol.Char) || AreSameType(type, PrimitiveTypeSymbol.Int);
    }

    private TypeSymbol PromoteNumeric(TypeSymbol left, TypeSymbol right)
    {
        if (AreSameType(left, PrimitiveTypeSymbol.Float) || AreSameType(right, PrimitiveTypeSymbol.Float))
        {
            return PrimitiveTypeSymbol.Float;
        }

        return PrimitiveTypeSymbol.Int;
    }

    private void ReportTypeError(string message, SyntaxNode node)
    {
        var span = node.Span;
        _diagnostics.ReportTypeError(message, span.Line, span.Column, span.Length);
    }
}
