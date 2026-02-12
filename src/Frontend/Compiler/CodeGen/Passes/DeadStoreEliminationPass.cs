namespace Oaf.Frontend.Compiler.CodeGen.Passes;

public sealed class DeadStoreEliminationPass : IIrOptimizationPass
{
    public string Name => "dead-store-elimination";

    public bool Run(IrFunction function)
    {
        if (function.Blocks.Count == 0)
        {
            return false;
        }

        var labelToIndex = BuildLabelToIndex(function);
        var successors = BuildSuccessors(function, labelToIndex);
        var liveIn = new HashSet<string>[function.Blocks.Count];
        var liveOut = new HashSet<string>[function.Blocks.Count];
        for (var i = 0; i < function.Blocks.Count; i++)
        {
            liveIn[i] = new HashSet<string>(StringComparer.Ordinal);
            liveOut[i] = new HashSet<string>(StringComparer.Ordinal);
        }

        ComputeLiveness(function, successors, liveIn, liveOut);
        return EliminateDeadStores(function, liveOut);
    }

    private static Dictionary<string, int> BuildLabelToIndex(IrFunction function)
    {
        var labels = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < function.Blocks.Count; i++)
        {
            labels[function.Blocks[i].Label] = i;
        }

        return labels;
    }

    private static List<int>[] BuildSuccessors(IrFunction function, IReadOnlyDictionary<string, int> labelToIndex)
    {
        var successors = new List<int>[function.Blocks.Count];
        for (var i = 0; i < function.Blocks.Count; i++)
        {
            successors[i] = [];
            var block = function.Blocks[i];
            if (block.Instructions.Count == 0)
            {
                if (i + 1 < function.Blocks.Count)
                {
                    successors[i].Add(i + 1);
                }

                continue;
            }

            var terminator = block.Instructions[^1];
            switch (terminator)
            {
                case IrBranchInstruction branch:
                    AddSuccessor(successors[i], labelToIndex, branch.TrueLabel);
                    AddSuccessor(successors[i], labelToIndex, branch.FalseLabel);
                    break;

                case IrJumpInstruction jump:
                    AddSuccessor(successors[i], labelToIndex, jump.TargetLabel);
                    break;

                case IrReturnInstruction:
                    break;

                default:
                    if (i + 1 < function.Blocks.Count)
                    {
                        successors[i].Add(i + 1);
                    }

                    break;
            }
        }

        return successors;
    }

    private static void AddSuccessor(List<int> successors, IReadOnlyDictionary<string, int> labelToIndex, string label)
    {
        if (!labelToIndex.TryGetValue(label, out var target))
        {
            return;
        }

        if (!successors.Contains(target))
        {
            successors.Add(target);
        }
    }

    private static void ComputeLiveness(
        IrFunction function,
        IReadOnlyList<List<int>> successors,
        IReadOnlyList<HashSet<string>> liveIn,
        IReadOnlyList<HashSet<string>> liveOut)
    {
        var changed = true;
        while (changed)
        {
            changed = false;

            for (var blockIndex = function.Blocks.Count - 1; blockIndex >= 0; blockIndex--)
            {
                var block = function.Blocks[blockIndex];
                var nextLiveOut = new HashSet<string>(StringComparer.Ordinal);
                foreach (var successor in successors[blockIndex])
                {
                    nextLiveOut.UnionWith(liveIn[successor]);
                }

                if (!liveOut[blockIndex].SetEquals(nextLiveOut))
                {
                    liveOut[blockIndex].Clear();
                    liveOut[blockIndex].UnionWith(nextLiveOut);
                    changed = true;
                }

                var nextLiveIn = ComputeLiveBeforeBlock(block, liveOut[blockIndex]);
                if (!liveIn[blockIndex].SetEquals(nextLiveIn))
                {
                    liveIn[blockIndex].Clear();
                    liveIn[blockIndex].UnionWith(nextLiveIn);
                    changed = true;
                }
            }
        }
    }

    private static HashSet<string> ComputeLiveBeforeBlock(IrBasicBlock block, IReadOnlySet<string> liveAfterBlock)
    {
        var live = new HashSet<string>(liveAfterBlock, StringComparer.Ordinal);

        for (var i = block.Instructions.Count - 1; i >= 0; i--)
        {
            ApplyInstructionToLiveness(block.Instructions[i], live);
        }

        return live;
    }

    private static bool EliminateDeadStores(IrFunction function, IReadOnlyList<HashSet<string>> liveOut)
    {
        var changed = false;

        for (var blockIndex = 0; blockIndex < function.Blocks.Count; blockIndex++)
        {
            var block = function.Blocks[blockIndex];
            var live = new HashSet<string>(liveOut[blockIndex], StringComparer.Ordinal);

            for (var i = block.Instructions.Count - 1; i >= 0; i--)
            {
                var instruction = block.Instructions[i];

                if (instruction is IrAssignInstruction assign
                    && assign.Destination is IrVariableValue destination
                    && !live.Contains(destination.Name))
                {
                    block.Instructions.RemoveAt(i);
                    changed = true;
                    continue;
                }

                ApplyInstructionToLiveness(instruction, live);
            }
        }

        return changed;
    }

    private static void ApplyInstructionToLiveness(IrInstruction instruction, HashSet<string> live)
    {
        if (instruction is IrAssignInstruction assign && assign.Destination is IrVariableValue destination)
        {
            live.Remove(destination.Name);
            AddReadVariables(assign.Source, live);
            return;
        }

        foreach (var variable in instruction.ReadValues().OfType<IrVariableValue>())
        {
            live.Add(variable.Name);
        }
    }

    private static void AddReadVariables(IrValue value, HashSet<string> live)
    {
        if (value is IrVariableValue variable)
        {
            live.Add(variable.Name);
        }
    }
}
