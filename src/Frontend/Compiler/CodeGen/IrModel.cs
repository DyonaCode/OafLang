namespace Oaf.Frontend.Compiler.CodeGen;

public sealed class IrBasicBlock
{
    public IrBasicBlock(string label)
    {
        Label = label;
    }

    public string Label { get; }

    public List<IrInstruction> Instructions { get; } = new();

    public bool IsTerminated => Instructions.Count > 0 && Instructions[^1].IsTerminator;

    public void Append(IrInstruction instruction)
    {
        if (IsTerminated)
        {
            throw new InvalidOperationException($"Cannot append instruction to terminated block '{Label}'.");
        }

        Instructions.Add(instruction);
    }
}

public sealed class IrFunction
{
    public IrFunction(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public List<IrBasicBlock> Blocks { get; } = new();
}

public sealed class IrModule
{
    public List<IrFunction> Functions { get; } = new();
}
