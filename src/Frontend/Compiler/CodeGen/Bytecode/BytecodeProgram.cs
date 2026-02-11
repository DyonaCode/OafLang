using Oaf.Frontend.Compiler.CodeGen;

namespace Oaf.Frontend.Compiler.CodeGen.Bytecode;

public sealed class BytecodeFunction
{
    public BytecodeFunction(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public List<BytecodeInstruction> Instructions { get; } = new();

    public List<object?> Constants { get; } = new();

    public int SlotCount { get; set; }

    public IrTypeKind? ReturnTypeKind { get; set; }
}

public sealed class BytecodeProgram
{
    public string EntryFunctionName { get; set; } = "main";

    public List<BytecodeFunction> Functions { get; } = new();

    public BytecodeFunction? FindFunction(string name)
    {
        return Functions.FirstOrDefault(function => string.Equals(function.Name, name, StringComparison.Ordinal));
    }
}
