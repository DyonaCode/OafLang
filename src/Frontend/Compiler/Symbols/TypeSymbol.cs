namespace Oaf.Frontend.Compiler.Symbols;

public abstract class TypeSymbol
{
    protected TypeSymbol(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public override string ToString()
    {
        return Name;
    }
}

public sealed class PrimitiveTypeSymbol : TypeSymbol
{
    private PrimitiveTypeSymbol(string name)
        : base(name)
    {
    }

    public static PrimitiveTypeSymbol Int { get; } = new("int");

    public static PrimitiveTypeSymbol Float { get; } = new("float");

    public static PrimitiveTypeSymbol Bool { get; } = new("bool");

    public static PrimitiveTypeSymbol String { get; } = new("string");

    public static PrimitiveTypeSymbol Char { get; } = new("char");

    public static PrimitiveTypeSymbol Unit { get; } = new("unit");

    public static PrimitiveTypeSymbol Error { get; } = new("error");

    public static PrimitiveTypeSymbol FromLiteral(object? value)
    {
        return value switch
        {
            bool => Bool,
            char => Char,
            string => String,
            double or float or decimal => Float,
            sbyte or byte or short or ushort or int or uint or long or ulong => Int,
            null => Unit,
            _ => Error
        };
    }

    public static IReadOnlyList<PrimitiveTypeSymbol> All { get; } =
    [
        Int,
        Float,
        Bool,
        String,
        Char,
        Unit,
        Error
    ];
}

public enum UserDefinedTypeKind
{
    Struct,
    Class,
    Enum
}

public sealed class GenericTypeParameterSymbol : TypeSymbol
{
    public GenericTypeParameterSymbol(string name, int position)
        : base(name)
    {
        Position = position;
    }

    public int Position { get; }
}

public sealed class FieldSymbol
{
    public FieldSymbol(string name, TypeSymbol type)
    {
        Name = name;
        Type = type;
    }

    public string Name { get; }

    public TypeSymbol Type { get; }
}

public sealed class EnumVariantSymbol
{
    public EnumVariantSymbol(string name, TypeSymbol? payloadType)
    {
        Name = name;
        PayloadType = payloadType;
    }

    public string Name { get; }

    public TypeSymbol? PayloadType { get; }
}

public sealed class UserDefinedTypeSymbol : TypeSymbol
{
    public UserDefinedTypeSymbol(string name, UserDefinedTypeKind kind, IReadOnlyList<GenericTypeParameterSymbol> typeParameters)
        : base(name)
    {
        Kind = kind;
        TypeParameters = typeParameters;
    }

    public UserDefinedTypeKind Kind { get; }

    public IReadOnlyList<GenericTypeParameterSymbol> TypeParameters { get; }

    public IReadOnlyList<FieldSymbol> Fields { get; private set; } = [];

    public IReadOnlyList<EnumVariantSymbol> Variants { get; private set; } = [];

    public void SetFields(IReadOnlyList<FieldSymbol> fields)
    {
        Fields = fields;
    }

    public void SetVariants(IReadOnlyList<EnumVariantSymbol> variants)
    {
        Variants = variants;
    }
}

public sealed class ConstructedTypeSymbol : TypeSymbol
{
    public ConstructedTypeSymbol(UserDefinedTypeSymbol genericDefinition, IReadOnlyList<TypeSymbol> typeArguments)
        : base(BuildName(genericDefinition, typeArguments))
    {
        GenericDefinition = genericDefinition;
        TypeArguments = typeArguments;
    }

    public UserDefinedTypeSymbol GenericDefinition { get; }

    public IReadOnlyList<TypeSymbol> TypeArguments { get; }

    private static string BuildName(UserDefinedTypeSymbol definition, IReadOnlyList<TypeSymbol> typeArguments)
    {
        return $"{definition.Name}<{string.Join(", ", typeArguments.Select(argument => argument.Name))}>";
    }
}
