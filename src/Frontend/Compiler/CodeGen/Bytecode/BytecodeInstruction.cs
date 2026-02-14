namespace Oaf.Frontend.Compiler.CodeGen.Bytecode;

public enum BytecodeOpCode
{
    Nop,
    LoadConst,
    Move,
    Unary,
    Binary,
    BinaryInt,
    BinaryIntConstRight,
    JumpIfBinaryIntTrue,
    JumpIfBinaryIntConstRightTrue,
    Cast,
    Jump,
    JumpIfTrue,
    JumpIfFalse,
    Print,
    HttpGet,
    HttpSend,
    HttpHeader,
    HttpQuery,
    HttpUrlEncode,
    HttpClientOpen,
    HttpClientConfigure,
    HttpClientConfigureRetry,
    HttpClientConfigureProxy,
    HttpClientDefaultHeaders,
    HttpClientSend,
    HttpClientClose,
    HttpClientRequestsSent,
    HttpClientRetriesUsed,
    HttpLastBody,
    HttpLastStatus,
    HttpLastError,
    HttpLastReason,
    HttpLastContentType,
    HttpLastHeaders,
    HttpLastHeader,
    Throw,
    ArrayCreate,
    ArrayGet,
    ArraySet,
    ParallelForBegin,
    ParallelForEnd,
    ParallelReduceAdd,
    Return
}

public enum BytecodeUnaryOperator
{
    Identity,
    Negate,
    LogicalNot,
    BitwiseNot
}

public enum BytecodeBinaryOperator
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

public sealed class BytecodeInstruction
{
    public BytecodeInstruction(BytecodeOpCode opCode, int a = 0, int b = 0, int c = 0, int d = 0, int e = 0, int f = 0)
    {
        OpCode = opCode;
        A = a;
        B = b;
        C = c;
        D = d;
        E = e;
        F = f;
    }

    public BytecodeOpCode OpCode { get; }

    public int A { get; set; }

    public int B { get; set; }

    public int C { get; set; }

    public int D { get; set; }

    public int E { get; set; }

    public int F { get; set; }
}
