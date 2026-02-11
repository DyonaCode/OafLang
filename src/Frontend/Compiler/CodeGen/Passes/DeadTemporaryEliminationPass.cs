namespace Oaf.Frontend.Compiler.CodeGen.Passes;

public sealed class DeadTemporaryEliminationPass : IIrOptimizationPass
{
    public string Name => "dead-temporary-elimination";

    public bool Run(IrFunction function)
    {
        var changed = false;

        foreach (var block in function.Blocks)
        {
            var usedTemporaries = new HashSet<string>(StringComparer.Ordinal);

            for (var i = block.Instructions.Count - 1; i >= 0; i--)
            {
                var instruction = block.Instructions[i];
                var destination = instruction.WrittenTemporaryName;

                if (destination is not null
                    && !usedTemporaries.Contains(destination)
                    && !instruction.HasSideEffects)
                {
                    block.Instructions.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (destination is not null)
                {
                    usedTemporaries.Remove(destination);
                }

                foreach (var temporary in instruction.ReadValues().OfType<IrTemporaryValue>())
                {
                    usedTemporaries.Add(temporary.Name);
                }
            }
        }

        return changed;
    }
}
