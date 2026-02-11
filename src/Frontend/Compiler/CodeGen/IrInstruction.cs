namespace Oaf.Frontend.Compiler.CodeGen;

public enum IrInstructionKind
{
    Assign,
    Unary,
    Binary,
    Cast,
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
