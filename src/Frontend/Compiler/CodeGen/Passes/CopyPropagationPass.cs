namespace Oaf.Frontend.Compiler.CodeGen.Passes;

public sealed class CopyPropagationPass : IIrOptimizationPass
{
    public string Name => "copy-propagation";

    public bool Run(IrFunction function)
    {
        var changed = false;

        foreach (var block in function.Blocks)
        {
            var temporaryCopies = new Dictionary<string, IrValue>(StringComparer.Ordinal);
            var variableCopies = new Dictionary<string, IrValue>(StringComparer.Ordinal);

            foreach (var instruction in block.Instructions)
            {
                changed |= RewriteInstructionInputs(instruction, temporaryCopies, variableCopies);

                switch (instruction)
                {
                    case IrAssignInstruction assign when assign.Destination is IrTemporaryValue destination:
                        temporaryCopies[destination.Name] = ResolveValue(assign.Source, temporaryCopies, variableCopies);
                        break;

                    case IrAssignInstruction assign when assign.Destination is IrVariableValue destination:
                        // Variable writes can invalidate prior aliases.
                        temporaryCopies.Clear();
                        variableCopies.Clear();
                        if (CanTrackVariableCopy(destination.Name, assign.Source))
                        {
                            variableCopies[destination.Name] = assign.Source;
                        }

                        break;

                    case IrParallelForBeginInstruction:
                    case IrParallelForEndInstruction:
                    case IrParallelReduceAddInstruction:
                        // Parallel regions can observe or mutate shared variables.
                        temporaryCopies.Clear();
                        variableCopies.Clear();
                        break;

                    default:
                    {
                        var written = instruction.WrittenTemporaryName;
                        if (written is not null)
                        {
                            temporaryCopies.Remove(written);
                        }

                        break;
                    }
                }
            }
        }

        return changed;
    }

    private static bool RewriteInstructionInputs(
        IrInstruction instruction,
        IReadOnlyDictionary<string, IrValue> temporaryCopies,
        IReadOnlyDictionary<string, IrValue> variableCopies)
    {
        var changed = false;

        switch (instruction)
        {
            case IrAssignInstruction assign:
            {
                if (TryResolveValue(assign.Source, temporaryCopies, variableCopies, out var assignSource))
                {
                    assign.Source = assignSource;
                    changed = true;
                }

                break;
            }

            case IrUnaryInstruction unary:
            {
                if (TryResolveValue(unary.Operand, temporaryCopies, variableCopies, out var unaryOperand))
                {
                    unary.Operand = unaryOperand;
                    changed = true;
                }

                break;
            }

            case IrBinaryInstruction binary:
            {
                if (TryResolveValue(binary.Left, temporaryCopies, variableCopies, out var binaryLeft))
                {
                    binary.Left = binaryLeft;
                    changed = true;
                }

                if (TryResolveValue(binary.Right, temporaryCopies, variableCopies, out var binaryRight))
                {
                    binary.Right = binaryRight;
                    changed = true;
                }

                break;
            }

            case IrCastInstruction cast:
            {
                if (TryResolveValue(cast.Source, temporaryCopies, variableCopies, out var castSource))
                {
                    cast.Source = castSource;
                    changed = true;
                }

                break;
            }

            case IrBranchInstruction branch:
            {
                if (TryResolveValue(branch.Condition, temporaryCopies, variableCopies, out var branchCondition))
                {
                    branch.Condition = branchCondition;
                    changed = true;
                }

                break;
            }

            case IrPrintInstruction print:
            {
                if (TryResolveValue(print.Value, temporaryCopies, variableCopies, out var printValue))
                {
                    print.Value = printValue;
                    changed = true;
                }

                break;
            }

            case IrHttpGetInstruction httpGet:
            {
                if (TryResolveValue(httpGet.Url, temporaryCopies, variableCopies, out var httpUrl))
                {
                    httpGet.Url = httpUrl;
                    changed = true;
                }

                break;
            }

            case IrHttpSendInstruction httpSend:
            {
                if (TryResolveValue(httpSend.Url, temporaryCopies, variableCopies, out var sendUrl))
                {
                    httpSend.Url = sendUrl;
                    changed = true;
                }

                if (TryResolveValue(httpSend.Method, temporaryCopies, variableCopies, out var sendMethod))
                {
                    httpSend.Method = sendMethod;
                    changed = true;
                }

                if (TryResolveValue(httpSend.Body, temporaryCopies, variableCopies, out var sendBody))
                {
                    httpSend.Body = sendBody;
                    changed = true;
                }

                if (TryResolveValue(httpSend.TimeoutMs, temporaryCopies, variableCopies, out var sendTimeout))
                {
                    httpSend.TimeoutMs = sendTimeout;
                    changed = true;
                }

                if (TryResolveValue(httpSend.Headers, temporaryCopies, variableCopies, out var sendHeaders))
                {
                    httpSend.Headers = sendHeaders;
                    changed = true;
                }

                break;
            }

            case IrHttpHeaderInstruction httpHeader:
            {
                if (TryResolveValue(httpHeader.Headers, temporaryCopies, variableCopies, out var headerText))
                {
                    httpHeader.Headers = headerText;
                    changed = true;
                }

                if (TryResolveValue(httpHeader.Name, temporaryCopies, variableCopies, out var headerName))
                {
                    httpHeader.Name = headerName;
                    changed = true;
                }

                if (TryResolveValue(httpHeader.Value, temporaryCopies, variableCopies, out var headerValue))
                {
                    httpHeader.Value = headerValue;
                    changed = true;
                }

                break;
            }

            case IrHttpQueryInstruction httpQuery:
            {
                if (TryResolveValue(httpQuery.Url, temporaryCopies, variableCopies, out var queryUrl))
                {
                    httpQuery.Url = queryUrl;
                    changed = true;
                }

                if (TryResolveValue(httpQuery.Key, temporaryCopies, variableCopies, out var queryKey))
                {
                    httpQuery.Key = queryKey;
                    changed = true;
                }

                if (TryResolveValue(httpQuery.Value, temporaryCopies, variableCopies, out var queryValue))
                {
                    httpQuery.Value = queryValue;
                    changed = true;
                }

                break;
            }

            case IrHttpUrlEncodeInstruction httpUrlEncode:
            {
                if (TryResolveValue(httpUrlEncode.Value, temporaryCopies, variableCopies, out var encodedValue))
                {
                    httpUrlEncode.Value = encodedValue;
                    changed = true;
                }

                break;
            }

            case IrHttpClientOpenInstruction httpClientOpen:
            {
                if (TryResolveValue(httpClientOpen.BaseUrl, temporaryCopies, variableCopies, out var clientBaseUrl))
                {
                    httpClientOpen.BaseUrl = clientBaseUrl;
                    changed = true;
                }

                break;
            }

            case IrHttpClientConfigureInstruction httpClientConfigure:
            {
                if (TryResolveValue(httpClientConfigure.Client, temporaryCopies, variableCopies, out var cfgClient))
                {
                    httpClientConfigure.Client = cfgClient;
                    changed = true;
                }

                if (TryResolveValue(httpClientConfigure.TimeoutMs, temporaryCopies, variableCopies, out var cfgTimeout))
                {
                    httpClientConfigure.TimeoutMs = cfgTimeout;
                    changed = true;
                }

                if (TryResolveValue(httpClientConfigure.AllowRedirects, temporaryCopies, variableCopies, out var cfgAllowRedirects))
                {
                    httpClientConfigure.AllowRedirects = cfgAllowRedirects;
                    changed = true;
                }

                if (TryResolveValue(httpClientConfigure.MaxRedirects, temporaryCopies, variableCopies, out var cfgMaxRedirects))
                {
                    httpClientConfigure.MaxRedirects = cfgMaxRedirects;
                    changed = true;
                }

                if (TryResolveValue(httpClientConfigure.UserAgent, temporaryCopies, variableCopies, out var cfgUserAgent))
                {
                    httpClientConfigure.UserAgent = cfgUserAgent;
                    changed = true;
                }

                break;
            }

            case IrHttpClientConfigureRetryInstruction httpClientConfigureRetry:
            {
                if (TryResolveValue(httpClientConfigureRetry.Client, temporaryCopies, variableCopies, out var retryClient))
                {
                    httpClientConfigureRetry.Client = retryClient;
                    changed = true;
                }

                if (TryResolveValue(httpClientConfigureRetry.MaxRetries, temporaryCopies, variableCopies, out var maxRetries))
                {
                    httpClientConfigureRetry.MaxRetries = maxRetries;
                    changed = true;
                }

                if (TryResolveValue(httpClientConfigureRetry.RetryDelayMs, temporaryCopies, variableCopies, out var retryDelay))
                {
                    httpClientConfigureRetry.RetryDelayMs = retryDelay;
                    changed = true;
                }

                break;
            }

            case IrHttpClientConfigureProxyInstruction httpClientConfigureProxy:
            {
                if (TryResolveValue(httpClientConfigureProxy.Client, temporaryCopies, variableCopies, out var proxyClient))
                {
                    httpClientConfigureProxy.Client = proxyClient;
                    changed = true;
                }

                if (TryResolveValue(httpClientConfigureProxy.ProxyUrl, temporaryCopies, variableCopies, out var proxyUrl))
                {
                    httpClientConfigureProxy.ProxyUrl = proxyUrl;
                    changed = true;
                }

                break;
            }

            case IrHttpClientDefaultHeadersInstruction httpClientDefaultHeaders:
            {
                if (TryResolveValue(httpClientDefaultHeaders.Client, temporaryCopies, variableCopies, out var defaultHeadersClient))
                {
                    httpClientDefaultHeaders.Client = defaultHeadersClient;
                    changed = true;
                }

                if (TryResolveValue(httpClientDefaultHeaders.Headers, temporaryCopies, variableCopies, out var defaultHeaders))
                {
                    httpClientDefaultHeaders.Headers = defaultHeaders;
                    changed = true;
                }

                break;
            }

            case IrHttpClientSendInstruction httpClientSend:
            {
                if (TryResolveValue(httpClientSend.Client, temporaryCopies, variableCopies, out var sendClient))
                {
                    httpClientSend.Client = sendClient;
                    changed = true;
                }

                if (TryResolveValue(httpClientSend.PathOrUrl, temporaryCopies, variableCopies, out var sendPath))
                {
                    httpClientSend.PathOrUrl = sendPath;
                    changed = true;
                }

                if (TryResolveValue(httpClientSend.Method, temporaryCopies, variableCopies, out var sendMethod))
                {
                    httpClientSend.Method = sendMethod;
                    changed = true;
                }

                if (TryResolveValue(httpClientSend.Body, temporaryCopies, variableCopies, out var sendBody))
                {
                    httpClientSend.Body = sendBody;
                    changed = true;
                }

                if (TryResolveValue(httpClientSend.Headers, temporaryCopies, variableCopies, out var sendHeaders))
                {
                    httpClientSend.Headers = sendHeaders;
                    changed = true;
                }

                break;
            }

            case IrHttpClientCloseInstruction httpClientClose:
            {
                if (TryResolveValue(httpClientClose.Client, temporaryCopies, variableCopies, out var closeClient))
                {
                    httpClientClose.Client = closeClient;
                    changed = true;
                }

                break;
            }

            case IrHttpClientRequestsSentInstruction httpClientRequestsSent:
            {
                if (TryResolveValue(httpClientRequestsSent.Client, temporaryCopies, variableCopies, out var requestsClient))
                {
                    httpClientRequestsSent.Client = requestsClient;
                    changed = true;
                }

                break;
            }

            case IrHttpClientRetriesUsedInstruction httpClientRetriesUsed:
            {
                if (TryResolveValue(httpClientRetriesUsed.Client, temporaryCopies, variableCopies, out var retriesClient))
                {
                    httpClientRetriesUsed.Client = retriesClient;
                    changed = true;
                }

                break;
            }

            case IrHttpLastHeaderInstruction httpLastHeader:
            {
                if (TryResolveValue(httpLastHeader.HeaderName, temporaryCopies, variableCopies, out var headerName))
                {
                    httpLastHeader.HeaderName = headerName;
                    changed = true;
                }

                break;
            }

            case IrThrowInstruction throwInstruction:
            {
                if (throwInstruction.Error is not null
                    && TryResolveValue(throwInstruction.Error, temporaryCopies, variableCopies, out var throwValue))
                {
                    throwInstruction.Error = throwValue;
                    changed = true;
                }

                if (throwInstruction.Detail is not null
                    && TryResolveValue(throwInstruction.Detail, temporaryCopies, variableCopies, out var throwDetail))
                {
                    throwInstruction.Detail = throwDetail;
                    changed = true;
                }

                break;
            }

            case IrArrayCreateInstruction arrayCreate:
            {
                if (TryResolveValue(arrayCreate.Length, temporaryCopies, variableCopies, out var arrayLength))
                {
                    arrayCreate.Length = arrayLength;
                    changed = true;
                }

                break;
            }

            case IrArrayGetInstruction arrayGet:
            {
                if (TryResolveValue(arrayGet.Array, temporaryCopies, variableCopies, out var arrayTarget))
                {
                    arrayGet.Array = arrayTarget;
                    changed = true;
                }

                if (TryResolveValue(arrayGet.Index, temporaryCopies, variableCopies, out var arrayIndex))
                {
                    arrayGet.Index = arrayIndex;
                    changed = true;
                }

                break;
            }

            case IrArraySetInstruction arraySet:
            {
                if (TryResolveValue(arraySet.Array, temporaryCopies, variableCopies, out var arrayValue))
                {
                    arraySet.Array = arrayValue;
                    changed = true;
                }

                if (TryResolveValue(arraySet.Index, temporaryCopies, variableCopies, out var indexValue))
                {
                    arraySet.Index = indexValue;
                    changed = true;
                }

                if (TryResolveValue(arraySet.Value, temporaryCopies, variableCopies, out var setValue))
                {
                    arraySet.Value = setValue;
                    changed = true;
                }

                break;
            }

            case IrParallelForBeginInstruction parallelBegin:
            {
                if (TryResolveValue(parallelBegin.Count, temporaryCopies, variableCopies, out var resolvedCount))
                {
                    parallelBegin.Count = resolvedCount;
                    changed = true;
                }

                break;
            }

            case IrParallelReduceAddInstruction parallelReduceAdd:
            {
                if (TryResolveValue(parallelReduceAdd.Value, temporaryCopies, variableCopies, out var resolvedValue))
                {
                    parallelReduceAdd.Value = resolvedValue;
                    changed = true;
                }

                break;
            }

            case IrReturnInstruction ret when ret.Value is not null:
            {
                if (TryResolveValue(ret.Value, temporaryCopies, variableCopies, out var returnValue))
                {
                    ret.Value = returnValue;
                    changed = true;
                }

                break;
            }
        }

        return changed;
    }

    private static bool TryResolveValue(
        IrValue value,
        IReadOnlyDictionary<string, IrValue> temporaryCopies,
        IReadOnlyDictionary<string, IrValue> variableCopies,
        out IrValue resolved)
    {
        resolved = ResolveValue(value, temporaryCopies, variableCopies);
        if (ReferenceEquals(resolved, value))
        {
            return false;
        }

        return true;
    }

    private static IrValue ResolveValue(
        IrValue value,
        IReadOnlyDictionary<string, IrValue> temporaryCopies,
        IReadOnlyDictionary<string, IrValue> variableCopies)
    {
        if (value is not (IrTemporaryValue or IrVariableValue))
        {
            return value;
        }

        var current = value;
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (TryResolveCopy(current, temporaryCopies, variableCopies, out var mapped, out var key))
        {
            if (!visited.Add(key))
            {
                break;
            }

            current = mapped;
        }

        return current;
    }

    private static bool TryResolveCopy(
        IrValue value,
        IReadOnlyDictionary<string, IrValue> temporaryCopies,
        IReadOnlyDictionary<string, IrValue> variableCopies,
        out IrValue mapped,
        out string key)
    {
        if (value is IrTemporaryValue temporary
            && temporaryCopies.TryGetValue(temporary.Name, out mapped!))
        {
            key = $"t:{temporary.Name}";
            return true;
        }

        if (value is IrVariableValue variable
            && variableCopies.TryGetValue(variable.Name, out mapped!))
        {
            key = $"v:{variable.Name}";
            return true;
        }

        mapped = null!;
        key = string.Empty;
        return false;
    }

    private static bool CanTrackVariableCopy(string destinationName, IrValue source)
    {
        return source switch
        {
            IrVariableValue variable => !string.Equals(variable.Name, destinationName, StringComparison.Ordinal),
            IrTemporaryValue => true,
            IrConstantValue => true,
            _ => false
        };
    }
}
