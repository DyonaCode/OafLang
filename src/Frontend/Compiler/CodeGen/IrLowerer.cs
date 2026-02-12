using Oaf.Frontend.Compiler.AST;
using Oaf.Frontend.Compiler.Lexer;

namespace Oaf.Frontend.Compiler.CodeGen;

public sealed class IrLowerer
{
    private readonly Stack<Dictionary<string, IrVariableValue>> _scopes = new();
    private readonly Stack<(string BreakLabel, string ContinueLabel)> _loopLabels = new();
    private readonly HashSet<string> _importedModules = new(StringComparer.Ordinal);
    private int _tempCounter;
    private int _labelCounter;
    private string? _currentModule;

    private IrFunction? _function;
    private IrBasicBlock? _currentBlock;

    public IrModule Lower(CompilationUnitSyntax syntaxTree)
    {
        _tempCounter = 0;
        _labelCounter = 0;
        _scopes.Clear();
        _loopLabels.Clear();
        _currentModule = null;
        _importedModules.Clear();

        var module = new IrModule();
        _function = new IrFunction("main");
        module.Functions.Add(_function);

        _currentBlock = CreateBlock("entry");
        EnterScope();

        foreach (var statement in syntaxTree.Statements)
        {
            LowerStatement(statement);
        }

        if (_currentBlock is not null && !_currentBlock.IsTerminated)
        {
            Emit(new IrReturnInstruction(null));
        }

        ExitScope();
        return module;
    }

    private void LowerStatement(StatementSyntax statement)
    {
        switch (statement)
        {
            case TypeDeclarationStatementSyntax:
                return;

            case ModuleDeclarationStatementSyntax moduleDeclaration:
                _currentModule = moduleDeclaration.ModuleName;
                _importedModules.Clear();
                return;

            case ImportStatementSyntax importStatement:
                _importedModules.Add(importStatement.ModuleName);
                return;

            case BlockStatementSyntax block:
                EnterScope();
                foreach (var nested in block.Statements)
                {
                    LowerStatement(nested);
                }

                ExitScope();
                return;

            case VariableDeclarationStatementSyntax declaration:
                LowerVariableDeclaration(declaration);
                return;

            case AssignmentStatementSyntax assignment:
                LowerAssignment(assignment);
                return;

            case ExpressionStatementSyntax expressionStatement:
                _ = LowerExpression(expressionStatement.Expression);
                return;

            case ReturnStatementSyntax returnStatement:
                Emit(new IrReturnInstruction(returnStatement.Expression is null ? null : LowerExpression(returnStatement.Expression)));
                return;

            case IfStatementSyntax ifStatement:
                LowerIfStatement(ifStatement);
                return;

            case LoopStatementSyntax loopStatement:
                LowerLoopStatement(loopStatement);
                return;

            case BreakStatementSyntax:
                if (_loopLabels.Count > 0)
                {
                    Emit(new IrJumpInstruction(_loopLabels.Peek().BreakLabel));
                }

                return;

            case ContinueStatementSyntax:
                if (_loopLabels.Count > 0)
                {
                    Emit(new IrJumpInstruction(_loopLabels.Peek().ContinueLabel));
                }

                return;
        }
    }

    private void LowerVariableDeclaration(VariableDeclarationStatementSyntax declaration)
    {
        var declaredType = declaration.DeclaredType is null
            ? InferTypeFromExpression(declaration.Initializer)
            : IrType.FromTypeName(declaration.DeclaredType.Name);

        var symbolName = ResolveDeclarationName(declaration.Identifier);
        var variable = new IrVariableValue(declaredType, symbolName);
        DeclareVariable(variable);

        var value = LowerExpression(declaration.Initializer);
        Emit(new IrAssignInstruction(variable, value));
    }

    private void LowerAssignment(AssignmentStatementSyntax assignment)
    {
        var variable = ResolveVariableWithContext(assignment.Identifier) ?? new IrVariableValue(IrType.Unknown, assignment.Identifier);
        var value = LowerExpression(assignment.Expression);

        if (assignment.OperatorKind == TokenKind.EqualsToken)
        {
            Emit(new IrAssignInstruction(variable, value));
            return;
        }

        var operation = MapBinaryOperator(assignment.OperatorKind);
        if (operation is null)
        {
            Emit(new IrAssignInstruction(variable, value));
            return;
        }

        var computedType = variable.Type.Kind == IrTypeKind.Unknown ? value.Type : variable.Type;
        var temporary = NewTemporary(computedType);
        Emit(new IrBinaryInstruction(temporary, operation.Value, variable, value));
        Emit(new IrAssignInstruction(variable, temporary));
    }

    private void LowerIfStatement(IfStatementSyntax statement)
    {
        var thenLabel = AllocateLabel("if_then");
        var elseLabel = AllocateLabel("if_else");
        var endLabel = AllocateLabel("if_end");

        var condition = LowerExpression(statement.Condition);
        Emit(new IrBranchInstruction(condition, thenLabel, statement.ElseStatement is null ? endLabel : elseLabel));

        var thenBlock = CreateBlock(thenLabel);
        SetCurrentBlock(thenBlock);
        LowerStatement(statement.ThenStatement);
        if (!_currentBlock!.IsTerminated)
        {
            Emit(new IrJumpInstruction(endLabel));
        }

        if (statement.ElseStatement is not null)
        {
            var elseBlock = CreateBlock(elseLabel);
            SetCurrentBlock(elseBlock);
            LowerStatement(statement.ElseStatement);
            if (!_currentBlock!.IsTerminated)
            {
                Emit(new IrJumpInstruction(endLabel));
            }
        }

        var endBlock = CreateBlock(endLabel);
        SetCurrentBlock(endBlock);
    }

    private void LowerLoopStatement(LoopStatementSyntax statement)
    {
        var conditionLabel = AllocateLabel("loop_cond");
        var bodyLabel = AllocateLabel("loop_body");
        var endLabel = AllocateLabel("loop_end");

        Emit(new IrJumpInstruction(conditionLabel));

        var conditionBlock = CreateBlock(conditionLabel);
        SetCurrentBlock(conditionBlock);

        var condition = LowerExpression(statement.IteratorOrCondition);
        Emit(new IrBranchInstruction(condition, bodyLabel, endLabel));

        var bodyBlock = CreateBlock(bodyLabel);
        SetCurrentBlock(bodyBlock);

        EnterScope();
        if (!string.IsNullOrWhiteSpace(statement.IterationVariable))
        {
            DeclareVariable(new IrVariableValue(IrType.Int, statement.IterationVariable));
        }

        _loopLabels.Push((endLabel, conditionLabel));
        LowerStatement(statement.Body);
        _loopLabels.Pop();
        ExitScope();

        if (!_currentBlock!.IsTerminated)
        {
            Emit(new IrJumpInstruction(conditionLabel));
        }

        var endBlock = CreateBlock(endLabel);
        SetCurrentBlock(endBlock);
    }

    private IrValue LowerExpression(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax literal:
                return new IrConstantValue(InferTypeFromLiteral(literal.Value), literal.Value);

            case NameExpressionSyntax name:
                return ResolveVariableWithContext(name.Identifier) ?? new IrVariableValue(IrType.Unknown, name.Identifier);

            case CastExpressionSyntax cast:
                {
                    var source = LowerExpression(cast.Expression);
                    var targetType = IrType.FromTypeName(cast.TargetType.Name);
                    var destination = NewTemporary(targetType);
                    Emit(new IrCastInstruction(destination, source, targetType));
                    return destination;
                }

            case UnaryExpressionSyntax unary:
                {
                    var operand = LowerExpression(unary.Operand);
                    var destinationType = InferUnaryType(unary.OperatorKind, operand.Type);
                    var destination = NewTemporary(destinationType);
                    var operation = MapUnaryOperator(unary.OperatorKind);
                    Emit(new IrUnaryInstruction(destination, operation, operand));
                    return destination;
                }

            case BinaryExpressionSyntax binary:
                {
                    var left = LowerExpression(binary.Left);
                    var right = LowerExpression(binary.Right);
                    var destinationType = InferBinaryType(binary.OperatorKind, left.Type, right.Type);
                    var destination = NewTemporary(destinationType);
                    var operation = MapBinaryOperator(binary.OperatorKind) ?? IrBinaryOperator.Add;
                    Emit(new IrBinaryInstruction(destination, operation, left, right));
                    return destination;
                }

            case ParenthesizedExpressionSyntax parenthesized:
                return LowerExpression(parenthesized.Expression);

            default:
                return new IrConstantValue(IrType.Unknown, null);
        }
    }

    private IrType InferTypeFromExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal => InferTypeFromLiteral(literal.Value),
            NameExpressionSyntax name when ResolveVariableWithContext(name.Identifier) is { } scopedVariable => scopedVariable.Type,
            CastExpressionSyntax cast => IrType.FromTypeName(cast.TargetType.Name),
            UnaryExpressionSyntax unary => InferUnaryType(unary.OperatorKind, InferTypeFromExpression(unary.Operand)),
            BinaryExpressionSyntax binary => InferBinaryType(
                binary.OperatorKind,
                InferTypeFromExpression(binary.Left),
                InferTypeFromExpression(binary.Right)),
            ParenthesizedExpressionSyntax parenthesized => InferTypeFromExpression(parenthesized.Expression),
            _ => IrType.Unknown
        };
    }

    private static IrType InferTypeFromLiteral(object? value)
    {
        return value switch
        {
            null => IrType.Unknown,
            bool => IrType.Bool,
            char => IrType.Char,
            string => IrType.String,
            double or float or decimal => IrType.Float,
            sbyte or byte or short or ushort or int or uint or long or ulong => IrType.Int,
            _ => IrType.Unknown
        };
    }

    private static IrType InferUnaryType(TokenKind kind, IrType operandType)
    {
        return kind switch
        {
            TokenKind.BangToken => IrType.Bool,
            TokenKind.TildeToken => IrType.Int,
            TokenKind.PlusToken or TokenKind.MinusToken => operandType.IsNumeric ? (operandType.Kind == IrTypeKind.Float ? IrType.Float : IrType.Int) : IrType.Unknown,
            _ => IrType.Unknown
        };
    }

    private static IrType InferBinaryType(TokenKind kind, IrType left, IrType right)
    {
        return kind switch
        {
            TokenKind.EqualsEqualsToken or TokenKind.BangEqualsToken
                or TokenKind.LessToken or TokenKind.LessOrEqualsToken
                or TokenKind.GreaterToken or TokenKind.GreaterOrEqualsToken
                or TokenKind.DoubleAmpersandToken or TokenKind.DoublePipeToken
                or TokenKind.BangPipeToken or TokenKind.BangAmpersandToken
                or TokenKind.AmpersandToken or TokenKind.PipeToken
                => IrType.Bool,

            TokenKind.ShiftLeftToken or TokenKind.ShiftRightToken
                or TokenKind.UnsignedShiftLeftToken or TokenKind.UnsignedShiftRightToken
                or TokenKind.CaretToken or TokenKind.CaretAmpersandToken
                => IrType.Int,

            TokenKind.PlusToken when left.Kind == IrTypeKind.String || right.Kind == IrTypeKind.String
                => IrType.String,

            TokenKind.PlusToken or TokenKind.MinusToken
                or TokenKind.StarToken or TokenKind.SlashToken
                or TokenKind.PercentToken or TokenKind.RootToken
                => (left.Kind == IrTypeKind.Float || right.Kind == IrTypeKind.Float) ? IrType.Float : IrType.Int,

            _ => IrType.Unknown
        };
    }

    private static IrUnaryOperator MapUnaryOperator(TokenKind kind)
    {
        return kind switch
        {
            TokenKind.PlusToken => IrUnaryOperator.Identity,
            TokenKind.MinusToken => IrUnaryOperator.Negate,
            TokenKind.BangToken => IrUnaryOperator.LogicalNot,
            TokenKind.TildeToken => IrUnaryOperator.BitwiseNot,
            _ => IrUnaryOperator.Identity
        };
    }

    private static IrBinaryOperator? MapBinaryOperator(TokenKind kind)
    {
        return kind switch
        {
            TokenKind.PlusToken => IrBinaryOperator.Add,
            TokenKind.MinusToken => IrBinaryOperator.Subtract,
            TokenKind.StarToken => IrBinaryOperator.Multiply,
            TokenKind.SlashToken => IrBinaryOperator.Divide,
            TokenKind.PercentToken => IrBinaryOperator.Modulo,
            TokenKind.RootToken => IrBinaryOperator.Root,
            TokenKind.ShiftLeftToken => IrBinaryOperator.ShiftLeft,
            TokenKind.ShiftRightToken => IrBinaryOperator.ShiftRight,
            TokenKind.UnsignedShiftLeftToken => IrBinaryOperator.UnsignedShiftLeft,
            TokenKind.UnsignedShiftRightToken => IrBinaryOperator.UnsignedShiftRight,
            TokenKind.LessToken => IrBinaryOperator.Less,
            TokenKind.LessOrEqualsToken => IrBinaryOperator.LessOrEqual,
            TokenKind.GreaterToken => IrBinaryOperator.Greater,
            TokenKind.GreaterOrEqualsToken => IrBinaryOperator.GreaterOrEqual,
            TokenKind.EqualsEqualsToken => IrBinaryOperator.Equal,
            TokenKind.BangEqualsToken => IrBinaryOperator.NotEqual,
            TokenKind.DoubleAmpersandToken => IrBinaryOperator.LogicalAnd,
            TokenKind.DoublePipeToken => IrBinaryOperator.LogicalOr,
            TokenKind.BangPipeToken => IrBinaryOperator.LogicalXor,
            TokenKind.BangAmpersandToken => IrBinaryOperator.LogicalXand,
            TokenKind.AmpersandToken => IrBinaryOperator.BitAnd,
            TokenKind.PipeToken => IrBinaryOperator.BitOr,
            TokenKind.CaretToken => IrBinaryOperator.BitXor,
            TokenKind.CaretAmpersandToken => IrBinaryOperator.BitXand,
            TokenKind.PlusEqualsToken => IrBinaryOperator.Add,
            TokenKind.MinusEqualsToken => IrBinaryOperator.Subtract,
            TokenKind.StarEqualsToken => IrBinaryOperator.Multiply,
            TokenKind.SlashEqualsToken => IrBinaryOperator.Divide,
            _ => null
        };
    }

    private IrTemporaryValue NewTemporary(IrType type)
    {
        var name = $"t{_tempCounter++}";
        return new IrTemporaryValue(type, name);
    }

    private string AllocateLabel(string prefix)
    {
        return $"{prefix}_{_labelCounter++}";
    }

    private IrBasicBlock CreateBlock(string label)
    {
        var function = EnsureFunction();
        var block = new IrBasicBlock(label);
        function.Blocks.Add(block);
        return block;
    }

    private void SetCurrentBlock(IrBasicBlock block)
    {
        _currentBlock = block;
    }

    private void Emit(IrInstruction instruction)
    {
        var block = EnsureCurrentBlock();
        if (block.IsTerminated)
        {
            return;
        }

        block.Append(instruction);
    }

    private void EnterScope()
    {
        _scopes.Push(new Dictionary<string, IrVariableValue>(StringComparer.Ordinal));
    }

    private void ExitScope()
    {
        if (_scopes.Count > 0)
        {
            _scopes.Pop();
        }
    }

    private void DeclareVariable(IrVariableValue variable)
    {
        _scopes.Peek()[variable.Name] = variable;
    }

    private string ResolveDeclarationName(string identifier)
    {
        if (IsTopLevelScope() && !string.IsNullOrWhiteSpace(_currentModule))
        {
            return QualifyTopLevelName(_currentModule, identifier);
        }

        return identifier;
    }

    private IrVariableValue? ResolveVariableWithContext(string name)
    {
        if (name.Contains('.', StringComparison.Ordinal))
        {
            var moduleName = ExtractModuleName(name);
            if (!IsModuleAccessible(moduleName))
            {
                return null;
            }

            return ResolveVariable(name);
        }

        var local = ResolveVariable(name);
        if (local is not null)
        {
            return local;
        }

        if (!string.IsNullOrWhiteSpace(_currentModule))
        {
            var currentScoped = ResolveVariable(QualifyTopLevelName(_currentModule, name));
            if (currentScoped is not null)
            {
                return currentScoped;
            }
        }

        foreach (var import in _importedModules)
        {
            var importedScoped = ResolveVariable(QualifyTopLevelName(import, name));
            if (importedScoped is not null)
            {
                return importedScoped;
            }
        }

        return null;
    }

    private bool IsTopLevelScope()
    {
        return _scopes.Count == 1;
    }

    private static string QualifyTopLevelName(string? moduleName, string symbolName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            return symbolName;
        }

        return $"{moduleName}.{symbolName}";
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

    private IrVariableValue? ResolveVariable(string name)
    {
        foreach (var scope in _scopes)
        {
            if (scope.TryGetValue(name, out var variable))
            {
                return variable;
            }
        }

        return null;
    }

    private IrFunction EnsureFunction()
    {
        return _function ?? throw new InvalidOperationException("IR function has not been initialized.");
    }

    private IrBasicBlock EnsureCurrentBlock()
    {
        return _currentBlock ?? throw new InvalidOperationException("Current block has not been initialized.");
    }
}
