namespace Oaf.Frontend.Compiler.Symbols;

public abstract class Symbol
{
    protected Symbol(string name)
    {
        Name = name;
    }

    public string Name { get; }
}

public sealed class VariableSymbol : Symbol
{
    public VariableSymbol(string name, TypeSymbol type, bool isMutable)
        : base(name)
    {
        Type = type;
        IsMutable = isMutable;
    }

    public TypeSymbol Type { get; }

    public bool IsMutable { get; }
}
