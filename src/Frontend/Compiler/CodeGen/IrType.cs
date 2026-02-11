namespace Oaf.Frontend.Compiler.CodeGen;

public enum IrTypeKind
{
    Void,
    Int,
    Float,
    Bool,
    Char,
    String,
    Unknown
}

public sealed class IrType : IEquatable<IrType>
{
    private IrType(IrTypeKind kind, string name)
    {
        Kind = kind;
        Name = name;
    }

    public IrTypeKind Kind { get; }

    public string Name { get; }

    public static IrType Void { get; } = new(IrTypeKind.Void, "void");

    public static IrType Int { get; } = new(IrTypeKind.Int, "int");

    public static IrType Float { get; } = new(IrTypeKind.Float, "float");

    public static IrType Bool { get; } = new(IrTypeKind.Bool, "bool");

    public static IrType Char { get; } = new(IrTypeKind.Char, "char");

    public static IrType String { get; } = new(IrTypeKind.String, "string");

    public static IrType Unknown { get; } = new(IrTypeKind.Unknown, "unknown");

    public bool IsNumeric => Kind is IrTypeKind.Int or IrTypeKind.Float or IrTypeKind.Char;

    public static IrType FromTypeName(string name)
    {
        return name switch
        {
            "void" => Void,
            "int" => Int,
            "float" => Float,
            "bool" => Bool,
            "char" => Char,
            "string" => String,
            _ => Unknown
        };
    }

    public bool Equals(IrType? other)
    {
        return other is not null && Kind == other.Kind;
    }

    public override bool Equals(object? obj)
    {
        return obj is IrType other && Equals(other);
    }

    public override int GetHashCode()
    {
        return (int)Kind;
    }

    public override string ToString()
    {
        return Name;
    }
}
