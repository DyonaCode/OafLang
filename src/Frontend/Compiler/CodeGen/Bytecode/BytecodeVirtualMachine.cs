using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace Oaf.Frontend.Compiler.CodeGen.Bytecode;

public sealed class BytecodeExecutionResult
{
    public BytecodeExecutionResult(bool success, object? returnValue, string? errorMessage)
    {
        Success = success;
        ReturnValue = returnValue;
        ErrorMessage = errorMessage;
    }

    public bool Success { get; }

    public object? ReturnValue { get; }

    public string? ErrorMessage { get; }
}

public sealed class BytecodeVirtualMachine
{
    private readonly struct DecodedFastInstruction
    {
        public DecodedFastInstruction(BytecodeInstruction instruction, long constantRight)
        {
            OpCode = instruction.OpCode;
            A = instruction.A;
            B = instruction.B;
            C = instruction.C;
            D = instruction.D;
            ConstantRight = constantRight;
        }

        public readonly BytecodeOpCode OpCode;

        public readonly int A;

        public readonly int B;

        public readonly int C;

        public readonly int D;

        public readonly long ConstantRight;
    }

    private sealed class IntegerFastPathProgram
    {
        public required int SlotCount { get; init; }

        public required DecodedFastInstruction[] Instructions { get; init; }

        public required long[] Constants { get; init; }

        public required IrTypeKind ReturnTypeKind { get; init; }

        public required bool TrackDynamicReturnTyping { get; init; }

        public bool[]? ConstantBoolFlags { get; init; }
    }

    private static readonly object FastPathCacheLock = new();
    private static readonly Dictionary<BytecodeFunction, IntegerFastPathProgram> FastPathCache = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<BytecodeFunction> UnsupportedFastPathFunctions = new(ReferenceEqualityComparer.Instance);

    public BytecodeExecutionResult Execute(BytecodeProgram program, string? entryFunctionName = null)
    {
        var functionName = string.IsNullOrWhiteSpace(entryFunctionName) ? program.EntryFunctionName : entryFunctionName;
        var function = program.FindFunction(functionName!);

        if (function is null)
        {
            return new BytecodeExecutionResult(false, null, $"Entry function '{functionName}' not found.");
        }

        if (TryExecuteIntegerFastPath(function, out var fastPathResult))
        {
            return fastPathResult;
        }

        var slots = new object?[Math.Max(function.SlotCount, 1)];
        var pc = 0;

        try
        {
            while (pc >= 0 && pc < function.Instructions.Count)
            {
                var instruction = function.Instructions[pc];

                switch (instruction.OpCode)
                {
                    case BytecodeOpCode.Nop:
                        pc++;
                        break;

                    case BytecodeOpCode.LoadConst:
                        slots[instruction.A] = function.Constants[instruction.B];
                        pc++;
                        break;

                    case BytecodeOpCode.Move:
                        slots[instruction.A] = slots[instruction.B];
                        pc++;
                        break;

                    case BytecodeOpCode.Unary:
                        slots[instruction.A] = EvaluateUnary((BytecodeUnaryOperator)instruction.B, slots[instruction.C]);
                        pc++;
                        break;

                    case BytecodeOpCode.Binary:
                        slots[instruction.A] = EvaluateBinary(
                            (BytecodeBinaryOperator)instruction.B,
                            slots[instruction.C],
                            slots[instruction.D]);
                        pc++;
                        break;

                    case BytecodeOpCode.BinaryInt:
                        slots[instruction.A] = EvaluateBinaryIntAsObject(
                            (BytecodeBinaryOperator)instruction.B,
                            ToLong(slots[instruction.C]),
                            ToLong(slots[instruction.D]));
                        pc++;
                        break;

                    case BytecodeOpCode.BinaryIntConstRight:
                        slots[instruction.A] = EvaluateBinaryIntAsObject(
                            (BytecodeBinaryOperator)instruction.B,
                            ToLong(slots[instruction.C]),
                            ToLong(function.Constants[instruction.D]));
                        pc++;
                        break;

                    case BytecodeOpCode.JumpIfBinaryIntTrue:
                    {
                        var value = EvaluateBinaryInt(
                            (BytecodeBinaryOperator)instruction.C,
                            ToLong(slots[instruction.A]),
                            ToLong(slots[instruction.B]),
                            out _);
                        pc = value != 0 ? instruction.D : pc + 1;
                        break;
                    }

                    case BytecodeOpCode.JumpIfBinaryIntConstRightTrue:
                    {
                        var value = EvaluateBinaryInt(
                            (BytecodeBinaryOperator)instruction.C,
                            ToLong(slots[instruction.A]),
                            ToLong(function.Constants[instruction.B]),
                            out _);
                        pc = value != 0 ? instruction.D : pc + 1;
                        break;
                    }

                    case BytecodeOpCode.Cast:
                        slots[instruction.A] = EvaluateCast(slots[instruction.B], (IrTypeKind)instruction.C);
                        pc++;
                        break;

                    case BytecodeOpCode.Jump:
                        pc = instruction.A;
                        break;

                    case BytecodeOpCode.JumpIfTrue:
                        pc = ToBool(slots[instruction.A]) ? instruction.B : pc + 1;
                        break;

                    case BytecodeOpCode.JumpIfFalse:
                        pc = ToBool(slots[instruction.A]) ? pc + 1 : instruction.B;
                        break;

                    case BytecodeOpCode.Print:
                        Console.WriteLine(FormatPrintedValue(slots[instruction.A]));
                        pc++;
                        break;

                    case BytecodeOpCode.Throw:
                    {
                        var errorValue = instruction.A >= 0 ? slots[instruction.A] : null;
                        var detailValue = instruction.B >= 0 ? slots[instruction.B] : null;
                        return new BytecodeExecutionResult(false, null, BuildThrownErrorMessage(errorValue, detailValue));
                    }

                    case BytecodeOpCode.ArrayCreate:
                    {
                        var length = Math.Max(0, (int)ToLong(slots[instruction.B]));
                        slots[instruction.A] = new object?[length];
                        pc++;
                        break;
                    }

                    case BytecodeOpCode.ArrayGet:
                    {
                        var array = RequireArrayValue(slots[instruction.B]);
                        var index = (int)ToLong(slots[instruction.C]);
                        if (index < 0 || index >= array.Length)
                        {
                            throw new IndexOutOfRangeException($"Array index {index} is out of range for length {array.Length}.");
                        }

                        slots[instruction.A] = array[index];
                        pc++;
                        break;
                    }

                    case BytecodeOpCode.ArraySet:
                    {
                        var array = RequireArrayValue(slots[instruction.A]);
                        var index = (int)ToLong(slots[instruction.B]);
                        if (index < 0 || index >= array.Length)
                        {
                            throw new IndexOutOfRangeException($"Array index {index} is out of range for length {array.Length}.");
                        }

                        array[index] = slots[instruction.C];
                        pc++;
                        break;
                    }

                    case BytecodeOpCode.ParallelForBegin:
                    {
                        var endIndex = ResolveParallelForEndIndex(function, pc, instruction);
                        if (endIndex < 0)
                        {
                            return new BytecodeExecutionResult(false, null, $"Unable to resolve parallel loop end for instruction {pc}.");
                        }

                        var count = Math.Max(0L, ToLong(slots[instruction.A]));
                        if (count > 0)
                        {
                            var error = ExecuteParallelForRange(function, slots, instruction.B, count, pc + 1, endIndex);
                            if (error is not null)
                            {
                                return new BytecodeExecutionResult(false, null, error);
                            }
                        }

                        pc = endIndex + 1;
                        break;
                    }

                    case BytecodeOpCode.ParallelForEnd:
                        pc++;
                        break;

                    case BytecodeOpCode.ParallelReduceAdd:
                    {
                        var current = ToLong(slots[instruction.A]);
                        var contribution = ToLong(slots[instruction.B]);
                        slots[instruction.A] = unchecked(current + contribution);
                        pc++;
                        break;
                    }

                    case BytecodeOpCode.Return:
                        return new BytecodeExecutionResult(true, instruction.A < 0 ? null : slots[instruction.A], null);

                    default:
                        return new BytecodeExecutionResult(false, null, $"Unsupported opcode '{instruction.OpCode}'.");
                }
            }

            return new BytecodeExecutionResult(true, null, null);
        }
        catch (Exception ex)
        {
            return new BytecodeExecutionResult(false, null, ex.Message);
        }
    }

    private static int ResolveParallelForEndIndex(BytecodeFunction function, int beginIndex, BytecodeInstruction beginInstruction)
    {
        if (beginInstruction.C > beginIndex
            && beginInstruction.C < function.Instructions.Count
            && function.Instructions[beginInstruction.C].OpCode == BytecodeOpCode.ParallelForEnd)
        {
            return beginInstruction.C;
        }

        var depth = 0;
        for (var index = beginIndex + 1; index < function.Instructions.Count; index++)
        {
            switch (function.Instructions[index].OpCode)
            {
                case BytecodeOpCode.ParallelForBegin:
                    depth++;
                    break;

                case BytecodeOpCode.ParallelForEnd:
                    if (depth == 0)
                    {
                        return index;
                    }

                    depth--;
                    break;
            }
        }

        return -1;
    }

    private static string? ExecuteParallelForRange(
        BytecodeFunction function,
        object?[] sharedSlots,
        int iterationSlot,
        long iterationCount,
        int bodyStart,
        int bodyEndExclusive)
    {
        if (bodyStart < 0 || bodyStart > bodyEndExclusive || bodyEndExclusive > function.Instructions.Count)
        {
            return "Invalid parallel loop body range.";
        }

        string? error = null;
        var reductionMergeLock = new object();

        try
        {
            Parallel.For(
                0L,
                iterationCount,
                () => new Dictionary<int, long>(),
                (iteration, state, localReductions) =>
                {
                    if (Volatile.Read(ref error) is not null)
                    {
                        state.Stop();
                        return localReductions;
                    }

                    var localSlots = (object?[])sharedSlots.Clone();
                    localSlots[iterationSlot] = iteration;

                    if (!TryExecuteInstructionRange(
                            function,
                            localSlots,
                            bodyStart,
                            bodyEndExclusive,
                            localReductions,
                            out var iterationError))
                    {
                        Interlocked.CompareExchange(ref error, iterationError ?? "Parallel loop iteration failed.", null);
                        state.Stop();
                    }

                    return localReductions;
                },
                localReductions =>
                {
                    if (localReductions.Count == 0)
                    {
                        return;
                    }

                    lock (reductionMergeLock)
                    {
                        foreach (var (slot, contribution) in localReductions)
                        {
                            var current = ToLong(sharedSlots[slot]);
                            sharedSlots[slot] = unchecked(current + contribution);
                        }
                    }
                });
        }
        catch (Exception ex)
        {
            var runtimeException = ex is AggregateException aggregate && aggregate.InnerExceptions.Count > 0
                ? aggregate.InnerExceptions[0]
                : ex;
            return runtimeException.Message;
        }

        return error;
    }

    private static bool TryExecuteInstructionRange(
        BytecodeFunction function,
        object?[] slots,
        int start,
        int endExclusive,
        Dictionary<int, long> reductions,
        out string? error)
    {
        error = null;
        var pc = start;

        try
        {
            while (pc < endExclusive)
            {
                var instruction = function.Instructions[pc];
                switch (instruction.OpCode)
                {
                    case BytecodeOpCode.Nop:
                        pc++;
                        break;

                    case BytecodeOpCode.LoadConst:
                        slots[instruction.A] = function.Constants[instruction.B];
                        pc++;
                        break;

                    case BytecodeOpCode.Move:
                        slots[instruction.A] = slots[instruction.B];
                        pc++;
                        break;

                    case BytecodeOpCode.Unary:
                        slots[instruction.A] = EvaluateUnary((BytecodeUnaryOperator)instruction.B, slots[instruction.C]);
                        pc++;
                        break;

                    case BytecodeOpCode.Binary:
                        slots[instruction.A] = EvaluateBinary(
                            (BytecodeBinaryOperator)instruction.B,
                            slots[instruction.C],
                            slots[instruction.D]);
                        pc++;
                        break;

                    case BytecodeOpCode.BinaryInt:
                        slots[instruction.A] = EvaluateBinaryIntAsObject(
                            (BytecodeBinaryOperator)instruction.B,
                            ToLong(slots[instruction.C]),
                            ToLong(slots[instruction.D]));
                        pc++;
                        break;

                    case BytecodeOpCode.BinaryIntConstRight:
                        slots[instruction.A] = EvaluateBinaryIntAsObject(
                            (BytecodeBinaryOperator)instruction.B,
                            ToLong(slots[instruction.C]),
                            ToLong(function.Constants[instruction.D]));
                        pc++;
                        break;

                    case BytecodeOpCode.JumpIfBinaryIntTrue:
                    {
                        var value = EvaluateBinaryInt(
                            (BytecodeBinaryOperator)instruction.C,
                            ToLong(slots[instruction.A]),
                            ToLong(slots[instruction.B]),
                            out _);
                        var target = value != 0 ? instruction.D : pc + 1;
                        if (target < start || target >= endExclusive)
                        {
                            error = "Parallel loop body contains jump outside permitted range.";
                            return false;
                        }

                        pc = target;
                        break;
                    }

                    case BytecodeOpCode.JumpIfBinaryIntConstRightTrue:
                    {
                        var value = EvaluateBinaryInt(
                            (BytecodeBinaryOperator)instruction.C,
                            ToLong(slots[instruction.A]),
                            ToLong(function.Constants[instruction.B]),
                            out _);
                        var target = value != 0 ? instruction.D : pc + 1;
                        if (target < start || target >= endExclusive)
                        {
                            error = "Parallel loop body contains jump outside permitted range.";
                            return false;
                        }

                        pc = target;
                        break;
                    }

                    case BytecodeOpCode.Cast:
                        slots[instruction.A] = EvaluateCast(slots[instruction.B], (IrTypeKind)instruction.C);
                        pc++;
                        break;

                    case BytecodeOpCode.Jump:
                        if (instruction.A < start || instruction.A >= endExclusive)
                        {
                            error = "Parallel loop body contains jump outside permitted range.";
                            return false;
                        }

                        pc = instruction.A;
                        break;

                    case BytecodeOpCode.JumpIfTrue:
                    {
                        var target = ToBool(slots[instruction.A]) ? instruction.B : pc + 1;
                        if (target < start || target >= endExclusive)
                        {
                            error = "Parallel loop body contains jump outside permitted range.";
                            return false;
                        }

                        pc = target;
                        break;
                    }

                    case BytecodeOpCode.JumpIfFalse:
                    {
                        var target = ToBool(slots[instruction.A]) ? pc + 1 : instruction.B;
                        if (target < start || target >= endExclusive)
                        {
                            error = "Parallel loop body contains jump outside permitted range.";
                            return false;
                        }

                        pc = target;
                        break;
                    }

                    case BytecodeOpCode.ArrayCreate:
                    {
                        var length = Math.Max(0, (int)ToLong(slots[instruction.B]));
                        slots[instruction.A] = new object?[length];
                        pc++;
                        break;
                    }

                    case BytecodeOpCode.ArrayGet:
                    {
                        var array = RequireArrayValue(slots[instruction.B]);
                        var index = (int)ToLong(slots[instruction.C]);
                        if (index < 0 || index >= array.Length)
                        {
                            throw new IndexOutOfRangeException($"Array index {index} is out of range for length {array.Length}.");
                        }

                        slots[instruction.A] = array[index];
                        pc++;
                        break;
                    }

                    case BytecodeOpCode.ArraySet:
                    {
                        var array = RequireArrayValue(slots[instruction.A]);
                        var index = (int)ToLong(slots[instruction.B]);
                        if (index < 0 || index >= array.Length)
                        {
                            throw new IndexOutOfRangeException($"Array index {index} is out of range for length {array.Length}.");
                        }

                        array[index] = slots[instruction.C];
                        pc++;
                        break;
                    }

                    case BytecodeOpCode.ParallelForBegin:
                    case BytecodeOpCode.ParallelForEnd:
                        error = "Nested counted paralloops are not supported.";
                        return false;

                    case BytecodeOpCode.ParallelReduceAdd:
                    {
                        var slot = instruction.A;
                        var contribution = ToLong(slots[instruction.B]);
                        if (reductions.TryGetValue(slot, out var current))
                        {
                            reductions[slot] = unchecked(current + contribution);
                        }
                        else
                        {
                            reductions[slot] = contribution;
                        }

                        pc++;
                        break;
                    }

                    case BytecodeOpCode.Print:
                        error = "Jot/print is not supported inside counted paralloop bodies.";
                        return false;

                    case BytecodeOpCode.Throw:
                    {
                        var errorValue = instruction.A >= 0 ? slots[instruction.A] : null;
                        var detailValue = instruction.B >= 0 ? slots[instruction.B] : null;
                        error = BuildThrownErrorMessage(errorValue, detailValue);
                        return false;
                    }

                    case BytecodeOpCode.Return:
                        error = "Return is not supported inside counted paralloop bodies.";
                        return false;

                    default:
                        error = $"Unsupported opcode '{instruction.OpCode}' in counted paralloop body.";
                        return false;
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        return true;
    }

    private static bool TryExecuteIntegerFastPath(BytecodeFunction function, out BytecodeExecutionResult result)
    {
        result = new BytecodeExecutionResult(false, null, "Fast path unavailable.");
        if (!TryGetIntegerFastPathProgram(function, out var fastPathProgram))
        {
            return false;
        }

        var slotCount = fastPathProgram.SlotCount;
        var slots = ArrayPool<long>.Shared.Rent(slotCount);
        Array.Clear(slots, 0, slotCount);
        try
        {
            if (fastPathProgram.TrackDynamicReturnTyping)
            {
                return TryExecuteIntegerFastPathWithDynamicReturnTyping(fastPathProgram, slots, out result);
            }

            return TryExecuteIntegerFastPathWithStaticReturnTyping(fastPathProgram, slots, out result);
        }
        finally
        {
            ArrayPool<long>.Shared.Return(slots);
        }
    }

    private static bool TryExecuteIntegerFastPathWithDynamicReturnTyping(
        IntegerFastPathProgram fastPathProgram,
        long[] slots,
        out BytecodeExecutionResult result)
    {
        result = new BytecodeExecutionResult(false, null, "Fast path unavailable.");

        var constants = fastPathProgram.Constants;
        var constantBoolFlags = fastPathProgram.ConstantBoolFlags;
        if (constantBoolFlags is null)
        {
            return false;
        }

        var boolSlots = ArrayPool<bool>.Shared.Rent(slots.Length);
        Array.Clear(boolSlots, 0, slots.Length);
        try
        {
            var instructions = fastPathProgram.Instructions;
            var pc = 0;
            while ((uint)pc < (uint)instructions.Length)
            {
                ref readonly var instruction = ref instructions[pc];
                switch (instruction.OpCode)
                {
                    case BytecodeOpCode.Nop:
                        pc++;
                        break;

                    case BytecodeOpCode.LoadConst:
                        slots[instruction.A] = constants[instruction.B];
                        boolSlots[instruction.A] = constantBoolFlags[instruction.B];
                        pc++;
                        break;

                    case BytecodeOpCode.Move:
                        slots[instruction.A] = slots[instruction.B];
                        boolSlots[instruction.A] = boolSlots[instruction.B];
                        pc++;
                        break;

                    case BytecodeOpCode.Unary:
                    {
                        var unaryOp = (BytecodeUnaryOperator)instruction.B;
                        var operand = slots[instruction.C];
                        switch (unaryOp)
                        {
                            case BytecodeUnaryOperator.Identity:
                                slots[instruction.A] = operand;
                                boolSlots[instruction.A] = boolSlots[instruction.C];
                                break;
                            case BytecodeUnaryOperator.Negate:
                                slots[instruction.A] = -operand;
                                boolSlots[instruction.A] = false;
                                break;
                            case BytecodeUnaryOperator.LogicalNot:
                                slots[instruction.A] = operand == 0 ? 1 : 0;
                                boolSlots[instruction.A] = true;
                                break;
                            case BytecodeUnaryOperator.BitwiseNot:
                                slots[instruction.A] = ~operand;
                                boolSlots[instruction.A] = false;
                                break;
                        }

                        pc++;
                        break;
                    }

                    case BytecodeOpCode.Binary:
                    case BytecodeOpCode.BinaryInt:
                    {
                        var value = EvaluateBinaryInt(
                            (BytecodeBinaryOperator)instruction.B,
                            slots[instruction.C],
                            slots[instruction.D],
                            out var isBoolResult);
                        slots[instruction.A] = value;
                        boolSlots[instruction.A] = isBoolResult;
                        pc++;
                        break;
                    }

                    case BytecodeOpCode.BinaryIntConstRight:
                    {
                        var operation = (BytecodeBinaryOperator)instruction.B;
                        var left = slots[instruction.C];
                        var right = instruction.ConstantRight;
                        switch (operation)
                        {
                            case BytecodeBinaryOperator.Add:
                                slots[instruction.A] = left + right;
                                boolSlots[instruction.A] = false;
                                break;
                            case BytecodeBinaryOperator.ShiftRight:
                                slots[instruction.A] = left >> (int)right;
                                boolSlots[instruction.A] = false;
                                break;
                            case BytecodeBinaryOperator.Modulo when left >= 0 && right > 0 && IsPositivePowerOfTwo(right):
                                slots[instruction.A] = left & (right - 1);
                                boolSlots[instruction.A] = false;
                                break;
                            default:
                            {
                                var value = EvaluateBinaryInt(operation, left, right, out var isBoolResult);
                                slots[instruction.A] = value;
                                boolSlots[instruction.A] = isBoolResult;
                                break;
                            }
                        }

                        pc++;
                        break;
                    }

                    case BytecodeOpCode.JumpIfBinaryIntTrue:
                    {
                        var value = EvaluateBinaryInt(
                            (BytecodeBinaryOperator)instruction.C,
                            slots[instruction.A],
                            slots[instruction.B],
                            out _);
                        pc = value != 0 ? instruction.D : pc + 1;
                        break;
                    }

                    case BytecodeOpCode.JumpIfBinaryIntConstRightTrue:
                    {
                        var value = EvaluateBinaryInt(
                            (BytecodeBinaryOperator)instruction.C,
                            slots[instruction.A],
                            instruction.ConstantRight,
                            out _);
                        pc = value != 0 ? instruction.D : pc + 1;
                        break;
                    }

                    case BytecodeOpCode.Cast:
                    {
                        var source = slots[instruction.B];
                        switch ((IrTypeKind)instruction.C)
                        {
                            case IrTypeKind.Int:
                            case IrTypeKind.Char:
                                slots[instruction.A] = source;
                                boolSlots[instruction.A] = false;
                                break;
                            case IrTypeKind.Bool:
                                slots[instruction.A] = source != 0 ? 1 : 0;
                                boolSlots[instruction.A] = true;
                                break;
                            default:
                                return false;
                        }

                        pc++;
                        break;
                    }

                    case BytecodeOpCode.Jump:
                        pc = instruction.A;
                        break;

                    case BytecodeOpCode.JumpIfTrue:
                        pc = slots[instruction.A] != 0 ? instruction.B : pc + 1;
                        break;

                    case BytecodeOpCode.JumpIfFalse:
                        pc = slots[instruction.A] == 0 ? instruction.B : pc + 1;
                        break;

                    case BytecodeOpCode.Return:
                    {
                        object? returnValue = null;
                        if (instruction.A >= 0)
                        {
                            returnValue = boolSlots[instruction.A]
                                ? slots[instruction.A] != 0
                                : slots[instruction.A];
                        }

                        result = new BytecodeExecutionResult(true, returnValue, null);
                        return true;
                    }

                    default:
                        return false;
                }
            }

            result = new BytecodeExecutionResult(true, null, null);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            ArrayPool<bool>.Shared.Return(boolSlots);
        }
    }

    private static bool TryExecuteIntegerFastPathWithStaticReturnTyping(
        IntegerFastPathProgram fastPathProgram,
        long[] slots,
        out BytecodeExecutionResult result)
    {
        result = new BytecodeExecutionResult(false, null, "Fast path unavailable.");

        var constants = fastPathProgram.Constants;
        var instructions = fastPathProgram.Instructions;
        var returnTypeKind = fastPathProgram.ReturnTypeKind;

        var pc = 0;
        try
        {
            while ((uint)pc < (uint)instructions.Length)
            {
                ref readonly var instruction = ref instructions[pc];
                switch (instruction.OpCode)
                {
                    case BytecodeOpCode.Nop:
                        pc++;
                        break;

                    case BytecodeOpCode.LoadConst:
                        slots[instruction.A] = constants[instruction.B];
                        pc++;
                        break;

                    case BytecodeOpCode.Move:
                        slots[instruction.A] = slots[instruction.B];
                        pc++;
                        break;

                    case BytecodeOpCode.Unary:
                    {
                        var unaryOp = (BytecodeUnaryOperator)instruction.B;
                        var operand = slots[instruction.C];
                        switch (unaryOp)
                        {
                            case BytecodeUnaryOperator.Identity:
                                slots[instruction.A] = operand;
                                break;
                            case BytecodeUnaryOperator.Negate:
                                slots[instruction.A] = -operand;
                                break;
                            case BytecodeUnaryOperator.LogicalNot:
                                slots[instruction.A] = operand == 0 ? 1 : 0;
                                break;
                            case BytecodeUnaryOperator.BitwiseNot:
                                slots[instruction.A] = ~operand;
                                break;
                        }

                        pc++;
                        break;
                    }

                    case BytecodeOpCode.Binary:
                    case BytecodeOpCode.BinaryInt:
                        slots[instruction.A] = EvaluateBinaryIntValue(
                            (BytecodeBinaryOperator)instruction.B,
                            slots[instruction.C],
                            slots[instruction.D]);
                        pc++;
                        break;

                    case BytecodeOpCode.BinaryIntConstRight:
                    {
                        var operation = (BytecodeBinaryOperator)instruction.B;
                        var left = slots[instruction.C];
                        var right = instruction.ConstantRight;
                        slots[instruction.A] = operation switch
                        {
                            BytecodeBinaryOperator.Add => left + right,
                            BytecodeBinaryOperator.ShiftRight => left >> (int)right,
                            BytecodeBinaryOperator.Modulo when left >= 0 && right > 0 && IsPositivePowerOfTwo(right) => left & (right - 1),
                            _ => EvaluateBinaryIntValue(operation, left, right)
                        };
                        pc++;
                        break;
                    }

                    case BytecodeOpCode.JumpIfBinaryIntTrue:
                    {
                        var value = EvaluateBinaryIntValue(
                            (BytecodeBinaryOperator)instruction.C,
                            slots[instruction.A],
                            slots[instruction.B]);
                        pc = value != 0 ? instruction.D : pc + 1;
                        break;
                    }

                    case BytecodeOpCode.JumpIfBinaryIntConstRightTrue:
                    {
                        var value = EvaluateBinaryIntValue(
                            (BytecodeBinaryOperator)instruction.C,
                            slots[instruction.A],
                            instruction.ConstantRight);
                        pc = value != 0 ? instruction.D : pc + 1;
                        break;
                    }

                    case BytecodeOpCode.Cast:
                    {
                        var source = slots[instruction.B];
                        switch ((IrTypeKind)instruction.C)
                        {
                            case IrTypeKind.Int:
                            case IrTypeKind.Char:
                                slots[instruction.A] = source;
                                break;
                            case IrTypeKind.Bool:
                                slots[instruction.A] = source != 0 ? 1 : 0;
                                break;
                            default:
                                return false;
                        }

                        pc++;
                        break;
                    }

                    case BytecodeOpCode.Jump:
                        pc = instruction.A;
                        break;

                    case BytecodeOpCode.JumpIfTrue:
                        pc = slots[instruction.A] != 0 ? instruction.B : pc + 1;
                        break;

                    case BytecodeOpCode.JumpIfFalse:
                        pc = slots[instruction.A] == 0 ? instruction.B : pc + 1;
                        break;

                    case BytecodeOpCode.Return:
                    {
                        object? returnValue = null;
                        if (instruction.A >= 0)
                        {
                            returnValue = ConvertFastPathReturnValue(slots[instruction.A], returnTypeKind);
                        }

                        result = new BytecodeExecutionResult(true, returnValue, null);
                        return true;
                    }

                    default:
                        return false;
                }
            }

            result = new BytecodeExecutionResult(true, null, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetIntegerFastPathProgram(BytecodeFunction function, out IntegerFastPathProgram program)
    {
        lock (FastPathCacheLock)
        {
            if (FastPathCache.TryGetValue(function, out program!))
            {
                return true;
            }

            if (UnsupportedFastPathFunctions.Contains(function))
            {
                program = default!;
                return false;
            }

            if (!SupportsIntegerFastPath(function))
            {
                UnsupportedFastPathFunctions.Add(function);
                program = default!;
                return false;
            }

            program = BuildIntegerFastPathProgram(function);
            FastPathCache.Add(function, program);
            return true;
        }
    }

    private static IntegerFastPathProgram BuildIntegerFastPathProgram(BytecodeFunction function)
    {
        var instructionCount = function.Instructions.Count;
        var instructions = new DecodedFastInstruction[instructionCount];

        var hasStaticReturnType = function.ReturnTypeKind.HasValue && function.ReturnTypeKind.Value != IrTypeKind.Unknown;
        var constants = new long[function.Constants.Count];
        bool[]? constantBoolFlags = hasStaticReturnType ? null : new bool[function.Constants.Count];
        for (var i = 0; i < function.Constants.Count; i++)
        {
            constants[i] = ToLong(function.Constants[i]);
            if (constantBoolFlags is not null)
            {
                constantBoolFlags[i] = function.Constants[i] is bool;
            }
        }

        for (var i = 0; i < instructionCount; i++)
        {
            var instruction = function.Instructions[i];
            var constantRight = instruction.OpCode switch
            {
                BytecodeOpCode.BinaryIntConstRight => constants[instruction.D],
                BytecodeOpCode.JumpIfBinaryIntConstRightTrue => constants[instruction.B],
                _ => 0L
            };

            instructions[i] = new DecodedFastInstruction(instruction, constantRight);
        }

        return new IntegerFastPathProgram
        {
            SlotCount = Math.Max(function.SlotCount, 1),
            Instructions = instructions,
            Constants = constants,
            ReturnTypeKind = hasStaticReturnType ? function.ReturnTypeKind!.Value : IrTypeKind.Unknown,
            TrackDynamicReturnTyping = !hasStaticReturnType,
            ConstantBoolFlags = constantBoolFlags
        };
    }

    private static bool SupportsIntegerFastPath(BytecodeFunction function)
    {
        foreach (var constant in function.Constants)
        {
            if (!IsFastPathConstant(constant))
            {
                return false;
            }
        }

        foreach (var instruction in function.Instructions)
        {
            switch (instruction.OpCode)
            {
                case BytecodeOpCode.Nop:
                case BytecodeOpCode.LoadConst:
                case BytecodeOpCode.Move:
                case BytecodeOpCode.Jump:
                case BytecodeOpCode.JumpIfTrue:
                case BytecodeOpCode.JumpIfFalse:
                case BytecodeOpCode.Return:
                    break;

                case BytecodeOpCode.JumpIfBinaryIntTrue:
                case BytecodeOpCode.JumpIfBinaryIntConstRightTrue:
                {
                    if (!IsIntegerBinaryOperation((BytecodeBinaryOperator)instruction.C))
                    {
                        return false;
                    }

                    break;
                }

                case BytecodeOpCode.Unary:
                {
                    var unaryOp = (BytecodeUnaryOperator)instruction.B;
                    if (unaryOp is not (BytecodeUnaryOperator.Identity
                        or BytecodeUnaryOperator.Negate
                        or BytecodeUnaryOperator.LogicalNot
                        or BytecodeUnaryOperator.BitwiseNot))
                    {
                        return false;
                    }

                    break;
                }

                case BytecodeOpCode.Binary:
                case BytecodeOpCode.BinaryInt:
                case BytecodeOpCode.BinaryIntConstRight:
                {
                    if (!IsIntegerBinaryOperation((BytecodeBinaryOperator)instruction.B))
                    {
                        return false;
                    }

                    break;
                }

                case BytecodeOpCode.Cast:
                {
                    var castTarget = (IrTypeKind)instruction.C;
                    if (castTarget is not (IrTypeKind.Int or IrTypeKind.Bool or IrTypeKind.Char))
                    {
                        return false;
                    }

                    break;
                }

                default:
                    return false;
            }
        }

        return true;
    }

    private static object ConvertFastPathReturnValue(long value, IrTypeKind returnTypeKind)
    {
        return returnTypeKind switch
        {
            IrTypeKind.Bool => value != 0,
            IrTypeKind.Char => (char)value,
            _ => value
        };
    }

    private static object? EvaluateUnary(BytecodeUnaryOperator operation, object? operand)
    {
        return operation switch
        {
            BytecodeUnaryOperator.Identity => operand,
            BytecodeUnaryOperator.Negate => operand switch
            {
                double floatValue => -floatValue,
                float floatValue => -floatValue,
                decimal decimalValue => -decimalValue,
                _ => -ToLong(operand)
            },
            BytecodeUnaryOperator.LogicalNot => !ToBool(operand),
            BytecodeUnaryOperator.BitwiseNot => ~ToLong(operand),
            _ => operand
        };
    }

    private static object EvaluateBinaryIntAsObject(BytecodeBinaryOperator operation, long left, long right)
    {
        var value = EvaluateBinaryInt(operation, left, right, out var isBoolResult);
        return isBoolResult ? value != 0 : value;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static long EvaluateBinaryIntValue(BytecodeBinaryOperator operation, long left, long right)
    {
        return operation switch
        {
            BytecodeBinaryOperator.Add => left + right,
            BytecodeBinaryOperator.Subtract => left - right,
            BytecodeBinaryOperator.Multiply => left * right,
            BytecodeBinaryOperator.Divide => DivideInt(left, right),
            BytecodeBinaryOperator.Modulo => ModuloInt(left, right),
            BytecodeBinaryOperator.Root => (long)Math.Truncate(Math.Pow(left, 1.0 / right)),
            BytecodeBinaryOperator.ShiftLeft => left << (int)right,
            BytecodeBinaryOperator.ShiftRight => left >> (int)right,
            BytecodeBinaryOperator.UnsignedShiftLeft => (long)((ulong)left << (int)right),
            BytecodeBinaryOperator.UnsignedShiftRight => (long)((ulong)left >> (int)right),
            BytecodeBinaryOperator.Less => left < right ? 1 : 0,
            BytecodeBinaryOperator.LessOrEqual => left <= right ? 1 : 0,
            BytecodeBinaryOperator.Greater => left > right ? 1 : 0,
            BytecodeBinaryOperator.GreaterOrEqual => left >= right ? 1 : 0,
            BytecodeBinaryOperator.Equal => left == right ? 1 : 0,
            BytecodeBinaryOperator.NotEqual => left != right ? 1 : 0,
            BytecodeBinaryOperator.LogicalAnd => left != 0 && right != 0 ? 1 : 0,
            BytecodeBinaryOperator.LogicalOr => left != 0 || right != 0 ? 1 : 0,
            BytecodeBinaryOperator.LogicalXor => (left != 0) ^ (right != 0) ? 1 : 0,
            BytecodeBinaryOperator.LogicalXand => !((left != 0) ^ (right != 0)) ? 1 : 0,
            BytecodeBinaryOperator.BitAnd => left & right,
            BytecodeBinaryOperator.BitOr => left | right,
            BytecodeBinaryOperator.BitXor => left ^ right,
            BytecodeBinaryOperator.BitXand => ~(left ^ right),
            _ => 0
        };
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static long EvaluateBinaryInt(BytecodeBinaryOperator operation, long left, long right, out bool isBoolResult)
    {
        isBoolResult = false;
        return operation switch
        {
            BytecodeBinaryOperator.Add => left + right,
            BytecodeBinaryOperator.Subtract => left - right,
            BytecodeBinaryOperator.Multiply => left * right,
            BytecodeBinaryOperator.Divide => DivideInt(left, right),
            BytecodeBinaryOperator.Modulo => ModuloInt(left, right),
            BytecodeBinaryOperator.Root => (long)Math.Truncate(Math.Pow(left, 1.0 / right)),
            BytecodeBinaryOperator.ShiftLeft => left << (int)right,
            BytecodeBinaryOperator.ShiftRight => left >> (int)right,
            BytecodeBinaryOperator.UnsignedShiftLeft => (long)((ulong)left << (int)right),
            BytecodeBinaryOperator.UnsignedShiftRight => (long)((ulong)left >> (int)right),
            BytecodeBinaryOperator.Less => BoolResult(left < right, out isBoolResult),
            BytecodeBinaryOperator.LessOrEqual => BoolResult(left <= right, out isBoolResult),
            BytecodeBinaryOperator.Greater => BoolResult(left > right, out isBoolResult),
            BytecodeBinaryOperator.GreaterOrEqual => BoolResult(left >= right, out isBoolResult),
            BytecodeBinaryOperator.Equal => BoolResult(left == right, out isBoolResult),
            BytecodeBinaryOperator.NotEqual => BoolResult(left != right, out isBoolResult),
            BytecodeBinaryOperator.LogicalAnd => BoolResult(left != 0 && right != 0, out isBoolResult),
            BytecodeBinaryOperator.LogicalOr => BoolResult(left != 0 || right != 0, out isBoolResult),
            BytecodeBinaryOperator.LogicalXor => BoolResult((left != 0) ^ (right != 0), out isBoolResult),
            BytecodeBinaryOperator.LogicalXand => BoolResult(!((left != 0) ^ (right != 0)), out isBoolResult),
            BytecodeBinaryOperator.BitAnd => left & right,
            BytecodeBinaryOperator.BitOr => left | right,
            BytecodeBinaryOperator.BitXor => left ^ right,
            BytecodeBinaryOperator.BitXand => ~(left ^ right),
            _ => 0
        };
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static long DivideInt(long left, long right)
    {
        if (right == 0)
        {
            return 0;
        }

        if (left >= 0 && right > 0 && IsPositivePowerOfTwo(right))
        {
            var shift = System.Numerics.BitOperations.TrailingZeroCount((ulong)right);
            return left >> shift;
        }

        return left / right;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static long ModuloInt(long left, long right)
    {
        if (right == 0)
        {
            return 0;
        }

        if (left >= 0 && right > 0 && IsPositivePowerOfTwo(right))
        {
            return left & (right - 1);
        }

        return left % right;
    }

    private static long BoolResult(bool value, out bool isBoolResult)
    {
        isBoolResult = true;
        return value ? 1 : 0;
    }

    private static object? EvaluateBinary(BytecodeBinaryOperator operation, object? left, object? right)
    {
        if (operation == BytecodeBinaryOperator.Add && (left is string || right is string))
        {
            return $"{left}{right}";
        }

        if (operation is BytecodeBinaryOperator.LogicalAnd or BytecodeBinaryOperator.LogicalOr
            or BytecodeBinaryOperator.LogicalXor or BytecodeBinaryOperator.LogicalXand)
        {
            var lhs = ToBool(left);
            var rhs = ToBool(right);

            return operation switch
            {
                BytecodeBinaryOperator.LogicalAnd => lhs && rhs,
                BytecodeBinaryOperator.LogicalOr => lhs || rhs,
                BytecodeBinaryOperator.LogicalXor => lhs ^ rhs,
                BytecodeBinaryOperator.LogicalXand => !(lhs ^ rhs),
                _ => false
            };
        }

        if (operation is BytecodeBinaryOperator.BitAnd or BytecodeBinaryOperator.BitOr
            or BytecodeBinaryOperator.BitXor or BytecodeBinaryOperator.BitXand
            or BytecodeBinaryOperator.ShiftLeft or BytecodeBinaryOperator.ShiftRight
            or BytecodeBinaryOperator.UnsignedShiftLeft or BytecodeBinaryOperator.UnsignedShiftRight)
        {
            var lhs = ToLong(left);
            var rhs = (int)ToLong(right);

            return operation switch
            {
                BytecodeBinaryOperator.BitAnd => lhs & ToLong(right),
                BytecodeBinaryOperator.BitOr => lhs | ToLong(right),
                BytecodeBinaryOperator.BitXor => lhs ^ ToLong(right),
                BytecodeBinaryOperator.BitXand => ~(lhs ^ ToLong(right)),
                BytecodeBinaryOperator.ShiftLeft => lhs << rhs,
                BytecodeBinaryOperator.ShiftRight => lhs >> rhs,
                BytecodeBinaryOperator.UnsignedShiftLeft => (long)((ulong)lhs << rhs),
                BytecodeBinaryOperator.UnsignedShiftRight => (long)((ulong)lhs >> rhs),
                _ => 0L
            };
        }

        if (operation is BytecodeBinaryOperator.Equal or BytecodeBinaryOperator.NotEqual)
        {
            var equals = NumericEquals(left, right) || Equals(left, right);
            return operation == BytecodeBinaryOperator.Equal ? equals : !equals;
        }

        if (operation is BytecodeBinaryOperator.Less or BytecodeBinaryOperator.LessOrEqual
            or BytecodeBinaryOperator.Greater or BytecodeBinaryOperator.GreaterOrEqual)
        {
            var lhs = ToDouble(left);
            var rhs = ToDouble(right);

            return operation switch
            {
                BytecodeBinaryOperator.Less => lhs < rhs,
                BytecodeBinaryOperator.LessOrEqual => lhs <= rhs,
                BytecodeBinaryOperator.Greater => lhs > rhs,
                BytecodeBinaryOperator.GreaterOrEqual => lhs >= rhs,
                _ => false
            };
        }

        var leftIsFloat = IsFloating(left);
        var rightIsFloat = IsFloating(right);
        if (leftIsFloat || rightIsFloat || operation is BytecodeBinaryOperator.Divide or BytecodeBinaryOperator.Root)
        {
            var lhs = ToDouble(left);
            var rhs = ToDouble(right);

            return operation switch
            {
                BytecodeBinaryOperator.Add => lhs + rhs,
                BytecodeBinaryOperator.Subtract => lhs - rhs,
                BytecodeBinaryOperator.Multiply => lhs * rhs,
                BytecodeBinaryOperator.Divide => lhs / rhs,
                BytecodeBinaryOperator.Modulo => lhs % rhs,
                BytecodeBinaryOperator.Root => Math.Pow(lhs, 1.0 / rhs),
                _ => 0.0
            };
        }

        var leftInt = ToLong(left);
        var rightInt = ToLong(right);

        return operation switch
        {
            BytecodeBinaryOperator.Add => leftInt + rightInt,
            BytecodeBinaryOperator.Subtract => leftInt - rightInt,
            BytecodeBinaryOperator.Multiply => leftInt * rightInt,
            BytecodeBinaryOperator.Divide => rightInt == 0 ? 0L : leftInt / rightInt,
            BytecodeBinaryOperator.Modulo => rightInt == 0 ? 0L : leftInt % rightInt,
            BytecodeBinaryOperator.Root => Math.Pow(leftInt, 1.0 / rightInt),
            _ => 0L
        };
    }

    private static object? EvaluateCast(object? value, IrTypeKind targetType)
    {
        if (value is null)
        {
            return null;
        }

        return targetType switch
        {
            IrTypeKind.Int => ToLong(value),
            IrTypeKind.Float => ToDouble(value),
            IrTypeKind.Bool => ToBool(value),
            IrTypeKind.Char => (char)ToLong(value),
            IrTypeKind.String => value.ToString() ?? string.Empty,
            _ => value
        };
    }

    private static string FormatPrintedValue(object? value)
    {
        return value switch
        {
            null => "null",
            bool boolean => boolean ? "true" : "false",
            _ => value.ToString() ?? "null"
        };
    }

    private static string BuildThrownErrorMessage(object? errorValue, object? detailValue)
    {
        var error = FormatPrintedValue(errorValue);
        if (detailValue is null)
        {
            return $"Thrown: {error}";
        }

        return $"Thrown: {error} ({FormatPrintedValue(detailValue)})";
    }

    private static object?[] RequireArrayValue(object? value)
    {
        return value as object?[]
               ?? throw new InvalidOperationException("Value is not an array.");
    }

    private static bool ToBool(object? value)
    {
        return value switch
        {
            null => false,
            bool boolean => boolean,
            string text => !string.IsNullOrEmpty(text),
            _ => Math.Abs(ToDouble(value)) > double.Epsilon
        };
    }

    private static long ToLong(object? value)
    {
        return value switch
        {
            null => 0,
            long longValue => longValue,
            ulong ulongValue => unchecked((long)ulongValue),
            int intValue => intValue,
            uint uintValue => uintValue,
            short shortValue => shortValue,
            ushort ushortValue => ushortValue,
            byte byteValue => byteValue,
            sbyte sbyteValue => sbyteValue,
            char charValue => charValue,
            bool boolValue => boolValue ? 1 : 0,
            float floatValue => (long)Math.Truncate(floatValue),
            double doubleValue => (long)Math.Truncate(doubleValue),
            decimal decimalValue => (long)Math.Truncate(decimalValue),
            _ => Convert.ToInt64(value)
        };
    }

    private static double ToDouble(object? value)
    {
        return value switch
        {
            null => 0,
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            decimal decimalValue => (double)decimalValue,
            long longValue => longValue,
            ulong ulongValue => ulongValue,
            int intValue => intValue,
            uint uintValue => uintValue,
            short shortValue => shortValue,
            ushort ushortValue => ushortValue,
            byte byteValue => byteValue,
            sbyte sbyteValue => sbyteValue,
            char charValue => charValue,
            bool boolValue => boolValue ? 1 : 0,
            _ => Convert.ToDouble(value)
        };
    }

    private static bool NumericEquals(object? left, object? right)
    {
        if (!IsNumeric(left) || !IsNumeric(right))
        {
            return false;
        }

        return Math.Abs(ToDouble(left) - ToDouble(right)) < double.Epsilon;
    }

    private static bool IsFloating(object? value)
    {
        return value is double or float or decimal;
    }

    private static bool IsNumeric(object? value)
    {
        return value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or char;
    }

    private static bool IsFastPathConstant(object? value)
    {
        return value is bool or char or byte or sbyte or short or ushort or int or uint or long or ulong;
    }

    private static bool IsIntegerBinaryOperation(BytecodeBinaryOperator operation)
    {
        return operation is BytecodeBinaryOperator.Add
            or BytecodeBinaryOperator.Subtract
            or BytecodeBinaryOperator.Multiply
            or BytecodeBinaryOperator.Divide
            or BytecodeBinaryOperator.Modulo
            or BytecodeBinaryOperator.Root
            or BytecodeBinaryOperator.ShiftLeft
            or BytecodeBinaryOperator.ShiftRight
            or BytecodeBinaryOperator.UnsignedShiftLeft
            or BytecodeBinaryOperator.UnsignedShiftRight
            or BytecodeBinaryOperator.Less
            or BytecodeBinaryOperator.LessOrEqual
            or BytecodeBinaryOperator.Greater
            or BytecodeBinaryOperator.GreaterOrEqual
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

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static bool IsPositivePowerOfTwo(long value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }
}
