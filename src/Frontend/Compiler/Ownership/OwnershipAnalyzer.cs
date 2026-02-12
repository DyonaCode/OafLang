using Oaf.Frontend.Compiler.AST;
using Oaf.Frontend.Compiler.Diagnostics;
using Oaf.Frontend.Compiler.Lexer;

namespace Oaf.Frontend.Compiler.Ownership;

public sealed class OwnershipAnalyzer
{
    private enum OwnershipState
    {
        Owned,
        Moved
    }

    private sealed class OwnershipVariable
    {
        public OwnershipVariable(string name, bool isMutable, bool isMoveType, int scopeDepth)
        {
            Name = name;
            IsMutable = isMutable;
            IsMoveType = isMoveType;
            ScopeDepth = scopeDepth;
        }

        public string Name { get; }

        public bool IsMutable { get; }

        public bool IsMoveType { get; }

        public int ScopeDepth { get; }

        public OwnershipState State { get; set; } = OwnershipState.Owned;

        public int ImmutableBorrowCount { get; set; }

        public bool HasMutableBorrow { get; set; }
    }

    private readonly DiagnosticBag _diagnostics;
    private readonly Stack<Dictionary<string, OwnershipVariable>> _scopes = new();
    private readonly HashSet<string> _importedModules = new(StringComparer.Ordinal);
    private string? _currentModule;

    public OwnershipAnalyzer(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public void Analyze(CompilationUnitSyntax compilationUnit)
    {
        _currentModule = null;
        _importedModules.Clear();
        EnterScope();

        foreach (var statement in compilationUnit.Statements)
        {
            AnalyzeStatement(statement);
        }

        ExitScope();
    }

    private void AnalyzeStatement(StatementSyntax statement)
    {
        switch (statement)
        {
            case BlockStatementSyntax block:
                EnterScope();
                foreach (var nested in block.Statements)
                {
                    AnalyzeStatement(nested);
                }

                ExitScope();
                break;

            case VariableDeclarationStatementSyntax declaration:
                AnalyzeVariableDeclaration(declaration);
                break;

            case AssignmentStatementSyntax assignment:
                AnalyzeAssignment(assignment);
                break;

            case ExpressionStatementSyntax expressionStatement:
                AnalyzeExpression(expressionStatement.Expression);
                break;

            case ReturnStatementSyntax returnStatement when returnStatement.Expression is not null:
                AnalyzeExpression(returnStatement.Expression);
                break;

            case IfStatementSyntax ifStatement:
                AnalyzeExpression(ifStatement.Condition);
                AnalyzeStatement(ifStatement.ThenStatement);
                if (ifStatement.ElseStatement is not null)
                {
                    AnalyzeStatement(ifStatement.ElseStatement);
                }

                break;

            case LoopStatementSyntax loopStatement:
                AnalyzeExpression(loopStatement.IteratorOrCondition);
                EnterScope();

                if (!string.IsNullOrWhiteSpace(loopStatement.IterationVariable))
                {
                    Declare(loopStatement.IterationVariable, isMutable: false, isMoveType: false, loopStatement.Span);
                }

                AnalyzeStatement(loopStatement.Body);
                ExitScope();
                break;

            case TypeDeclarationStatementSyntax:
            case BreakStatementSyntax:
            case ContinueStatementSyntax:
            case ReturnStatementSyntax:
                break;

            case ModuleDeclarationStatementSyntax moduleDeclaration:
                _currentModule = moduleDeclaration.ModuleName;
                _importedModules.Clear();
                break;

            case ImportStatementSyntax importStatement:
                _importedModules.Add(importStatement.ModuleName);
                break;
        }
    }

    private void AnalyzeVariableDeclaration(VariableDeclarationStatementSyntax declaration)
    {
        var isMoveType = declaration.DeclaredType is not null
            ? IsMoveType(declaration.DeclaredType)
            : InferExpressionMoveType(declaration.Initializer);

        var declarationName = ResolveDeclarationName(declaration.Identifier);
        var variable = Declare(declarationName, declaration.IsMutable, isMoveType, declaration.Span);
        if (variable is null)
        {
            AnalyzeExpression(declaration.Initializer);
            return;
        }

        if (declaration.Initializer is NameExpressionSyntax sourceName
            && TryLookupWithContext(sourceName.Identifier, out var source)
            && source.IsMoveType
            && variable.IsMoveType)
        {
            if (string.Equals(source.Name, variable.Name, StringComparison.Ordinal))
            {
                ReportOwnershipError("Cannot move a value into itself during declaration.", declaration);
                return;
            }

            if (TryMove(source, declaration.Initializer.Span))
            {
                variable.State = OwnershipState.Owned;
            }

            return;
        }

        AnalyzeExpression(declaration.Initializer);
    }

    private void AnalyzeAssignment(AssignmentStatementSyntax assignment)
    {
        if (!TryLookupWithContext(assignment.Identifier, out var target))
        {
            AnalyzeExpression(assignment.Expression);
            return;
        }

        if (target.HasMutableBorrow || target.ImmutableBorrowCount > 0)
        {
            ReportOwnershipError($"Cannot assign to '{target.Name}' while it is borrowed.", assignment);
        }

        if (assignment.OperatorKind != TokenKind.EqualsToken)
        {
            AnalyzeExpression(assignment.Expression);
            return;
        }

        if (target.IsMoveType
            && assignment.Expression is NameExpressionSyntax sourceName
            && TryLookupWithContext(sourceName.Identifier, out var source)
            && source.IsMoveType)
        {
            if (string.Equals(source.Name, target.Name, StringComparison.Ordinal))
            {
                ReportOwnershipError("Cannot move a value into itself during assignment.", assignment);
                return;
            }

            if (TryMove(source, assignment.Expression.Span))
            {
                target.State = OwnershipState.Owned;
            }

            return;
        }

        AnalyzeExpression(assignment.Expression);

        if (target.IsMoveType)
        {
            target.State = OwnershipState.Owned;
        }
    }

    private void AnalyzeExpression(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax:
                return;

            case NameExpressionSyntax name:
                AnalyzeNameExpression(name);
                return;

            case CastExpressionSyntax cast:
                AnalyzeExpression(cast.Expression);
                return;

            case UnaryExpressionSyntax unary:
                AnalyzeExpression(unary.Operand);
                return;

            case BinaryExpressionSyntax binary:
                AnalyzeExpression(binary.Left);
                AnalyzeExpression(binary.Right);
                return;

            case ParenthesizedExpressionSyntax parenthesized:
                AnalyzeExpression(parenthesized.Expression);
                return;
        }
    }

    private void AnalyzeNameExpression(NameExpressionSyntax nameExpression)
    {
        if (!TryLookupWithContext(nameExpression.Identifier, out var variable) || !variable.IsMoveType)
        {
            return;
        }

        if (variable.State == OwnershipState.Moved)
        {
            ReportOwnershipError($"Use of moved value '{variable.Name}'.", nameExpression);
            return;
        }

        if (variable.HasMutableBorrow)
        {
            ReportOwnershipError($"Cannot immutably borrow '{variable.Name}' while it is mutably borrowed.", nameExpression);
            return;
        }

        variable.ImmutableBorrowCount++;
        variable.ImmutableBorrowCount--;
    }

    private OwnershipVariable? Declare(string name, bool isMutable, bool isMoveType, SourceSpan span)
    {
        var currentScope = _scopes.Peek();
        if (currentScope.ContainsKey(name))
        {
            ReportOwnershipError($"Variable '{name}' is already declared in this scope.", span);
            return null;
        }

        var variable = new OwnershipVariable(name, isMutable, isMoveType, _scopes.Count);
        currentScope[name] = variable;
        return variable;
    }

    private bool TryMove(OwnershipVariable variable, SourceSpan span)
    {
        if (variable.State == OwnershipState.Moved)
        {
            ReportOwnershipError($"Cannot move from '{variable.Name}' because it has already been moved.", span);
            return false;
        }

        if (variable.ImmutableBorrowCount > 0 || variable.HasMutableBorrow)
        {
            ReportOwnershipError($"Cannot move from '{variable.Name}' while it is borrowed.", span);
            return false;
        }

        variable.State = OwnershipState.Moved;
        return true;
    }

    private bool TryLookup(string name, out OwnershipVariable variable)
    {
        foreach (var scope in _scopes)
        {
            if (scope.TryGetValue(name, out variable!))
            {
                return true;
            }
        }

        variable = null!;
        return false;
    }

    private bool TryLookupWithContext(string name, out OwnershipVariable variable)
    {
        if (name.Contains('.', StringComparison.Ordinal))
        {
            var moduleName = ExtractModuleName(name);
            if (!IsModuleAccessible(moduleName))
            {
                variable = null!;
                return false;
            }

            return TryLookup(name, out variable);
        }

        if (TryLookup(name, out variable))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_currentModule))
        {
            if (TryLookup(QualifyTopLevelName(_currentModule, name), out variable))
            {
                return true;
            }
        }

        foreach (var importedModule in _importedModules)
        {
            if (TryLookup(QualifyTopLevelName(importedModule, name), out variable))
            {
                return true;
            }
        }

        variable = null!;
        return false;
    }

    private void EnterScope()
    {
        _scopes.Push(new Dictionary<string, OwnershipVariable>(StringComparer.Ordinal));
    }

    private void ExitScope()
    {
        if (_scopes.Count == 0)
        {
            return;
        }

        var scope = _scopes.Pop();

        foreach (var variable in scope.Values)
        {
            if (variable.ImmutableBorrowCount > 0 || variable.HasMutableBorrow)
            {
                ReportOwnershipError($"Borrow of '{variable.Name}' outlives its scope.", SourceSpan.Unknown);
            }
        }
    }

    private bool IsMoveType(TypeReferenceSyntax typeReference)
    {
        return !IsCopyTypeName(typeReference.Name);
    }

    private bool InferExpressionMoveType(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal => literal.Value is string,
            NameExpressionSyntax name when TryLookupWithContext(name.Identifier, out var variable) => variable.IsMoveType,
            CastExpressionSyntax cast => IsMoveType(cast.TargetType),
            ParenthesizedExpressionSyntax parenthesized => InferExpressionMoveType(parenthesized.Expression),
            _ => false
        };
    }

    private string ResolveDeclarationName(string identifier)
    {
        if (_scopes.Count == 1 && !string.IsNullOrWhiteSpace(_currentModule))
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

    private bool IsCopyTypeName(string typeName)
    {
        return string.Equals(typeName, "int", StringComparison.Ordinal)
               || string.Equals(typeName, "float", StringComparison.Ordinal)
               || string.Equals(typeName, "bool", StringComparison.Ordinal)
               || string.Equals(typeName, "char", StringComparison.Ordinal);
    }

    private void ReportOwnershipError(string message, SyntaxNode node)
    {
        ReportOwnershipError(message, node.Span);
    }

    private void ReportOwnershipError(string message, SourceSpan span)
    {
        _diagnostics.ReportOwnershipError(message, span.Line, span.Column, Math.Max(span.Length, 1));
    }
}
