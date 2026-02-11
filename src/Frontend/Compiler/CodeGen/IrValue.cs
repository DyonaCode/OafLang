namespace Oaf.Frontend.Compiler.CodeGen;

public abstract class IrValue
{
    protected IrValue(IrType type)
    {
        Type = type;
    }

    public IrType Type { get; }

    public abstract string DisplayText { get; }
}

public sealed class IrConstantValue : IrValue
{
    public IrConstantValue(IrType type, object? value)
        : base(type)
    {
        Value = value;
    }

    public object? Value { get; }

    public override string DisplayText
    {
        get
        {
            return Value switch
            {
                null => "null",
                string text => $"\"{text}\"",
                char ch => $"'{ch}'",
                bool boolean => boolean ? "true" : "false",
                _ => Value.ToString() ?? "null"
            };
        }
    }
}

public sealed class IrTemporaryValue : IrValue
{
    public IrTemporaryValue(IrType type, string name)
        : base(type)
    {
        Name = name;
    }

    public string Name { get; }

    public override string DisplayText => Name;
}

public sealed class IrVariableValue : IrValue
{
    public IrVariableValue(IrType type, string name)
        : base(type)
    {
        Name = name;
    }

    public string Name { get; }

    public override string DisplayText => Name;
}
