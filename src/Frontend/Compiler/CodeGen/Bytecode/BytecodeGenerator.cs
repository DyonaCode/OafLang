using Oaf.Frontend.Compiler.CodeGen;

namespace Oaf.Frontend.Compiler.CodeGen.Bytecode;

public sealed class BytecodeGenerator
{
    private sealed class FunctionGenerationContext
    {
        public FunctionGenerationContext(BytecodeFunction function)
        {
            Function = function;
        }

        public BytecodeFunction Function { get; }

        public Dictionary<string, int> Slots { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> ConstantIndices { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> BlockOffsets { get; } = new(StringComparer.Ordinal);

        public List<LabelFixup> Fixups { get; } = new();

        public Stack<int> ParallelForBeginStack { get; } = new();

        public int NextSlot { get; set; }
    }

    private readonly struct LabelFixup
    {
        public LabelFixup(int instructionIndex, int operandIndex, string label)
        {
            InstructionIndex = instructionIndex;
            OperandIndex = operandIndex;
            Label = label;
        }

        public int InstructionIndex { get; }

        public int OperandIndex { get; }

        public string Label { get; }
    }

    public BytecodeProgram Generate(IrModule module)
    {
        var program = new BytecodeProgram();

        foreach (var irFunction in module.Functions)
        {
            var bytecodeFunction = new BytecodeFunction(irFunction.Name)
            {
                ReturnTypeKind = InferReturnTypeKind(irFunction)
            };
            var context = new FunctionGenerationContext(bytecodeFunction);

            EmitFunction(irFunction, context);
            PatchLabels(context);
            OptimizeInstructionStream(context.Function);

            context.Function.SlotCount = context.NextSlot;
            program.Functions.Add(bytecodeFunction);
        }

        if (module.Functions.Count > 0)
        {
            program.EntryFunctionName = module.Functions[0].Name;
        }

        return program;
    }

    private static IrTypeKind? InferReturnTypeKind(IrFunction function)
    {
        IrTypeKind? inferred = null;
        foreach (var block in function.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                if (instruction is not IrReturnInstruction irReturn)
                {
                    continue;
                }

                var returnKind = irReturn.Value?.Type.Kind ?? IrTypeKind.Void;
                if (returnKind == IrTypeKind.Unknown)
                {
                    return null;
                }

                if (inferred is null)
                {
                    inferred = returnKind;
                    continue;
                }

                if (inferred != returnKind)
                {
                    return null;
                }
            }
        }

        return inferred ?? IrTypeKind.Void;
    }

    private void EmitFunction(IrFunction irFunction, FunctionGenerationContext context)
    {
        foreach (var block in irFunction.Blocks)
        {
            context.BlockOffsets[block.Label] = context.Function.Instructions.Count;

            foreach (var instruction in block.Instructions)
            {
                EmitInstruction(instruction, context);
            }
        }
    }

    private void EmitInstruction(IrInstruction instruction, FunctionGenerationContext context)
    {
        switch (instruction)
        {
            case IrAssignInstruction assign:
                {
                    var destination = GetSlot(assign.Destination, context);

                    if (assign.Source is IrConstantValue constant)
                    {
                        var constIndex = GetConstantIndex(constant, context);
                        context.Function.Instructions.Add(new BytecodeInstruction(BytecodeOpCode.LoadConst, destination, constIndex));
                    }
                    else
                    {
                        var source = GetSlot(assign.Source, context);
                        context.Function.Instructions.Add(new BytecodeInstruction(BytecodeOpCode.Move, destination, source));
                    }

                    break;
                }

            case IrUnaryInstruction unary:
                {
                    var destination = GetSlot(unary.Destination, context);
                    var operand = MaterializeOperand(unary.Operand, context);
                    var operation = MapUnaryOperator(unary.Operation);
                    context.Function.Instructions.Add(new BytecodeInstruction(BytecodeOpCode.Unary, destination, (int)operation, operand));
                    break;
                }

            case IrBinaryInstruction binary:
                {
                    var destination = GetSlot(binary.Destination, context);
                    var operation = MapBinaryOperator(binary.Operation);
                    if (IsIntSpecializable(binary.Operation, binary.Left, binary.Right))
                    {
                        if (TryGetIntegralConstantIndex(binary.Right, context, out var rightConstantIndex))
                        {
                            var left = MaterializeOperand(binary.Left, context);
                            context.Function.Instructions.Add(new BytecodeInstruction(
                                BytecodeOpCode.BinaryIntConstRight,
                                destination,
                                (int)operation,
                                left,
                                rightConstantIndex));
                            break;
                        }

                        if (TryGetIntegralConstantIndex(binary.Left, context, out var leftConstantIndex) && IsCommutative(operation))
                        {
                            var left = MaterializeOperand(binary.Right, context);
                            context.Function.Instructions.Add(new BytecodeInstruction(
                                BytecodeOpCode.BinaryIntConstRight,
                                destination,
                                (int)operation,
                                left,
                                leftConstantIndex));
                            break;
                        }

                        var intLeft = MaterializeOperand(binary.Left, context);
                        var intRight = MaterializeOperand(binary.Right, context);
                        context.Function.Instructions.Add(new BytecodeInstruction(
                            BytecodeOpCode.BinaryInt,
                            destination,
                            (int)operation,
                            intLeft,
                            intRight));
                        break;
                    }

                    var genericLeft = MaterializeOperand(binary.Left, context);
                    var genericRight = MaterializeOperand(binary.Right, context);
                    context.Function.Instructions.Add(new BytecodeInstruction(BytecodeOpCode.Binary, destination, (int)operation, genericLeft, genericRight));
                    break;
                }

            case IrCastInstruction cast:
                {
                    var destination = GetSlot(cast.Destination, context);
                    var source = MaterializeOperand(cast.Source, context);
                    context.Function.Instructions.Add(new BytecodeInstruction(BytecodeOpCode.Cast, destination, source, (int)cast.TargetType.Kind));
                    break;
                }

            case IrPrintInstruction print:
                {
                    var value = MaterializeOperand(print.Value, context);
                    context.Function.Instructions.Add(new BytecodeInstruction(BytecodeOpCode.Print, value));
                    break;
                }

            case IrThrowInstruction throwInstruction:
                {
                    var error = throwInstruction.Error is null ? -1 : MaterializeOperand(throwInstruction.Error, context);
                    var detail = throwInstruction.Detail is null ? -1 : MaterializeOperand(throwInstruction.Detail, context);
                    context.Function.Instructions.Add(new BytecodeInstruction(BytecodeOpCode.Throw, error, detail));
                    break;
                }

            case IrArrayCreateInstruction arrayCreate:
                {
                    var destination = GetSlot(arrayCreate.Destination, context);
                    var length = MaterializeOperand(arrayCreate.Length, context);
                    context.Function.Instructions.Add(new BytecodeInstruction(BytecodeOpCode.ArrayCreate, destination, length));
                    break;
                }

            case IrArrayGetInstruction arrayGet:
                {
                    var destination = GetSlot(arrayGet.Destination, context);
                    var array = MaterializeOperand(arrayGet.Array, context);
                    var index = MaterializeOperand(arrayGet.Index, context);
                    context.Function.Instructions.Add(new BytecodeInstruction(BytecodeOpCode.ArrayGet, destination, array, index));
                    break;
                }

            case IrArraySetInstruction arraySet:
                {
                    var array = MaterializeOperand(arraySet.Array, context);
                    var index = MaterializeOperand(arraySet.Index, context);
                    var value = MaterializeOperand(arraySet.Value, context);
                    context.Function.Instructions.Add(new BytecodeInstruction(BytecodeOpCode.ArraySet, array, index, value));
                    break;
                }

            case IrParallelForBeginInstruction parallelBegin:
                {
                    var count = MaterializeOperand(parallelBegin.Count, context);
                    var iterationSlot = GetSlot(parallelBegin.IterationVariable, context);
                    var beginIndex = context.Function.Instructions.Count;
                    context.Function.Instructions.Add(new BytecodeInstruction(BytecodeOpCode.ParallelForBegin, count, iterationSlot, -1));
                    context.ParallelForBeginStack.Push(beginIndex);
                    break;
                }

            case IrParallelForEndInstruction:
                {
                    var endIndex = context.Function.Instructions.Count;
                    context.Function.Instructions.Add(new BytecodeInstruction(BytecodeOpCode.ParallelForEnd));

                    if (context.ParallelForBeginStack.Count > 0)
                    {
                        var beginIndex = context.ParallelForBeginStack.Pop();
                        context.Function.Instructions[beginIndex].C = endIndex;
                    }

                    break;
                }

            case IrParallelReduceAddInstruction parallelReduceAdd:
                {
                    var target = GetSlot(parallelReduceAdd.Target, context);
                    var value = MaterializeOperand(parallelReduceAdd.Value, context);
                    context.Function.Instructions.Add(new BytecodeInstruction(BytecodeOpCode.ParallelReduceAdd, target, value));
                    break;
                }

            case IrBranchInstruction branch:
                {
                    var condition = MaterializeOperand(branch.Condition, context);

                    var branchInstruction = new BytecodeInstruction(BytecodeOpCode.JumpIfTrue, condition, -1);
                    context.Fixups.Add(new LabelFixup(context.Function.Instructions.Count, operandIndex: 1, branch.TrueLabel));
                    context.Function.Instructions.Add(branchInstruction);

                    var jumpInstruction = new BytecodeInstruction(BytecodeOpCode.Jump, -1);
                    context.Fixups.Add(new LabelFixup(context.Function.Instructions.Count, operandIndex: 0, branch.FalseLabel));
                    context.Function.Instructions.Add(jumpInstruction);
                    break;
                }

            case IrJumpInstruction jump:
                {
                    var instructionIndex = context.Function.Instructions.Count;
                    context.Function.Instructions.Add(new BytecodeInstruction(BytecodeOpCode.Jump, -1));
                    context.Fixups.Add(new LabelFixup(instructionIndex, operandIndex: 0, jump.TargetLabel));
                    break;
                }

            case IrReturnInstruction returnInstruction:
                {
                    if (returnInstruction.Value is null)
                    {
                        context.Function.Instructions.Add(new BytecodeInstruction(BytecodeOpCode.Return, -1));
                    }
                    else
                    {
                        var valueSlot = MaterializeOperand(returnInstruction.Value, context);
                        context.Function.Instructions.Add(new BytecodeInstruction(BytecodeOpCode.Return, valueSlot));
                    }

                    break;
                }
        }
    }

    private void PatchLabels(FunctionGenerationContext context)
    {
        foreach (var fixup in context.Fixups)
        {
            if (!context.BlockOffsets.TryGetValue(fixup.Label, out var target))
            {
                target = context.Function.Instructions.Count;
            }

            var instruction = context.Function.Instructions[fixup.InstructionIndex];
            switch (fixup.OperandIndex)
            {
                case 0:
                    instruction.A = target;
                    break;
                case 1:
                    instruction.B = target;
                    break;
                case 2:
                    instruction.C = target;
                    break;
                case 3:
                    instruction.D = target;
                    break;
            }
        }

        while (context.ParallelForBeginStack.Count > 0)
        {
            var beginIndex = context.ParallelForBeginStack.Pop();
            context.Function.Instructions[beginIndex].C = context.Function.Instructions.Count;
        }
    }

    private static void OptimizeInstructionStream(BytecodeFunction function)
    {
        if (function.Instructions.Count < 2)
        {
            return;
        }

        var instructions = function.Instructions;
        var jumpTargets = CollectJumpTargets(instructions);
        var removable = new bool[instructions.Count];

        for (var i = 0; i < instructions.Count - 1; i++)
        {
            if (jumpTargets.Contains(i + 1))
            {
                continue;
            }

            var producer = instructions[i];
            var jumpIfTrue = instructions[i + 1];
            if (jumpIfTrue.OpCode != BytecodeOpCode.JumpIfTrue || jumpIfTrue.A != producer.A)
            {
                continue;
            }

            if (producer.OpCode == BytecodeOpCode.BinaryInt && !IsSlotReadAfter(instructions, producer.A, i + 2))
            {
                instructions[i] = new BytecodeInstruction(
                    BytecodeOpCode.JumpIfBinaryIntTrue,
                    producer.C,
                    producer.D,
                    producer.B,
                    jumpIfTrue.B);
                removable[i + 1] = true;
                continue;
            }

            if (producer.OpCode == BytecodeOpCode.BinaryIntConstRight && !IsSlotReadAfter(instructions, producer.A, i + 2))
            {
                instructions[i] = new BytecodeInstruction(
                    BytecodeOpCode.JumpIfBinaryIntConstRightTrue,
                    producer.C,
                    producer.D,
                    producer.B,
                    jumpIfTrue.B);
                removable[i + 1] = true;
            }
        }

        jumpTargets = CollectJumpTargets(instructions);
        for (var i = 1; i < instructions.Count; i++)
        {
            if (removable[i] || removable[i - 1] || jumpTargets.Contains(i))
            {
                continue;
            }

            var move = instructions[i];
            var producer = instructions[i - 1];
            if (move.OpCode != BytecodeOpCode.Move || !CanRedirectDestination(producer.OpCode))
            {
                continue;
            }

            if (producer.A != move.B)
            {
                continue;
            }

            if (IsSlotReadAfter(instructions, move.B, i + 1))
            {
                continue;
            }

            producer.A = move.A;
            removable[i] = true;
        }

        if (!removable.Any(static flag => flag))
        {
            return;
        }

        var oldCount = instructions.Count;
        var oldToNewIndex = new int[oldCount];
        var newInstructions = new List<BytecodeInstruction>(oldCount);
        var newIndex = 0;

        for (var i = 0; i < oldCount; i++)
        {
            if (removable[i])
            {
                oldToNewIndex[i] = -1;
                continue;
            }

            oldToNewIndex[i] = newIndex;
            newInstructions.Add(instructions[i]);
            newIndex++;
        }

        foreach (var instruction in newInstructions)
        {
            switch (instruction.OpCode)
            {
                case BytecodeOpCode.Jump:
                    instruction.A = RemapJumpTarget(instruction.A, oldToNewIndex, oldCount, newInstructions.Count);
                    break;
                case BytecodeOpCode.JumpIfTrue:
                case BytecodeOpCode.JumpIfFalse:
                    instruction.B = RemapJumpTarget(instruction.B, oldToNewIndex, oldCount, newInstructions.Count);
                    break;
                case BytecodeOpCode.JumpIfBinaryIntTrue:
                case BytecodeOpCode.JumpIfBinaryIntConstRightTrue:
                    instruction.D = RemapJumpTarget(instruction.D, oldToNewIndex, oldCount, newInstructions.Count);
                    break;

                case BytecodeOpCode.ParallelForBegin:
                    instruction.C = RemapJumpTarget(instruction.C, oldToNewIndex, oldCount, newInstructions.Count);
                    break;
            }
        }

        function.Instructions.Clear();
        function.Instructions.AddRange(newInstructions);
    }

    private static HashSet<int> CollectJumpTargets(IReadOnlyList<BytecodeInstruction> instructions)
    {
        var targets = new HashSet<int>();
        foreach (var instruction in instructions)
        {
            switch (instruction.OpCode)
            {
                case BytecodeOpCode.Jump:
                    targets.Add(instruction.A);
                    break;
                case BytecodeOpCode.JumpIfTrue:
                case BytecodeOpCode.JumpIfFalse:
                    targets.Add(instruction.B);
                    break;
                case BytecodeOpCode.JumpIfBinaryIntTrue:
                case BytecodeOpCode.JumpIfBinaryIntConstRightTrue:
                    targets.Add(instruction.D);
                    break;
            }
        }

        return targets;
    }

    private static bool CanRedirectDestination(BytecodeOpCode opCode)
    {
        return opCode is BytecodeOpCode.LoadConst
            or BytecodeOpCode.Move
            or BytecodeOpCode.Unary
            or BytecodeOpCode.Binary
            or BytecodeOpCode.BinaryInt
            or BytecodeOpCode.BinaryIntConstRight
            or BytecodeOpCode.Cast
            or BytecodeOpCode.ArrayCreate
            or BytecodeOpCode.ArrayGet;
    }

    private static bool IsSlotReadAfter(IReadOnlyList<BytecodeInstruction> instructions, int slot, int startIndex)
    {
        for (var i = startIndex; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            if (ReadsSlot(instruction, slot))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReadsSlot(BytecodeInstruction instruction, int slot)
    {
        return instruction.OpCode switch
        {
            BytecodeOpCode.Move => instruction.B == slot,
            BytecodeOpCode.Unary => instruction.C == slot,
            BytecodeOpCode.Binary => instruction.C == slot || instruction.D == slot,
            BytecodeOpCode.BinaryInt => instruction.C == slot || instruction.D == slot,
            BytecodeOpCode.BinaryIntConstRight => instruction.C == slot,
            BytecodeOpCode.Cast => instruction.B == slot,
            BytecodeOpCode.Print => instruction.A == slot,
            BytecodeOpCode.Throw => instruction.A == slot || instruction.B == slot,
            BytecodeOpCode.ArrayCreate => instruction.B == slot,
            BytecodeOpCode.ArrayGet => instruction.B == slot || instruction.C == slot,
            BytecodeOpCode.ArraySet => instruction.A == slot || instruction.B == slot || instruction.C == slot,
            BytecodeOpCode.ParallelForBegin => instruction.A == slot,
            BytecodeOpCode.ParallelReduceAdd => instruction.A == slot || instruction.B == slot,
            BytecodeOpCode.JumpIfTrue or BytecodeOpCode.JumpIfFalse => instruction.A == slot,
            BytecodeOpCode.JumpIfBinaryIntTrue => instruction.A == slot || instruction.B == slot,
            BytecodeOpCode.JumpIfBinaryIntConstRightTrue => instruction.A == slot,
            BytecodeOpCode.Return => instruction.A == slot,
            _ => false
        };
    }

    private static int RemapJumpTarget(int target, IReadOnlyList<int> oldToNewIndex, int oldCount, int newCount)
    {
        if (target < 0)
        {
            return target;
        }

        if (target >= oldCount)
        {
            return newCount;
        }

        var mapped = oldToNewIndex[target];
        if (mapped >= 0)
        {
            return mapped;
        }

        for (var i = target + 1; i < oldCount; i++)
        {
            if (oldToNewIndex[i] >= 0)
            {
                return oldToNewIndex[i];
            }
        }

        return newCount;
    }

    private int MaterializeOperand(IrValue value, FunctionGenerationContext context)
    {
        if (value is not IrConstantValue constant)
        {
            return GetSlot(value, context);
        }

        var slot = context.NextSlot++;
        var constantIndex = GetConstantIndex(constant, context);
        context.Function.Instructions.Add(new BytecodeInstruction(BytecodeOpCode.LoadConst, slot, constantIndex));
        return slot;
    }

    private int GetSlot(IrValue value, FunctionGenerationContext context)
    {
        var key = value switch
        {
            IrTemporaryValue temporary => $"tmp:{temporary.Name}",
            IrVariableValue variable => $"var:{variable.Name}",
            _ => $"value:{value.DisplayText}"
        };

        if (context.Slots.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var slot = context.NextSlot;
        context.NextSlot++;
        context.Slots[key] = slot;
        return slot;
    }

    private int GetConstantIndex(IrConstantValue constant, FunctionGenerationContext context)
    {
        var key = $"{constant.Type.Kind}:{constant.Value?.ToString() ?? "null"}";

        if (context.ConstantIndices.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var index = context.Function.Constants.Count;
        context.Function.Constants.Add(constant.Value);
        context.ConstantIndices[key] = index;
        return index;
    }

    private static BytecodeUnaryOperator MapUnaryOperator(IrUnaryOperator operation)
    {
        return operation switch
        {
            IrUnaryOperator.Identity => BytecodeUnaryOperator.Identity,
            IrUnaryOperator.Negate => BytecodeUnaryOperator.Negate,
            IrUnaryOperator.LogicalNot => BytecodeUnaryOperator.LogicalNot,
            IrUnaryOperator.BitwiseNot => BytecodeUnaryOperator.BitwiseNot,
            _ => BytecodeUnaryOperator.Identity
        };
    }

    private static BytecodeBinaryOperator MapBinaryOperator(IrBinaryOperator operation)
    {
        return operation switch
        {
            IrBinaryOperator.Add => BytecodeBinaryOperator.Add,
            IrBinaryOperator.Subtract => BytecodeBinaryOperator.Subtract,
            IrBinaryOperator.Multiply => BytecodeBinaryOperator.Multiply,
            IrBinaryOperator.Divide => BytecodeBinaryOperator.Divide,
            IrBinaryOperator.Modulo => BytecodeBinaryOperator.Modulo,
            IrBinaryOperator.Root => BytecodeBinaryOperator.Root,
            IrBinaryOperator.ShiftLeft => BytecodeBinaryOperator.ShiftLeft,
            IrBinaryOperator.ShiftRight => BytecodeBinaryOperator.ShiftRight,
            IrBinaryOperator.UnsignedShiftLeft => BytecodeBinaryOperator.UnsignedShiftLeft,
            IrBinaryOperator.UnsignedShiftRight => BytecodeBinaryOperator.UnsignedShiftRight,
            IrBinaryOperator.Less => BytecodeBinaryOperator.Less,
            IrBinaryOperator.LessOrEqual => BytecodeBinaryOperator.LessOrEqual,
            IrBinaryOperator.Greater => BytecodeBinaryOperator.Greater,
            IrBinaryOperator.GreaterOrEqual => BytecodeBinaryOperator.GreaterOrEqual,
            IrBinaryOperator.Equal => BytecodeBinaryOperator.Equal,
            IrBinaryOperator.NotEqual => BytecodeBinaryOperator.NotEqual,
            IrBinaryOperator.LogicalAnd => BytecodeBinaryOperator.LogicalAnd,
            IrBinaryOperator.LogicalOr => BytecodeBinaryOperator.LogicalOr,
            IrBinaryOperator.LogicalXor => BytecodeBinaryOperator.LogicalXor,
            IrBinaryOperator.LogicalXand => BytecodeBinaryOperator.LogicalXand,
            IrBinaryOperator.BitAnd => BytecodeBinaryOperator.BitAnd,
            IrBinaryOperator.BitOr => BytecodeBinaryOperator.BitOr,
            IrBinaryOperator.BitXor => BytecodeBinaryOperator.BitXor,
            IrBinaryOperator.BitXand => BytecodeBinaryOperator.BitXand,
            _ => BytecodeBinaryOperator.Add
        };
    }

    private static bool IsIntSpecializable(IrBinaryOperator operation, IrValue left, IrValue right)
    {
        if (!IsIntegerLikeType(left.Type) || !IsIntegerLikeType(right.Type))
        {
            return false;
        }

        return operation switch
        {
            IrBinaryOperator.Add or IrBinaryOperator.Subtract or IrBinaryOperator.Multiply or IrBinaryOperator.Divide or IrBinaryOperator.Modulo
                or IrBinaryOperator.ShiftLeft or IrBinaryOperator.ShiftRight or IrBinaryOperator.UnsignedShiftLeft or IrBinaryOperator.UnsignedShiftRight
                or IrBinaryOperator.Less or IrBinaryOperator.LessOrEqual or IrBinaryOperator.Greater or IrBinaryOperator.GreaterOrEqual
                or IrBinaryOperator.Equal or IrBinaryOperator.NotEqual
                or IrBinaryOperator.LogicalAnd or IrBinaryOperator.LogicalOr or IrBinaryOperator.LogicalXor or IrBinaryOperator.LogicalXand
                or IrBinaryOperator.BitAnd or IrBinaryOperator.BitOr or IrBinaryOperator.BitXor or IrBinaryOperator.BitXand
                => true,
            _ => false
        };
    }

    private static bool IsIntegerLikeType(IrType type)
    {
        return type.Kind is IrTypeKind.Int or IrTypeKind.Bool or IrTypeKind.Char;
    }

    private bool TryGetIntegralConstantIndex(
        IrValue value,
        FunctionGenerationContext context,
        out int constantIndex)
    {
        constantIndex = -1;
        if (value is not IrConstantValue constant || !IsIntegralConstant(constant.Value))
        {
            return false;
        }

        constantIndex = GetConstantIndex(constant, context);
        return true;
    }

    private static bool IsIntegralConstant(object? value)
    {
        return value is bool or char or byte or sbyte or short or ushort or int or uint or long or ulong;
    }

    private static bool IsCommutative(BytecodeBinaryOperator operation)
    {
        return operation is BytecodeBinaryOperator.Add
            or BytecodeBinaryOperator.Multiply
            or BytecodeBinaryOperator.Equal
            or BytecodeBinaryOperator.NotEqual
            or BytecodeBinaryOperator.LogicalAnd
            or BytecodeBinaryOperator.LogicalOr
            or BytecodeBinaryOperator.LogicalXor
            or BytecodeBinaryOperator.LogicalXand
            or BytecodeBinaryOperator.BitAnd
            or BytecodeBinaryOperator.BitOr
            or BytecodeBinaryOperator.BitXor
            or BytecodeBinaryOperator.BitXand;
    }
}
