namespace Oaf.Frontend.Compiler.CodeGen;

public interface IIrOptimizationPass
{
    string Name { get; }

    bool Run(IrFunction function);
}

public sealed class IrOptimizationPipeline
{
    private readonly List<IIrOptimizationPass> _passes = new();

    public IReadOnlyList<IIrOptimizationPass> Passes => _passes;

    public void AddPass(IIrOptimizationPass pass)
    {
        _passes.Add(pass);
    }

    public bool Run(IrModule module)
    {
        var changed = false;

        foreach (var function in module.Functions)
        {
            foreach (var pass in _passes)
            {
                changed |= pass.Run(function);
            }
        }

        return changed;
    }
}
