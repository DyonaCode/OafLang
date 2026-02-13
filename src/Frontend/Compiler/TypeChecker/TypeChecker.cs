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

    private sealed class ParallelLoopContext
    {
        public ParallelLoopContext(int scopeDepth, string iterationVariableName)
        {
            ScopeDepth = scopeDepth;
            IterationVariableName = iterationVariableName;
        }

        public int ScopeDepth { get; }

        public string IterationVariableName { get; }
    }

    private readonly DiagnosticBag _diagnostics;
    private readonly Dictionary<string, UserDefinedTypeSymbol> _userDefinedTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _importedModules = new(StringComparer.Ordinal);
    private string? _currentModule;
    private int _loopDepth;
    private readonly Stack<ParallelLoopContext> _parallelLoopContexts = new();

    public TypeChecker(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public SymbolTable Symbols { get; } = new();

    public void Check(CompilationUnitSyntax compilationUnit)
    {
        _currentModule = null;
        _importedModules.Clear();
        _parallelLoopContexts.Clear();

        CollectTypeDeclarations(compilationUnit);
        ValidateTypeDeclarations(compilationUnit);

        foreach (var statement in compilationUnit.Statements)
        {
            if (statement is ModuleDeclarationStatementSyntax moduleDeclaration)
            {
                _currentModule = moduleDeclaration.ModuleName;
                _importedModules.Clear();
                continue;
            }

            if (statement is ImportStatementSyntax importStatement)
            {
                _importedModules.Add(importStatement.ModuleName);
                continue;
            }

            if (statement is TypeDeclarationStatementSyntax)
            {
                continue;
            }

            CheckStatement(statement);
        }
    }

    private void CollectTypeDeclarations(CompilationUnitSyntax compilationUnit)
    {
        string? moduleContext = null;
        foreach (var statement in compilationUnit.Statements)
        {
            if (statement is ModuleDeclarationStatementSyntax moduleDeclaration)
            {
                moduleContext = moduleDeclaration.ModuleName;
                continue;
            }

            if (statement is not TypeDeclarationStatementSyntax declaration)
            {
                continue;
            }

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
            var qualifiedName = QualifyTopLevelName(moduleContext, declaration.Name);
            symbol = new UserDefinedTypeSymbol(qualifiedName, kind, genericParameters);

            if (!Symbols.TryDeclareType(symbol))
            {
                ReportTypeError($"Type '{qualifiedName}' is already declared.", declaration);
                continue;
            }

            _userDefinedTypes[qualifiedName] = symbol;
        }
    }

    private void ValidateTypeDeclarations(CompilationUnitSyntax compilationUnit)
    {
        string? moduleContext = null;
        foreach (var statement in compilationUnit.Statements)
        {
            if (statement is ModuleDeclarationStatementSyntax moduleDeclaration)
            {
                moduleContext = moduleDeclaration.ModuleName;
                continue;
            }

            if (statement is not TypeDeclarationStatementSyntax declaration)
            {
                continue;
            }

            var qualifiedName = QualifyTopLevelName(moduleContext, declaration.Name);
            if (!_userDefinedTypes.TryGetValue(qualifiedName, out var typeSymbol))
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
        if (string.Equals(typeReference.Name, "array", StringComparison.Ordinal))
        {
            if (typeReference.TypeArguments.Count != 1)
            {
                ReportTypeError("Array type requires exactly one element type argument.", typeReference);
                return PrimitiveTypeSymbol.Error;
            }

            var elementType = ResolveTypeReference(typeReference.TypeArguments[0], genericScope);
            return new ArrayTypeSymbol(elementType);
        }

        if (genericScope is not null && genericScope.TryGetValue(typeReference.Name, out var genericTypeParameter))
        {
            if (typeReference.TypeArguments.Count > 0)
            {
                ReportTypeError($"Generic parameter '{typeReference.Name}' cannot have type arguments.", typeReference);
                return PrimitiveTypeSymbol.Error;
            }

            return genericTypeParameter;
        }

        if (!TryLookupTypeSymbol(typeReference.Name, out var resolvedType))
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

            case IndexedAssignmentStatementSyntax indexedAssignment:
                CheckIndexedAssignment(indexedAssignment);
                break;

            case MatchStatementSyntax matchStatement:
                CheckMatchStatement(matchStatement);
                break;

            case ExpressionStatementSyntax expression:
                CheckExpression(expression.Expression);
                break;

            case ThrowStatementSyntax throwStatement:
                if (IsInsideParallelLoopBody())
                {
                    ReportTypeError("'throw' is not supported inside counted paralloop bodies.", throwStatement);
                }

                if (throwStatement.ErrorExpression is not null)
                {
                    _ = CheckExpression(throwStatement.ErrorExpression);
                }

                if (throwStatement.DetailExpression is not null)
                {
                    _ = CheckExpression(throwStatement.DetailExpression);
                }

                break;

            case GcStatementSyntax gcStatement:
                CheckStatement(gcStatement.Body);
                break;

            case ReturnStatementSyntax returnStatement:
                if (IsInsideParallelLoopBody())
                {
                    ReportTypeError("'return' is not supported inside counted paralloop bodies.", returnStatement);
                }

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
                if (IsInsideParallelLoopBody() && loopStatement.IsParallel)
                {
                    ReportTypeError("Nested counted paralloops are not currently supported.", loopStatement);
                }

                var loopExpressionType = CheckExpression(loopStatement.IteratorOrCondition);
                var isCountedParallelLoop = loopStatement.IsParallel && !string.IsNullOrWhiteSpace(loopStatement.IterationVariable);
                if (isCountedParallelLoop)
                {
                    if (!AreSameType(loopExpressionType, PrimitiveTypeSymbol.Int)
                        && !AreSameType(loopExpressionType, PrimitiveTypeSymbol.Error))
                    {
                        ReportTypeError("Counted paralloop requires an integer iteration count expression.", loopStatement.IteratorOrCondition);
                    }
                }
                else if (!AreSameType(loopExpressionType, PrimitiveTypeSymbol.Int)
                    && !AreSameType(loopExpressionType, PrimitiveTypeSymbol.Bool)
                    && !AreSameType(loopExpressionType, PrimitiveTypeSymbol.Error))
                {
                    ReportTypeError("Loop condition/iterator must be int or bool in current implementation.", loopStatement.IteratorOrCondition);
                }

                Symbols.EnterScope();
                if (!isCountedParallelLoop)
                {
                    _loopDepth++;
                }

                var enteredParallelContext = false;
                if (!string.IsNullOrWhiteSpace(loopStatement.IterationVariable))
                {
                    var iterationSymbol = new VariableSymbol(loopStatement.IterationVariable, PrimitiveTypeSymbol.Int, isMutable: false);
                    Symbols.TryDeclare(iterationSymbol);
                    if (isCountedParallelLoop)
                    {
                        _parallelLoopContexts.Push(new ParallelLoopContext(Symbols.ScopeDepth, iterationSymbol.Name));
                        enteredParallelContext = true;
                    }
                }

                CheckStatement(loopStatement.Body);

                if (enteredParallelContext)
                {
                    _parallelLoopContexts.Pop();
                }

                if (!isCountedParallelLoop)
                {
                    _loopDepth--;
                }
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

            case JotStatementSyntax jotStatement:
                if (IsInsideParallelLoopBody())
                {
                    ReportTypeError("'Jot' is not supported inside counted paralloop bodies.", jotStatement);
                }

                _ = CheckExpression(jotStatement.Expression);
                break;

            case ModuleDeclarationStatementSyntax moduleDeclaration:
                _currentModule = moduleDeclaration.ModuleName;
                _importedModules.Clear();
                break;

            case ImportStatementSyntax importStatement:
                _importedModules.Add(importStatement.ModuleName);
                break;

            case TypeDeclarationStatementSyntax:
                break;
        }
    }

    private void CheckVariableDeclaration(VariableDeclarationStatementSyntax declaration)
    {
        var initializerType = CheckExpression(declaration.Initializer);
        var variableType = initializerType;
        var symbolName = ResolveDeclarationName(declaration.Identifier);

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

        if (Symbols.IsDeclaredInCurrentScope(symbolName))
        {
            ReportTypeError($"Variable '{declaration.Identifier}' is already declared in this scope.", declaration);
            return;
        }

        Symbols.TryDeclare(new VariableSymbol(symbolName, variableType, declaration.IsMutable));

        if (TryGetAggregateFields(variableType, out var fields))
        {
            DeclareAggregateFieldSymbols(symbolName, declaration.IsMutable, fields, declaration);
        }
    }

    private void CheckAssignment(AssignmentStatementSyntax assignment)
    {
        if (!TryLookupVariableSymbolWithScopeDepth(assignment.Identifier, out var symbol, out var scopeDepth))
        {
            ReportTypeError($"Variable '{assignment.Identifier}' is not declared.", assignment);
            return;
        }

        var isOuterParallelAssignment = TryGetCurrentParallelLoopContext(out var parallelContext)
            && scopeDepth > 0
            && scopeDepth < parallelContext.ScopeDepth;

        if (isOuterParallelAssignment)
        {
            if (!symbol.IsMutable)
            {
                ReportTypeError($"Variable '{assignment.Identifier}' is immutable and cannot be assigned.", assignment);
                _ = CheckExpression(assignment.Expression);
                return;
            }

            var reductionExpressionType = CheckExpression(assignment.Expression);
            if (assignment.OperatorKind != TokenKind.PlusEqualsToken)
            {
                ReportTypeError(
                    $"Counted paralloop only supports outer variable reductions using '+=' for '{assignment.Identifier}'.",
                    assignment);
                return;
            }

            if (!AreSameType(symbol.Type, PrimitiveTypeSymbol.Int)
                && !AreSameType(symbol.Type, PrimitiveTypeSymbol.Error))
            {
                ReportTypeError(
                    $"Counted paralloop '+=' reduction currently requires int target variable '{assignment.Identifier}'.",
                    assignment);
            }

            if (!IsIntegerLike(reductionExpressionType)
                && !AreSameType(reductionExpressionType, PrimitiveTypeSymbol.Error))
            {
                ReportTypeError(
                    $"Counted paralloop '+=' reduction for '{assignment.Identifier}' requires integer contribution expression.",
                    assignment.Expression);
            }

            if (ContainsNameReference(assignment.Expression, assignment.Identifier))
            {
                ReportTypeError(
                    $"Counted paralloop '+=' reduction expression for '{assignment.Identifier}' cannot read the reduction variable itself.",
                    assignment.Expression);
            }

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

    private void CheckIndexedAssignment(IndexedAssignmentStatementSyntax indexedAssignment)
    {
        if (indexedAssignment.Target is not IndexExpressionSyntax)
        {
            ReportTypeError("Indexed assignment requires an index target.", indexedAssignment.Target);
            _ = CheckExpression(indexedAssignment.Expression);
            return;
        }

        if (TryResolveIndexedBaseVariable(indexedAssignment.Target, out var baseVariable) && !baseVariable.IsMutable)
        {
            ReportTypeError($"Variable '{baseVariable.Name}' is immutable and cannot be assigned.", indexedAssignment);
        }

        if (TryGetCurrentParallelLoopContext(out var parallelContext)
            && TryResolveIndexedBaseVariableWithScopeDepth(indexedAssignment.Target, out _, out var baseScopeDepth)
            && baseScopeDepth > 0
            && baseScopeDepth < parallelContext.ScopeDepth
            && !HasDirectIterationIndex(indexedAssignment.Target, parallelContext.IterationVariableName))
        {
            ReportTypeError(
                $"Counted paralloop indexed writes to outer storage must reference iteration variable '{parallelContext.IterationVariableName}'.",
                indexedAssignment);
        }

        var targetType = CheckExpression(indexedAssignment.Target);
        var expressionType = CheckExpression(indexedAssignment.Expression);

        if (assignmentRequiresNumeric(indexedAssignment.OperatorKind))
        {
            if (!IsNumeric(targetType) || !IsNumeric(expressionType))
            {
                ReportTypeError($"Operator '{indexedAssignment.OperatorKind}' requires numeric operands.", indexedAssignment);
            }

            return;
        }

        var conversion = ClassifyConversion(expressionType, targetType, isExplicit: false);
        if (conversion == ConversionKind.None)
        {
            ReportTypeError($"Cannot assign value of type '{expressionType}' to indexed target of type '{targetType}'.", indexedAssignment);
        }

        static bool assignmentRequiresNumeric(TokenKind kind)
        {
            return kind is TokenKind.PlusEqualsToken
                or TokenKind.MinusEqualsToken
                or TokenKind.StarEqualsToken
                or TokenKind.SlashEqualsToken;
        }
    }

    private void CheckMatchStatement(MatchStatementSyntax matchStatement)
    {
        var scrutineeType = CheckExpression(matchStatement.Expression);
        var defaultCount = 0;

        for (var index = 0; index < matchStatement.Arms.Count; index++)
        {
            var arm = matchStatement.Arms[index];
            if (arm.Pattern is null)
            {
                defaultCount++;
                CheckStatement(arm.Body);
                continue;
            }

            var patternType = CheckExpression(arm.Pattern);
            var comparable = IsAssignable(scrutineeType, patternType) || IsAssignable(patternType, scrutineeType);
            if (!comparable
                && !AreSameType(scrutineeType, PrimitiveTypeSymbol.Error)
                && !AreSameType(patternType, PrimitiveTypeSymbol.Error))
            {
                ReportTypeError(
                    $"Match arm {index + 1} pattern type '{patternType}' is not compatible with scrutinee type '{scrutineeType}'.",
                    arm.Pattern);
            }

            CheckStatement(arm.Body);
        }

        if (defaultCount > 1)
        {
            ReportTypeError("Match statement can contain at most one default arm ('->').", matchStatement);
        }
    }

    private TypeSymbol CheckExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal => PrimitiveTypeSymbol.FromLiteral(literal.Value),
            NameExpressionSyntax name => LookupName(name),
            ConstructorExpressionSyntax constructor => CheckConstructorExpression(constructor),
            ArrayLiteralExpressionSyntax arrayLiteral => CheckArrayLiteralExpression(arrayLiteral),
            IndexExpressionSyntax indexExpression => CheckIndexExpression(indexExpression),
            CastExpressionSyntax cast => CheckCastExpression(cast),
            UnaryExpressionSyntax unary => CheckUnaryExpression(unary),
            BinaryExpressionSyntax binary => CheckBinaryExpression(binary),
            ParenthesizedExpressionSyntax parenthesized => CheckExpression(parenthesized.Expression),
            _ => PrimitiveTypeSymbol.Error
        };
    }

    private TypeSymbol LookupName(NameExpressionSyntax nameExpression)
    {
        if (TryLookupVariableSymbol(nameExpression.Identifier, out var symbol))
        {
            return symbol.Type;
        }

        if (TryResolveEnumVariantType(nameExpression.Identifier, out var enumType))
        {
            return enumType;
        }

        ReportTypeError($"Variable '{nameExpression.Identifier}' is not declared.", nameExpression);
        return PrimitiveTypeSymbol.Error;
    }

    private TypeSymbol CheckConstructorExpression(ConstructorExpressionSyntax constructorExpression)
    {
        var targetType = ResolveTypeReference(constructorExpression.TargetType);
        if (AreSameType(targetType, PrimitiveTypeSymbol.Error))
        {
            foreach (var argument in constructorExpression.Arguments)
            {
                _ = CheckExpression(argument);
            }

            return PrimitiveTypeSymbol.Error;
        }

        if (IsOpenGenericDefinition(targetType))
        {
            ReportTypeError($"Type '{targetType.Name}' requires explicit type arguments.", constructorExpression.TargetType);
            foreach (var argument in constructorExpression.Arguments)
            {
                _ = CheckExpression(argument);
            }

            return PrimitiveTypeSymbol.Error;
        }

        if (!TryGetAggregateFields(targetType, out var fields))
        {
            ReportTypeError($"Type '{targetType.Name}' cannot be constructed with '[...]' syntax.", constructorExpression);
            foreach (var argument in constructorExpression.Arguments)
            {
                _ = CheckExpression(argument);
            }

            return PrimitiveTypeSymbol.Error;
        }

        if (constructorExpression.Arguments.Count != fields.Count)
        {
            ReportTypeError(
                $"Type '{targetType.Name}' expects {fields.Count} constructor argument(s) but got {constructorExpression.Arguments.Count}.",
                constructorExpression);
        }

        var sharedCount = Math.Min(constructorExpression.Arguments.Count, fields.Count);
        for (var index = 0; index < sharedCount; index++)
        {
            var argumentType = CheckExpression(constructorExpression.Arguments[index]);
            var fieldType = fields[index].Type;
            var conversion = ClassifyConversion(argumentType, fieldType, isExplicit: false);
            if (conversion == ConversionKind.None)
            {
                ReportTypeError(
                    $"Cannot assign constructor argument {index + 1} of type '{argumentType}' to field '{fields[index].Name}' of type '{fieldType}'.",
                    constructorExpression.Arguments[index]);
            }
        }

        for (var index = sharedCount; index < constructorExpression.Arguments.Count; index++)
        {
            _ = CheckExpression(constructorExpression.Arguments[index]);
        }

        return targetType;
    }

    private TypeSymbol CheckArrayLiteralExpression(ArrayLiteralExpressionSyntax arrayLiteral)
    {
        if (arrayLiteral.Elements.Count == 0)
        {
            return new ArrayTypeSymbol(PrimitiveTypeSymbol.Unit);
        }

        var elementType = CheckExpression(arrayLiteral.Elements[0]);
        for (var index = 1; index < arrayLiteral.Elements.Count; index++)
        {
            var candidateType = CheckExpression(arrayLiteral.Elements[index]);
            if (AreSameType(elementType, candidateType))
            {
                continue;
            }

            if (IsNumeric(elementType) && IsNumeric(candidateType))
            {
                elementType = PromoteNumeric(elementType, candidateType);
                continue;
            }

            if (ClassifyConversion(candidateType, elementType, isExplicit: false) != ConversionKind.None)
            {
                continue;
            }

            if (ClassifyConversion(elementType, candidateType, isExplicit: false) != ConversionKind.None)
            {
                elementType = candidateType;
                continue;
            }

            ReportTypeError(
                $"Array literal element {index + 1} has incompatible type '{candidateType}' (expected '{elementType}').",
                arrayLiteral.Elements[index]);
            elementType = PrimitiveTypeSymbol.Error;
        }

        return new ArrayTypeSymbol(elementType);
    }

    private TypeSymbol CheckIndexExpression(IndexExpressionSyntax indexExpression)
    {
        var targetType = CheckExpression(indexExpression.Target);
        var indexType = CheckExpression(indexExpression.Index);

        if (!IsIntegerLike(indexType) && !AreSameType(indexType, PrimitiveTypeSymbol.Error))
        {
            ReportTypeError("Array index must be an integer value.", indexExpression.Index);
        }

        if (targetType is ArrayTypeSymbol arrayType)
        {
            return arrayType.ElementType;
        }

        if (!AreSameType(targetType, PrimitiveTypeSymbol.Error))
        {
            ReportTypeError($"Cannot index value of type '{targetType}'.", indexExpression.Target);
        }

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

            TokenKind.DoubleAmpersandToken or TokenKind.DoublePipeToken or TokenKind.BangPipeToken or TokenKind.BangAmpersandToken
                when AreSameType(leftType, PrimitiveTypeSymbol.Bool) && AreSameType(rightType, PrimitiveTypeSymbol.Bool)
                => PrimitiveTypeSymbol.Bool,

            TokenKind.DoubleAmpersandToken or TokenKind.DoublePipeToken or TokenKind.BangPipeToken or TokenKind.BangAmpersandToken
                => ReportBinaryError("Logical operators require bool operands.", binary),

            TokenKind.AmpersandToken or TokenKind.PipeToken
                when AreSameType(leftType, PrimitiveTypeSymbol.Bool) && AreSameType(rightType, PrimitiveTypeSymbol.Bool)
                => PrimitiveTypeSymbol.Bool,

            TokenKind.AmpersandToken or TokenKind.PipeToken or TokenKind.ShiftLeftToken or TokenKind.ShiftRightToken or TokenKind.UnsignedShiftLeftToken or TokenKind.UnsignedShiftRightToken or TokenKind.CaretToken or TokenKind.CaretAmpersandToken
                when IsIntegerLike(leftType) && IsIntegerLike(rightType)
                => PrimitiveTypeSymbol.Int,

            TokenKind.AmpersandToken or TokenKind.PipeToken or TokenKind.ShiftLeftToken or TokenKind.ShiftRightToken or TokenKind.UnsignedShiftLeftToken or TokenKind.UnsignedShiftRightToken or TokenKind.CaretToken or TokenKind.CaretAmpersandToken
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

        if (left is ArrayTypeSymbol leftArray && right is ArrayTypeSymbol rightArray)
        {
            return AreSameType(leftArray.ElementType, rightArray.ElementType);
        }

        return false;
    }

    private bool TryGetAggregateFields(TypeSymbol type, out IReadOnlyList<FieldSymbol> fields)
    {
        fields = [];

        switch (type)
        {
            case UserDefinedTypeSymbol userDefined when userDefined.Kind is UserDefinedTypeKind.Struct or UserDefinedTypeKind.Class:
                fields = userDefined.Fields;
                return true;

            case ConstructedTypeSymbol constructed
                when constructed.GenericDefinition.Kind is UserDefinedTypeKind.Struct or UserDefinedTypeKind.Class:
            {
                var instantiatedFields = new List<FieldSymbol>(constructed.GenericDefinition.Fields.Count);
                foreach (var field in constructed.GenericDefinition.Fields)
                {
                    instantiatedFields.Add(new FieldSymbol(field.Name, InstantiateAggregateFieldType(field.Type, constructed)));
                }

                fields = instantiatedFields;
                return true;
            }

            default:
                return false;
        }
    }

    private static TypeSymbol InstantiateAggregateFieldType(TypeSymbol fieldType, ConstructedTypeSymbol aggregateType)
    {
        if (fieldType is GenericTypeParameterSymbol genericParameter
            && genericParameter.Position >= 0
            && genericParameter.Position < aggregateType.TypeArguments.Count)
        {
            return aggregateType.TypeArguments[genericParameter.Position];
        }

        if (fieldType is ConstructedTypeSymbol constructedField)
        {
            var resolvedArguments = constructedField.TypeArguments
                .Select(argument => InstantiateAggregateFieldType(argument, aggregateType))
                .ToList();
            return new ConstructedTypeSymbol(constructedField.GenericDefinition, resolvedArguments);
        }

        return fieldType;
    }

    private void DeclareAggregateFieldSymbols(string aggregateName, bool isMutable, IReadOnlyList<FieldSymbol> fields, SyntaxNode declarationNode)
    {
        foreach (var field in fields)
        {
            var fieldVariableName = $"{aggregateName}.{field.Name}";
            if (Symbols.IsDeclaredInCurrentScope(fieldVariableName))
            {
                ReportTypeError($"Field variable '{fieldVariableName}' is already declared in this scope.", declarationNode);
                continue;
            }

            Symbols.TryDeclare(new VariableSymbol(fieldVariableName, field.Type, isMutable));
        }
    }

    private bool TryResolveEnumVariantType(string enumVariantAccess, out TypeSymbol enumType)
    {
        enumType = PrimitiveTypeSymbol.Error;
        var separatorIndex = enumVariantAccess.LastIndexOf('.');
        if (separatorIndex <= 0 || separatorIndex == enumVariantAccess.Length - 1)
        {
            return false;
        }

        var enumTypeName = enumVariantAccess[..separatorIndex];
        var variantName = enumVariantAccess[(separatorIndex + 1)..];

        if (!TryLookupTypeSymbol(enumTypeName, out var resolvedType))
        {
            return false;
        }

        if (resolvedType is UserDefinedTypeSymbol enumDefinition && enumDefinition.Kind == UserDefinedTypeKind.Enum)
        {
            if (enumDefinition.Variants.Any(variant => string.Equals(variant.Name, variantName, StringComparison.Ordinal)))
            {
                enumType = enumDefinition;
                return true;
            }

            return false;
        }

        if (resolvedType is ConstructedTypeSymbol constructed
            && constructed.GenericDefinition.Kind == UserDefinedTypeKind.Enum
            && constructed.GenericDefinition.Variants.Any(variant => string.Equals(variant.Name, variantName, StringComparison.Ordinal)))
        {
            enumType = constructed;
            return true;
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

    private string ResolveDeclarationName(string identifier)
    {
        if (Symbols.ScopeDepth == 1 && !string.IsNullOrWhiteSpace(_currentModule))
        {
            return QualifyTopLevelName(_currentModule, identifier);
        }

        return identifier;
    }

    private static string QualifyTopLevelName(string? moduleName, string symbolName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            return symbolName;
        }

        return $"{moduleName}.{symbolName}";
    }

    private bool TryLookupVariableSymbol(string name, out VariableSymbol symbol)
    {
        if (name.Contains('.', StringComparison.Ordinal))
        {
            if (Symbols.TryLookup(name, out var exactSymbol) && exactSymbol is not null)
            {
                var containerPath = ExtractModuleName(name);
                if (IsModuleAccessible(containerPath) || HasVariablePrefix(containerPath))
                {
                    symbol = exactSymbol;
                    return true;
                }
            }

            var moduleName = ExtractModuleName(name);
            if (!IsModuleAccessible(moduleName))
            {
                if (!string.IsNullOrWhiteSpace(_currentModule))
                {
                    var currentQualified = QualifyTopLevelName(_currentModule, name);
                    if (Symbols.TryLookup(currentQualified, out var currentScoped) && currentScoped is not null)
                    {
                        symbol = currentScoped;
                        return true;
                    }
                }

                foreach (var importedModule in _importedModules)
                {
                    var importedQualified = QualifyTopLevelName(importedModule, name);
                    if (Symbols.TryLookup(importedQualified, out var importedScoped) && importedScoped is not null)
                    {
                        symbol = importedScoped;
                        return true;
                    }
                }

                symbol = null!;
                return false;
            }

            symbol = null!;
            return false;
        }

        if (Symbols.TryLookup(name, out var localSymbol) && localSymbol is not null)
        {
            symbol = localSymbol;
            return true;
        }

        var candidates = new List<VariableSymbol>();
        if (!string.IsNullOrWhiteSpace(_currentModule))
        {
            var currentQualified = QualifyTopLevelName(_currentModule, name);
            if (Symbols.TryLookup(currentQualified, out var currentScoped) && currentScoped is not null)
            {
                candidates.Add(currentScoped);
            }
        }

        foreach (var importedModule in _importedModules)
        {
            var qualified = QualifyTopLevelName(importedModule, name);
            if (Symbols.TryLookup(qualified, out var importedScoped) && importedScoped is not null)
            {
                candidates.Add(importedScoped);
            }
        }

        if (candidates.Count == 1)
        {
            symbol = candidates[0];
            return true;
        }

        symbol = null!;
        return false;
    }

    private bool TryLookupVariableSymbolWithScopeDepth(string name, out VariableSymbol symbol, out int scopeDepth)
    {
        if (!TryLookupVariableSymbol(name, out symbol))
        {
            scopeDepth = 0;
            return false;
        }

        if (Symbols.TryLookupWithScopeDepth(symbol.Name, out _, out scopeDepth))
        {
            return true;
        }

        if (!name.Contains('.', StringComparison.Ordinal)
            && Symbols.TryLookupWithScopeDepth(name, out _, out scopeDepth))
        {
            return true;
        }

        scopeDepth = 1;
        return true;
    }

    private bool HasVariablePrefix(string dottedPath)
    {
        var candidate = dottedPath;
        while (!string.IsNullOrWhiteSpace(candidate))
        {
            if (Symbols.TryLookup(candidate, out var symbol) && symbol is not null)
            {
                return true;
            }

            var separator = candidate.LastIndexOf('.');
            if (separator <= 0)
            {
                break;
            }

            candidate = candidate[..separator];
        }

        return false;
    }

    private bool TryLookupTypeSymbol(string typeName, out TypeSymbol symbol)
    {
        if (typeName.Contains('.', StringComparison.Ordinal))
        {
            var moduleName = ExtractModuleName(typeName);
            if (!IsModuleAccessible(moduleName))
            {
                symbol = null!;
                return false;
            }

            if (Symbols.TryLookupType(typeName, out var qualifiedType) && qualifiedType is not null)
            {
                symbol = qualifiedType;
                return true;
            }

            symbol = null!;
            return false;
        }

        if (Symbols.TryLookupType(typeName, out var builtInOrCurrent) && builtInOrCurrent is not null)
        {
            symbol = builtInOrCurrent;
            return true;
        }

        var candidates = new List<TypeSymbol>();
        if (!string.IsNullOrWhiteSpace(_currentModule))
        {
            var currentQualified = QualifyTopLevelName(_currentModule, typeName);
            if (Symbols.TryLookupType(currentQualified, out var currentScoped) && currentScoped is not null)
            {
                candidates.Add(currentScoped);
            }
        }

        foreach (var importedModule in _importedModules)
        {
            var qualified = QualifyTopLevelName(importedModule, typeName);
            if (Symbols.TryLookupType(qualified, out var importedScoped) && importedScoped is not null)
            {
                candidates.Add(importedScoped);
            }
        }

        if (candidates.Count == 1)
        {
            symbol = candidates[0];
            return true;
        }

        symbol = null!;
        return false;
    }

    private bool IsModuleAccessible(string moduleName)
    {
        if (string.Equals(moduleName, _currentModule, StringComparison.Ordinal))
        {
            return true;
        }

        return _importedModules.Contains(moduleName);
    }

    private static string ExtractModuleName(string qualifiedName)
    {
        var lastDot = qualifiedName.LastIndexOf('.');
        return lastDot <= 0
            ? string.Empty
            : qualifiedName[..lastDot];
    }

    private void ReportTypeError(string message, SyntaxNode node)
    {
        var span = node.Span;
        _diagnostics.ReportTypeError(message, span.Line, span.Column, span.Length);
    }

    private bool IsInsideParallelLoopBody()
    {
        return _parallelLoopContexts.Count > 0;
    }

    private bool TryGetCurrentParallelLoopContext(out ParallelLoopContext context)
    {
        if (_parallelLoopContexts.Count > 0)
        {
            context = _parallelLoopContexts.Peek();
            return true;
        }

        context = null!;
        return false;
    }

    private static bool ContainsNameReference(ExpressionSyntax expression, string identifier)
    {
        return expression switch
        {
            NameExpressionSyntax name => string.Equals(name.Identifier, identifier, StringComparison.Ordinal),
            IndexExpressionSyntax index => ContainsNameReference(index.Target, identifier) || ContainsNameReference(index.Index, identifier),
            UnaryExpressionSyntax unary => ContainsNameReference(unary.Operand, identifier),
            BinaryExpressionSyntax binary => ContainsNameReference(binary.Left, identifier) || ContainsNameReference(binary.Right, identifier),
            ParenthesizedExpressionSyntax parenthesized => ContainsNameReference(parenthesized.Expression, identifier),
            ArrayLiteralExpressionSyntax arrayLiteral => arrayLiteral.Elements.Any(element => ContainsNameReference(element, identifier)),
            ConstructorExpressionSyntax constructor => constructor.Arguments.Any(argument => ContainsNameReference(argument, identifier)),
            CastExpressionSyntax cast => ContainsNameReference(cast.Expression, identifier),
            _ => false
        };
    }

    private static bool HasDirectIterationIndex(ExpressionSyntax expression, string identifier)
    {
        return expression switch
        {
            IndexExpressionSyntax indexExpression =>
                IsNameReference(indexExpression.Index, identifier)
                || HasDirectIterationIndex(indexExpression.Target, identifier),
            _ => false
        };
    }

    private static bool IsNameReference(ExpressionSyntax expression, string identifier)
    {
        return expression is NameExpressionSyntax name
            && string.Equals(name.Identifier, identifier, StringComparison.Ordinal);
    }

    private bool TryResolveIndexedBaseVariable(ExpressionSyntax expression, out VariableSymbol symbol)
    {
        switch (expression)
        {
            case IndexExpressionSyntax indexExpression:
                return TryResolveIndexedBaseVariable(indexExpression.Target, out symbol);

            case NameExpressionSyntax name:
                return TryLookupVariableSymbol(name.Identifier, out symbol);

            default:
                symbol = null!;
                return false;
        }
    }

    private bool TryResolveIndexedBaseVariableWithScopeDepth(ExpressionSyntax expression, out VariableSymbol symbol, out int scopeDepth)
    {
        switch (expression)
        {
            case IndexExpressionSyntax indexExpression:
                return TryResolveIndexedBaseVariableWithScopeDepth(indexExpression.Target, out symbol, out scopeDepth);

            case NameExpressionSyntax name:
                return TryLookupVariableSymbolWithScopeDepth(name.Identifier, out symbol, out scopeDepth);

            default:
                symbol = null!;
                scopeDepth = 0;
                return false;
        }
    }
}
