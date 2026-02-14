namespace Oaf.Frontend.Compiler.CodeGen;

public enum IrInstructionKind
{
    Assign,
    Unary,
    Binary,
    Cast,
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

public sealed class IrHttpGetInstruction : IrInstruction
{
    public IrHttpGetInstruction(IrTemporaryValue destination, IrValue url)
    {
        Destination = destination;
        Url = url;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue Url { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpGet;

    public override bool HasSideEffects => true;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Url;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_get {Url.DisplayText}";
    }
}

public sealed class IrHttpSendInstruction : IrInstruction
{
    public IrHttpSendInstruction(
        IrTemporaryValue destination,
        IrValue url,
        IrValue method,
        IrValue body,
        IrValue timeoutMs,
        IrValue headers)
    {
        Destination = destination;
        Url = url;
        Method = method;
        Body = body;
        TimeoutMs = timeoutMs;
        Headers = headers;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue Url { get; set; }

    public IrValue Method { get; set; }

    public IrValue Body { get; set; }

    public IrValue TimeoutMs { get; set; }

    public IrValue Headers { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpSend;

    public override bool HasSideEffects => true;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Url;
        yield return Method;
        yield return Body;
        yield return TimeoutMs;
        yield return Headers;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_send {Method.DisplayText} {Url.DisplayText} timeout={TimeoutMs.DisplayText} headers={Headers.DisplayText}";
    }
}

public sealed class IrHttpHeaderInstruction : IrInstruction
{
    public IrHttpHeaderInstruction(IrTemporaryValue destination, IrValue headers, IrValue name, IrValue value)
    {
        Destination = destination;
        Headers = headers;
        Name = name;
        Value = value;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue Headers { get; set; }

    public IrValue Name { get; set; }

    public IrValue Value { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpHeader;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Headers;
        yield return Name;
        yield return Value;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_header {Headers.DisplayText}, {Name.DisplayText}, {Value.DisplayText}";
    }
}

public sealed class IrHttpQueryInstruction : IrInstruction
{
    public IrHttpQueryInstruction(IrTemporaryValue destination, IrValue url, IrValue key, IrValue value)
    {
        Destination = destination;
        Url = url;
        Key = key;
        Value = value;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue Url { get; set; }

    public IrValue Key { get; set; }

    public IrValue Value { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpQuery;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Url;
        yield return Key;
        yield return Value;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_query {Url.DisplayText}, {Key.DisplayText}, {Value.DisplayText}";
    }
}

public sealed class IrHttpUrlEncodeInstruction : IrInstruction
{
    public IrHttpUrlEncodeInstruction(IrTemporaryValue destination, IrValue value)
    {
        Destination = destination;
        Value = value;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue Value { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpUrlEncode;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Value;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_url_encode {Value.DisplayText}";
    }
}

public sealed class IrHttpClientOpenInstruction : IrInstruction
{
    public IrHttpClientOpenInstruction(IrTemporaryValue destination, IrValue baseUrl)
    {
        Destination = destination;
        BaseUrl = baseUrl;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue BaseUrl { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpClientOpen;

    public override bool HasSideEffects => true;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return BaseUrl;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_client_open {BaseUrl.DisplayText}";
    }
}

public sealed class IrHttpClientConfigureInstruction : IrInstruction
{
    public IrHttpClientConfigureInstruction(
        IrTemporaryValue destination,
        IrValue client,
        IrValue timeoutMs,
        IrValue allowRedirects,
        IrValue maxRedirects,
        IrValue userAgent)
    {
        Destination = destination;
        Client = client;
        TimeoutMs = timeoutMs;
        AllowRedirects = allowRedirects;
        MaxRedirects = maxRedirects;
        UserAgent = userAgent;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue Client { get; set; }

    public IrValue TimeoutMs { get; set; }

    public IrValue AllowRedirects { get; set; }

    public IrValue MaxRedirects { get; set; }

    public IrValue UserAgent { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpClientConfigure;

    public override bool HasSideEffects => true;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Client;
        yield return TimeoutMs;
        yield return AllowRedirects;
        yield return MaxRedirects;
        yield return UserAgent;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_client_configure client={Client.DisplayText} timeout={TimeoutMs.DisplayText} redirects={AllowRedirects.DisplayText}/{MaxRedirects.DisplayText} ua={UserAgent.DisplayText}";
    }
}

public sealed class IrHttpClientConfigureRetryInstruction : IrInstruction
{
    public IrHttpClientConfigureRetryInstruction(
        IrTemporaryValue destination,
        IrValue client,
        IrValue maxRetries,
        IrValue retryDelayMs)
    {
        Destination = destination;
        Client = client;
        MaxRetries = maxRetries;
        RetryDelayMs = retryDelayMs;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue Client { get; set; }

    public IrValue MaxRetries { get; set; }

    public IrValue RetryDelayMs { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpClientConfigureRetry;

    public override bool HasSideEffects => true;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Client;
        yield return MaxRetries;
        yield return RetryDelayMs;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_client_configure_retry client={Client.DisplayText} retries={MaxRetries.DisplayText} delay_ms={RetryDelayMs.DisplayText}";
    }
}

public sealed class IrHttpClientConfigureProxyInstruction : IrInstruction
{
    public IrHttpClientConfigureProxyInstruction(
        IrTemporaryValue destination,
        IrValue client,
        IrValue proxyUrl)
    {
        Destination = destination;
        Client = client;
        ProxyUrl = proxyUrl;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue Client { get; set; }

    public IrValue ProxyUrl { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpClientConfigureProxy;

    public override bool HasSideEffects => true;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Client;
        yield return ProxyUrl;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_client_configure_proxy client={Client.DisplayText} proxy={ProxyUrl.DisplayText}";
    }
}

public sealed class IrHttpClientDefaultHeadersInstruction : IrInstruction
{
    public IrHttpClientDefaultHeadersInstruction(IrTemporaryValue destination, IrValue client, IrValue headers)
    {
        Destination = destination;
        Client = client;
        Headers = headers;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue Client { get; set; }

    public IrValue Headers { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpClientDefaultHeaders;

    public override bool HasSideEffects => true;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Client;
        yield return Headers;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_client_default_headers client={Client.DisplayText}, headers={Headers.DisplayText}";
    }
}

public sealed class IrHttpClientSendInstruction : IrInstruction
{
    public IrHttpClientSendInstruction(
        IrTemporaryValue destination,
        IrValue client,
        IrValue pathOrUrl,
        IrValue method,
        IrValue body,
        IrValue headers)
    {
        Destination = destination;
        Client = client;
        PathOrUrl = pathOrUrl;
        Method = method;
        Body = body;
        Headers = headers;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue Client { get; set; }

    public IrValue PathOrUrl { get; set; }

    public IrValue Method { get; set; }

    public IrValue Body { get; set; }

    public IrValue Headers { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpClientSend;

    public override bool HasSideEffects => true;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Client;
        yield return PathOrUrl;
        yield return Method;
        yield return Body;
        yield return Headers;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_client_send client={Client.DisplayText} method={Method.DisplayText} target={PathOrUrl.DisplayText}";
    }
}

public sealed class IrHttpClientCloseInstruction : IrInstruction
{
    public IrHttpClientCloseInstruction(IrTemporaryValue destination, IrValue client)
    {
        Destination = destination;
        Client = client;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue Client { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpClientClose;

    public override bool HasSideEffects => true;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Client;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_client_close {Client.DisplayText}";
    }
}

public sealed class IrHttpClientRequestsSentInstruction : IrInstruction
{
    public IrHttpClientRequestsSentInstruction(IrTemporaryValue destination, IrValue client)
    {
        Destination = destination;
        Client = client;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue Client { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpClientRequestsSent;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Client;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_client_requests_sent {Client.DisplayText}";
    }
}

public sealed class IrHttpClientRetriesUsedInstruction : IrInstruction
{
    public IrHttpClientRetriesUsedInstruction(IrTemporaryValue destination, IrValue client)
    {
        Destination = destination;
        Client = client;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue Client { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpClientRetriesUsed;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return Client;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_client_retries_used {Client.DisplayText}";
    }
}

public sealed class IrHttpLastBodyInstruction : IrInstruction
{
    public IrHttpLastBodyInstruction(IrTemporaryValue destination)
    {
        Destination = destination;
    }

    public IrTemporaryValue Destination { get; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpLastBody;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield break;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_last_body";
    }
}

public sealed class IrHttpLastStatusInstruction : IrInstruction
{
    public IrHttpLastStatusInstruction(IrTemporaryValue destination)
    {
        Destination = destination;
    }

    public IrTemporaryValue Destination { get; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpLastStatus;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield break;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_last_status";
    }
}

public sealed class IrHttpLastErrorInstruction : IrInstruction
{
    public IrHttpLastErrorInstruction(IrTemporaryValue destination)
    {
        Destination = destination;
    }

    public IrTemporaryValue Destination { get; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpLastError;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield break;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_last_error";
    }
}

public sealed class IrHttpLastReasonInstruction : IrInstruction
{
    public IrHttpLastReasonInstruction(IrTemporaryValue destination)
    {
        Destination = destination;
    }

    public IrTemporaryValue Destination { get; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpLastReason;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield break;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_last_reason";
    }
}

public sealed class IrHttpLastContentTypeInstruction : IrInstruction
{
    public IrHttpLastContentTypeInstruction(IrTemporaryValue destination)
    {
        Destination = destination;
    }

    public IrTemporaryValue Destination { get; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpLastContentType;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield break;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_last_content_type";
    }
}

public sealed class IrHttpLastHeadersInstruction : IrInstruction
{
    public IrHttpLastHeadersInstruction(IrTemporaryValue destination)
    {
        Destination = destination;
    }

    public IrTemporaryValue Destination { get; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpLastHeaders;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield break;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_last_headers";
    }
}

public sealed class IrHttpLastHeaderInstruction : IrInstruction
{
    public IrHttpLastHeaderInstruction(IrTemporaryValue destination, IrValue headerName)
    {
        Destination = destination;
        HeaderName = headerName;
    }

    public IrTemporaryValue Destination { get; }

    public IrValue HeaderName { get; set; }

    public override IrInstructionKind Kind => IrInstructionKind.HttpLastHeader;

    public override string? WrittenTemporaryName => Destination.Name;

    public override IEnumerable<IrValue> ReadValues()
    {
        yield return HeaderName;
    }

    public override string ToDisplayString()
    {
        return $"{Destination.DisplayText} = http_last_header {HeaderName.DisplayText}";
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
