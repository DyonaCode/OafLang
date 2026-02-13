namespace Oaf.Frontend.Compiler.Symbols;

public sealed class SymbolTable
{
    private readonly Stack<Dictionary<string, VariableSymbol>> _scopes = new();
    private readonly Dictionary<string, TypeSymbol> _types = new(StringComparer.Ordinal);

    public SymbolTable()
    {
        _scopes.Push(new Dictionary<string, VariableSymbol>(StringComparer.Ordinal));
        RegisterBuiltInTypes();
    }

    public int ScopeDepth => _scopes.Count;

    public void EnterScope()
    {
        _scopes.Push(new Dictionary<string, VariableSymbol>(StringComparer.Ordinal));
    }

    public void ExitScope()
    {
        if (_scopes.Count <= 1)
        {
            return;
        }

        _scopes.Pop();
    }

    public bool TryDeclare(VariableSymbol symbol)
    {
        var current = _scopes.Peek();
        if (current.ContainsKey(symbol.Name))
        {
            return false;
        }

        current[symbol.Name] = symbol;
        return true;
    }

    public bool TryLookup(string name, out VariableSymbol? symbol)
    {
        foreach (var scope in _scopes)
        {
            if (scope.TryGetValue(name, out symbol))
            {
                return true;
            }
        }

        symbol = null;
        return false;
    }

    public bool TryLookupWithScopeDepth(string name, out VariableSymbol? symbol, out int scopeDepth)
    {
        var currentDepth = _scopes.Count;
        foreach (var scope in _scopes)
        {
            if (scope.TryGetValue(name, out symbol))
            {
                scopeDepth = currentDepth;
                return true;
            }

            currentDepth--;
        }

        symbol = null;
        scopeDepth = 0;
        return false;
    }

    public bool TryDeclareType(TypeSymbol symbol)
    {
        if (_types.ContainsKey(symbol.Name))
        {
            return false;
        }

        _types[symbol.Name] = symbol;
        return true;
    }

    public bool TryLookupType(string name, out TypeSymbol? symbol)
    {
        return _types.TryGetValue(name, out symbol);
    }

    public bool IsDeclaredInCurrentScope(string name)
    {
        return _scopes.Peek().ContainsKey(name);
    }

    private void RegisterBuiltInTypes()
    {
        foreach (var primitive in PrimitiveTypeSymbol.All)
        {
            _types[primitive.Name] = primitive;
        }
    }
}
