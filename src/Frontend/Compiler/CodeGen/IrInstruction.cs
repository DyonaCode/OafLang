namespace Oaf.Frontend.Compiler.CodeGen;

public enum IrInstructionKind
{
    Assign,
    Unary,
    Binary,
    Cast,
    Print,
    Throw,
    ArrayCreate,
    ArrayGet,
    ArraySet,
    ParallelForBegin,
    ParallelForEnd,
    ParallelReduceAdd,
    Branch,
    Jump,
    Return
}

public enum IrUnaryOperator
{
    Negate,
    Identity,
    LogicalNot,
    BitwiseNot
}

public enum IrBinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Root,
    ShiftLeft,
    ShiftRight,
    UnsignedShiftLeft,
    UnsignedShiftRight,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    Equal,
    NotEqual,
    LogicalAnd,
    LogicalOr,
    LogicalXor,
    LogicalXand,
    BitAnd,
    BitOr,
    BitXor,
    BitXand
}

public abstract class IrInstruction
{
    public abstract IrInstructionKind Kind { get; }

    public virtual bool IsTerminator => false;

    public virtual bool HasSideEffects => false;

    public virtual string? WrittenTemporaryName => null;

    public abstract IEnumerable<IrValue> ReadValues();

    public abstract string ToDisplayString();
}

public sealed class IrAssignInstruction : IrInstruction
{
    public IrAssignInstruction(IrValue destination, IrValue source)
    {
        Destination = destination;
        Source = source;
    }

    public IrValue Destination { get; set; }

    public IrValue Source { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.Assign;

    public override bool HasSideEffects => Destination is IrVariableValue;

    public override string? WrittenTemporaryName => Destination is IrTemporaryValue temporary ? temporary.Name : null;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Source;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = {Source.DisplayText}";
    }
}

public sealed class IrUnaryInstruction : IrInstruction
{
    public IrUnaryInstruction(IrTemporaryValue destination, IrUnaryOperator operation, IrValue operand)
    {
        Destination = destination;
        Operation = operation;
        Operand = operand;
    }

    public IrTemporaryValue Destination { get; }

    public IrUnaryOperator Operation { get; }

    public IrValue Operand { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.Unary;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Operand;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = {Operation} {Operand.DisplayText}";
    }
}

public sealed class IrBinaryInstruction : IrInstruction
{
    public IrBinaryInstruction(IrTemporaryValue destination, IrBinaryOperator operation, IrValue left, IrValue right)
    {
        Destination = destination;
        Operation = operation;
        Left = left;
        Right = right;
    }

    public IrTemporaryValue Destination { get; }

    public IrBinaryOperator Operation { get; }

    public IrValue Left { get; set; }

    public IrValue Right { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.Binary;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Left;
        yield return Right;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = {Left.DisplayText} {Operation} {Right.DisplayText}";
    }
}

public sealed class IrCastInstruction : IrInstruction
{
    public IrCastInstruction(IrTemporaryValue destination, IrValue source, IrType targetType)
    {
        Destination = destination;
        Source = source;
        TargetType = targetType;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue Source { get; set; }

    public IrType TargetType { get; }

    public override IrInstructionKind Kind => IrInstructionKind.Cast;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Source;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = ({TargetType.Name}){Source.DisplayText}";
    }
}

public sealed class IrPrintInstruction : IrInstruction
{
    public IrPrintInstruction(IrValue value)
    {
        Value = value;
    }

    public IrValue Value { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.Print;

    public override bool HasSideEffects => true;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Value;
    }

    public override string ToDisplayString()
    {
        return $"print {Value.DisplayText}";
    }
}

public sealed class IrThrowInstruction : IrInstruction
{
    public IrThrowInstruction(IrValue? error, IrValue? detail)
    {
        Error = error;
        Detail = detail;
    }

    public IrValue? Error { get; set; }

    public IrValue? Detail { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.Throw;

    public override bool IsTerminator => true;

    public override bool HasSideEffects => true;

    public override IEnumerable<IrValue> ReadValues()
    {
        if (Error is not null)
        {
            yield return Error;
        }

        if (Detail is not null)
        {
            yield return Detail;
        }
    }

    public override string ToDisplayString()
    {
        if (Error is null && Detail is null)
        {
            return "throw";
        }

        if (Detail is null)
        {
            return $"throw {Error!.DisplayText}";
        }

        return $"throw {Error!.DisplayText}, {Detail.DisplayText}";
    }
}

public sealed class IrArrayCreateInstruction : IrInstruction
{
    public IrArrayCreateInstruction(IrTemporaryValue destination, IrValue length)
    {
        Destination = destination;
        Length = length;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue Length { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.ArrayCreate;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Length;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = newarray {Length.DisplayText}";
    }
}

public sealed class IrArrayGetInstruction : IrInstruction
{
    public IrArrayGetInstruction(IrTemporaryValue destination, IrValue array, IrValue index)
    {
        Destination = destination;
        Array = array;
        Index = index;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue Array { get; set; }

    public IrValue Index { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.ArrayGet;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Array;
        yield return Index;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = {Array.DisplayText}[{Index.DisplayText}]";
    }
}

public sealed class IrArraySetInstruction : IrInstruction
{
    public IrArraySetInstruction(IrValue array, IrValue index, IrValue value)
    {
        Array = array;
        Index = index;
        Value = value;
    }

    public IrValue Array { get; set; }

    public IrValue Index { get; set; }

    public IrValue Value { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.ArraySet;

    public override bool HasSideEffects => true;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Array;
        yield return Index;
        yield return Value;
    }

    public override string ToDisplayString()
    {
        return $"{Array.DisplayText}[{Index.DisplayText}] = {Value.DisplayText}";
    }
}

public sealed class IrParallelForBeginInstruction : IrInstruction
{
    public IrParallelForBeginInstruction(IrValue count, IrVariableValue iterationVariable)
    {
        Count = count;
        IterationVariable = iterationVariable;
    }

    public IrValue Count { get; set; }

    public IrVariableValue IterationVariable { get; }

    public override IrInstructionKind Kind => IrInstructionKind.ParallelForBegin;

    public override bool HasSideEffects => true;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Count;
    }

    public override string ToDisplayString()
    {
        return $"parallel_for_begin count={Count.DisplayText}, iter={IterationVariable.DisplayText}";
    }
}

public sealed class IrParallelForEndInstruction : IrInstruction
{
    public override IrInstructionKind Kind => IrInstructionKind.ParallelForEnd;

    public override bool HasSideEffects => true;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield break;
    }

    public override string ToDisplayString()
    {
        return "parallel_for_end";
    }
}

public sealed class IrParallelReduceAddInstruction : IrInstruction
{
    public IrParallelReduceAddInstruction(IrVariableValue target, IrValue value)
    {
        Target = target;
        Value = value;
    }

    public IrVariableValue Target { get; }

    public IrValue Value { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.ParallelReduceAdd;

    public override bool HasSideEffects => true;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Target;
        yield return Value;
    }

    public override string ToDisplayString()
    {
        return $"parallel_reduce_add {Target.DisplayText} += {Value.DisplayText}";
    }
}

public sealed class IrBranchInstruction : IrInstruction
{
    public IrBranchInstruction(IrValue condition, string trueLabel, string falseLabel)
    {
        Condition = condition;
        TrueLabel = trueLabel;
        FalseLabel = falseLabel;
    }

    public IrValue Condition { get; set; }

    public string TrueLabel { get; }

    public string FalseLabel { get; }

    public override IrInstructionKind Kind => IrInstructionKind.Branch;

    public override bool IsTerminator => true;

    public override bool HasSideEffects => true;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Condition;
    }

    public override string ToDisplayString()
    {
        return $"branch {Condition.DisplayText} ? {TrueLabel} : {FalseLabel}";
    }
}

public sealed class IrJumpInstruction : IrInstruction
{
    public IrJumpInstruction(string targetLabel)
    {
        TargetLabel = targetLabel;
    }

    public string TargetLabel { get; }

    public override IrInstructionKind Kind => IrInstructionKind.Jump;

    public override bool IsTerminator => true;

    public override bool HasSideEffects => true;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield break;
    }

    public override string ToDisplayString()
    {
        return $"jump {TargetLabel}";
    }
}

public sealed class IrReturnInstruction : IrInstruction
{
    public IrReturnInstruction(IrValue? value)
    {
        Value = value;
    }

    public IrValue? Value { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.Return;

    public override bool IsTerminator => true;

    public override bool HasSideEffects => true;

    public override IEnumerable<IrValue> ReadValues()
    {
        if (Value is not null)
        {
            yield return Value;
        }
    }

    public override string ToDisplayString()
    {
        return Value is null ? "return" : $"return {Value.DisplayText}";
    }
}
