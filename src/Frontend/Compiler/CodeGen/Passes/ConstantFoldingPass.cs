namespace Oaf.Frontend.Compiler.CodeGen.Passes;

public sealed class ConstantFoldingPass : IIrOptimizationPass
{
    public string Name => "constant-folding";

    public bool Run(IrFunction function)
    {
        var changed = false;

        foreach (var block in function.Blocks)
        {
            var knownConstants = new Dictionary<string, IrConstantValue>(StringComparer.Ordinal);

            for (var i = 0; i < block.Instructions.Count; i++)
            {
                var instruction = block.Instructions[i];

                switch (instruction)
                {
                    case IrAssignInstruction assign:
                        assign.Source = ReplaceKnownConstants(assign.Source, knownConstants);
                        if (assign.Destination is IrTemporaryValue temp)
                        {
                            if (assign.Source is IrConstantValue constant)
                            {
                                knownConstants[temp.Name] = constant;
                            }
                            else
                            {
                                knownConstants.Remove(temp.Name);
                            }
                        }

                        break;

                    case IrUnaryInstruction unary:
                        unary.Operand = ReplaceKnownConstants(unary.Operand, knownConstants);
                        if (TryFoldUnary(unary.Operation, unary.Operand, out var unaryValue))
                        {
                            var replacement = new IrAssignInstruction(unary.Destination, unaryValue);
                            block.Instructions[i] = replacement;
                            knownConstants[unary.Destination.Name] = unaryValue;
                            changed = true;
                        }
                        else
                        {
                            knownConstants.Remove(unary.Destination.Name);
                        }

                        break;

                    case IrBinaryInstruction binary:
                        binary.Left = ReplaceKnownConstants(binary.Left, knownConstants);
                        binary.Right = ReplaceKnownConstants(binary.Right, knownConstants);
                        if (TryFoldBinary(binary.Operation, binary.Left, binary.Right, out var binaryValue))
                        {
                            var replacement = new IrAssignInstruction(binary.Destination, binaryValue);
                            block.Instructions[i] = replacement;
                            knownConstants[binary.Destination.Name] = binaryValue;
                            changed = true;
                        }
                        else
                        {
                            knownConstants.Remove(binary.Destination.Name);
                        }

                        break;

                    case IrCastInstruction cast:
                        cast.Source = ReplaceKnownConstants(cast.Source, knownConstants);
                        if (TryFoldCast(cast.Source, cast.TargetType, out var castValue))
                        {
                            var replacement = new IrAssignInstruction(cast.Destination, castValue);
                            block.Instructions[i] = replacement;
                            knownConstants[cast.Destination.Name] = castValue;
                            changed = true;
                        }
                        else
                        {
                            knownConstants.Remove(cast.Destination.Name);
                        }

                        break;

                    case IrPrintInstruction print:
                        print.Value = ReplaceKnownConstants(print.Value, knownConstants);
                        break;

                    case IrHttpGetInstruction httpGet:
                        httpGet.Url = ReplaceKnownConstants(httpGet.Url, knownConstants);
                        knownConstants.Remove(httpGet.Destination.Name);
                        break;

                    case IrHttpSendInstruction httpSend:
                        httpSend.Url = ReplaceKnownConstants(httpSend.Url, knownConstants);
                        httpSend.Method = ReplaceKnownConstants(httpSend.Method, knownConstants);
                        httpSend.Body = ReplaceKnownConstants(httpSend.Body, knownConstants);
                        httpSend.TimeoutMs = ReplaceKnownConstants(httpSend.TimeoutMs, knownConstants);
                        httpSend.Headers = ReplaceKnownConstants(httpSend.Headers, knownConstants);
                        knownConstants.Remove(httpSend.Destination.Name);
                        break;

                    case IrHttpHeaderInstruction httpHeader:
                        httpHeader.Headers = ReplaceKnownConstants(httpHeader.Headers, knownConstants);
                        httpHeader.Name = ReplaceKnownConstants(httpHeader.Name, knownConstants);
                        httpHeader.Value = ReplaceKnownConstants(httpHeader.Value, knownConstants);
                        knownConstants.Remove(httpHeader.Destination.Name);
                        break;

                    case IrHttpQueryInstruction httpQuery:
                        httpQuery.Url = ReplaceKnownConstants(httpQuery.Url, knownConstants);
                        httpQuery.Key = ReplaceKnownConstants(httpQuery.Key, knownConstants);
                        httpQuery.Value = ReplaceKnownConstants(httpQuery.Value, knownConstants);
                        knownConstants.Remove(httpQuery.Destination.Name);
                        break;

                    case IrHttpUrlEncodeInstruction httpUrlEncode:
                        httpUrlEncode.Value = ReplaceKnownConstants(httpUrlEncode.Value, knownConstants);
                        knownConstants.Remove(httpUrlEncode.Destination.Name);
                        break;

                    case IrHttpClientOpenInstruction httpClientOpen:
                        httpClientOpen.BaseUrl = ReplaceKnownConstants(httpClientOpen.BaseUrl, knownConstants);
                        knownConstants.Remove(httpClientOpen.Destination.Name);
                        break;

                    case IrHttpClientConfigureInstruction httpClientConfigure:
                        httpClientConfigure.Client = ReplaceKnownConstants(httpClientConfigure.Client, knownConstants);
                        httpClientConfigure.TimeoutMs = ReplaceKnownConstants(httpClientConfigure.TimeoutMs, knownConstants);
                        httpClientConfigure.AllowRedirects = ReplaceKnownConstants(httpClientConfigure.AllowRedirects, knownConstants);
                        httpClientConfigure.MaxRedirects = ReplaceKnownConstants(httpClientConfigure.MaxRedirects, knownConstants);
                        httpClientConfigure.UserAgent = ReplaceKnownConstants(httpClientConfigure.UserAgent, knownConstants);
                        knownConstants.Remove(httpClientConfigure.Destination.Name);
                        break;

                    case IrHttpClientConfigureRetryInstruction httpClientConfigureRetry:
                        httpClientConfigureRetry.Client = ReplaceKnownConstants(httpClientConfigureRetry.Client, knownConstants);
                        httpClientConfigureRetry.MaxRetries = ReplaceKnownConstants(httpClientConfigureRetry.MaxRetries, knownConstants);
                        httpClientConfigureRetry.RetryDelayMs = ReplaceKnownConstants(httpClientConfigureRetry.RetryDelayMs, knownConstants);
                        knownConstants.Remove(httpClientConfigureRetry.Destination.Name);
                        break;

                    case IrHttpClientConfigureProxyInstruction httpClientConfigureProxy:
                        httpClientConfigureProxy.Client = ReplaceKnownConstants(httpClientConfigureProxy.Client, knownConstants);
                        httpClientConfigureProxy.ProxyUrl = ReplaceKnownConstants(httpClientConfigureProxy.ProxyUrl, knownConstants);
                        knownConstants.Remove(httpClientConfigureProxy.Destination.Name);
                        break;

                    case IrHttpClientDefaultHeadersInstruction httpClientDefaultHeaders:
                        httpClientDefaultHeaders.Client = ReplaceKnownConstants(httpClientDefaultHeaders.Client, knownConstants);
                        httpClientDefaultHeaders.Headers = ReplaceKnownConstants(httpClientDefaultHeaders.Headers, knownConstants);
                        knownConstants.Remove(httpClientDefaultHeaders.Destination.Name);
                        break;

                    case IrHttpClientSendInstruction httpClientSend:
                        httpClientSend.Client = ReplaceKnownConstants(httpClientSend.Client, knownConstants);
                        httpClientSend.PathOrUrl = ReplaceKnownConstants(httpClientSend.PathOrUrl, knownConstants);
                        httpClientSend.Method = ReplaceKnownConstants(httpClientSend.Method, knownConstants);
                        httpClientSend.Body = ReplaceKnownConstants(httpClientSend.Body, knownConstants);
                        httpClientSend.Headers = ReplaceKnownConstants(httpClientSend.Headers, knownConstants);
                        knownConstants.Remove(httpClientSend.Destination.Name);
                        break;

                    case IrHttpClientCloseInstruction httpClientClose:
                        httpClientClose.Client = ReplaceKnownConstants(httpClientClose.Client, knownConstants);
                        knownConstants.Remove(httpClientClose.Destination.Name);
                        break;

                    case IrHttpClientRequestsSentInstruction httpClientRequestsSent:
                        httpClientRequestsSent.Client = ReplaceKnownConstants(httpClientRequestsSent.Client, knownConstants);
                        knownConstants.Remove(httpClientRequestsSent.Destination.Name);
                        break;

                    case IrHttpClientRetriesUsedInstruction httpClientRetriesUsed:
                        httpClientRetriesUsed.Client = ReplaceKnownConstants(httpClientRetriesUsed.Client, knownConstants);
                        knownConstants.Remove(httpClientRetriesUsed.Destination.Name);
                        break;

                    case IrHttpLastBodyInstruction httpLastBody:
                        knownConstants.Remove(httpLastBody.Destination.Name);
                        break;

                    case IrHttpLastStatusInstruction httpLastStatus:
                        knownConstants.Remove(httpLastStatus.Destination.Name);
                        break;

                    case IrHttpLastErrorInstruction httpLastError:
                        knownConstants.Remove(httpLastError.Destination.Name);
                        break;

                    case IrHttpLastReasonInstruction httpLastReason:
                        knownConstants.Remove(httpLastReason.Destination.Name);
                        break;

                    case IrHttpLastContentTypeInstruction httpLastContentType:
                        knownConstants.Remove(httpLastContentType.Destination.Name);
                        break;

                    case IrHttpLastHeadersInstruction httpLastHeaders:
                        knownConstants.Remove(httpLastHeaders.Destination.Name);
                        break;

                    case IrHttpLastHeaderInstruction httpLastHeader:
                        httpLastHeader.HeaderName = ReplaceKnownConstants(httpLastHeader.HeaderName, knownConstants);
                        knownConstants.Remove(httpLastHeader.Destination.Name);
                        break;

                    case IrThrowInstruction throwInstruction:
                        if (throwInstruction.Error is not null)
                        {
                            throwInstruction.Error = ReplaceKnownConstants(throwInstruction.Error, knownConstants);
                        }

                        if (throwInstruction.Detail is not null)
                        {
                            throwInstruction.Detail = ReplaceKnownConstants(throwInstruction.Detail, knownConstants);
                        }

                        break;

                    case IrArrayCreateInstruction arrayCreate:
                        arrayCreate.Length = ReplaceKnownConstants(arrayCreate.Length, knownConstants);
                        knownConstants.Remove(arrayCreate.Destination.Name);
                        break;

                    case IrArrayGetInstruction arrayGet:
                        arrayGet.Array = ReplaceKnownConstants(arrayGet.Array, knownConstants);
                        arrayGet.Index = ReplaceKnownConstants(arrayGet.Index, knownConstants);
                        knownConstants.Remove(arrayGet.Destination.Name);
                        break;

                    case IrArraySetInstruction arraySet:
                        arraySet.Array = ReplaceKnownConstants(arraySet.Array, knownConstants);
                        arraySet.Index = ReplaceKnownConstants(arraySet.Index, knownConstants);
                        arraySet.Value = ReplaceKnownConstants(arraySet.Value, knownConstants);
                        break;

                    case IrParallelForBeginInstruction parallelBegin:
                        parallelBegin.Count = ReplaceKnownConstants(parallelBegin.Count, knownConstants);
                        break;

                    case IrParallelReduceAddInstruction parallelReduceAdd:
                        parallelReduceAdd.Value = ReplaceKnownConstants(parallelReduceAdd.Value, knownConstants);
                        break;

                    case IrBranchInstruction branch:
                        branch.Condition = ReplaceKnownConstants(branch.Condition, knownConstants);
                        if (TryGetTruthiness(branch.Condition, out var condition))
                        {
                            block.Instructions[i] = new IrJumpInstruction(condition ? branch.TrueLabel : branch.FalseLabel);
                            changed = true;
                        }

                        break;

                    case IrReturnInstruction returnInstruction when returnInstruction.Value is not null:
                        returnInstruction.Value = ReplaceKnownConstants(returnInstruction.Value, knownConstants);
                        break;
                }
            }
        }

        return changed;
    }

    private static IrValue ReplaceKnownConstants(IrValue value, IReadOnlyDictionary<string, IrConstantValue> constants)
    {
        if (value is IrTemporaryValue temporary && constants.TryGetValue(temporary.Name, out var constant))
        {
            return constant;
        }

        return value;
    }

    private static bool TryFoldUnary(IrUnaryOperator operation, IrValue operand, out IrConstantValue value)
    {
        if (operand is not IrConstantValue constant)
        {
            value = null!;
            return false;
        }

        object? result = operation switch
        {
            IrUnaryOperator.Identity => constant.Value,
            IrUnaryOperator.Negate => constant.Value switch
            {
                long intValue => -intValue,
                double floatValue => -floatValue,
                _ => null
            },
            IrUnaryOperator.LogicalNot => constant.Value is bool boolValue ? !boolValue : null,
            IrUnaryOperator.BitwiseNot => constant.Value is long intValue ? ~intValue : null,
            _ => null
        };

        if (result is null && constant.Value is not null)
        {
            value = null!;
            return false;
        }

        value = new IrConstantValue(constant.Type, result);
        return true;
    }

    private static bool TryFoldBinary(IrBinaryOperator operation, IrValue left, IrValue right, out IrConstantValue value)
    {
        if (left is not IrConstantValue leftConstant || right is not IrConstantValue rightConstant)
        {
            value = null!;
            return false;
        }

        if (leftConstant.Value is string leftText && operation == IrBinaryOperator.Add)
        {
            value = new IrConstantValue(IrType.String, leftText + rightConstant.DisplayText.Trim('"'));
            return true;
        }

        if (TryAsDouble(leftConstant.Value, out var leftFloat) && TryAsDouble(rightConstant.Value, out var rightFloat))
        {
            var leftIsInt = TryAsLong(leftConstant.Value, out var leftInt);
            var rightIsInt = TryAsLong(rightConstant.Value, out var rightInt);

            switch (operation)
            {
                case IrBinaryOperator.Add:
                    if (leftIsInt && rightIsInt)
                    {
                        value = new IrConstantValue(IrType.Int, leftInt + rightInt);
                    }
                    else
                    {
                        value = new IrConstantValue(IrType.Float, leftFloat + rightFloat);
                    }

                    return true;

                case IrBinaryOperator.Subtract:
                    if (leftIsInt && rightIsInt)
                    {
                        value = new IrConstantValue(IrType.Int, leftInt - rightInt);
                    }
                    else
                    {
                        value = new IrConstantValue(IrType.Float, leftFloat - rightFloat);
                    }

                    return true;

                case IrBinaryOperator.Multiply:
                    if (leftIsInt && rightIsInt)
                    {
                        value = new IrConstantValue(IrType.Int, leftInt * rightInt);
                    }
                    else
                    {
                        value = new IrConstantValue(IrType.Float, leftFloat * rightFloat);
                    }

                    return true;

                case IrBinaryOperator.Divide:
                    if (rightFloat == 0)
                    {
                        value = null!;
                        return false;
                    }

                    value = new IrConstantValue(IrType.Float, leftFloat / rightFloat);
                    return true;

                case IrBinaryOperator.Modulo:
                    if (leftIsInt && rightIsInt && rightInt != 0)
                    {
                        value = new IrConstantValue(IrType.Int, leftInt % rightInt);
                        return true;
                    }

                    value = null!;
                    return false;

                case IrBinaryOperator.Root:
                    if (rightFloat == 0)
                    {
                        value = null!;
                        return false;
                    }

                    value = new IrConstantValue(IrType.Float, Math.Pow(leftFloat, 1.0 / rightFloat));
                    return true;

                case IrBinaryOperator.Less:
                    value = new IrConstantValue(IrType.Bool, leftFloat < rightFloat);
                    return true;

                case IrBinaryOperator.LessOrEqual:
                    value = new IrConstantValue(IrType.Bool, leftFloat <= rightFloat);
                    return true;

                case IrBinaryOperator.Greater:
                    value = new IrConstantValue(IrType.Bool, leftFloat > rightFloat);
                    return true;

                case IrBinaryOperator.GreaterOrEqual:
                    value = new IrConstantValue(IrType.Bool, leftFloat >= rightFloat);
                    return true;

                case IrBinaryOperator.Equal:
                    value = new IrConstantValue(IrType.Bool, Math.Abs(leftFloat - rightFloat) < double.Epsilon);
                    return true;

                case IrBinaryOperator.NotEqual:
                    value = new IrConstantValue(IrType.Bool, Math.Abs(leftFloat - rightFloat) >= double.Epsilon);
                    return true;
            }
        }

        if (leftConstant.Value is bool leftBool && rightConstant.Value is bool rightBool)
        {
            object result = operation switch
            {
                IrBinaryOperator.LogicalAnd or IrBinaryOperator.BitAnd => leftBool && rightBool,
                IrBinaryOperator.LogicalOr or IrBinaryOperator.BitOr => leftBool || rightBool,
                IrBinaryOperator.LogicalXor => leftBool ^ rightBool,
                IrBinaryOperator.LogicalXand => !(leftBool ^ rightBool),
                IrBinaryOperator.Equal => leftBool == rightBool,
                IrBinaryOperator.NotEqual => leftBool != rightBool,
                _ => new object()
            };

            if (result is bool boolResult)
            {
                value = new IrConstantValue(IrType.Bool, boolResult);
                return true;
            }
        }

        if (TryAsLong(leftConstant.Value, out var leftInteger) && TryAsLong(rightConstant.Value, out var rightInteger))
        {
            if (rightInteger == 0 && operation is IrBinaryOperator.ShiftLeft or IrBinaryOperator.ShiftRight or IrBinaryOperator.UnsignedShiftLeft or IrBinaryOperator.UnsignedShiftRight)
            {
                value = null!;
                return false;
            }

            object result = operation switch
            {
                IrBinaryOperator.ShiftLeft => leftInteger << (int)rightInteger,
                IrBinaryOperator.ShiftRight => leftInteger >> (int)rightInteger,
                IrBinaryOperator.UnsignedShiftLeft => (long)((ulong)leftInteger << (int)rightInteger),
                IrBinaryOperator.UnsignedShiftRight => (long)((ulong)leftInteger >> (int)rightInteger),
                IrBinaryOperator.BitAnd => leftInteger & rightInteger,
                IrBinaryOperator.BitOr => leftInteger | rightInteger,
                IrBinaryOperator.BitXor => leftInteger ^ rightInteger,
                IrBinaryOperator.BitXand => ~(leftInteger ^ rightInteger),
                _ => new object()
            };

            if (result is long integerResult)
            {
                value = new IrConstantValue(IrType.Int, integerResult);
                return true;
            }
        }

        if (operation is IrBinaryOperator.Equal or IrBinaryOperator.NotEqual)
        {
            var equal = Equals(leftConstant.Value, rightConstant.Value);
            value = new IrConstantValue(IrType.Bool, operation == IrBinaryOperator.Equal ? equal : !equal);
            return true;
        }

        value = null!;
        return false;
    }

    private static bool TryFoldCast(IrValue source, IrType targetType, out IrConstantValue value)
    {
        if (source is not IrConstantValue constant)
        {
            value = null!;
            return false;
        }

        if (constant.Value is null)
        {
            value = new IrConstantValue(targetType, null);
            return true;
        }

        try
        {
            object converted = targetType.Kind switch
            {
                IrTypeKind.Int => constant.Value switch
                {
                    char character => (long)character,
                    double doubleValue => (long)Math.Truncate(doubleValue),
                    float floatValue => (long)Math.Truncate(floatValue),
                    decimal decimalValue => (long)Math.Truncate(decimalValue),
                    _ => Convert.ToInt64(constant.Value)
                },
                IrTypeKind.Float => constant.Value switch
                {
                    char character => (double)character,
                    _ => Convert.ToDouble(constant.Value)
                },
                IrTypeKind.Char => Convert.ToChar(constant.Value),
                IrTypeKind.Bool => Convert.ToBoolean(constant.Value),
                IrTypeKind.String => Convert.ToString(constant.Value) ?? string.Empty,
                _ => constant.Value
            };

            value = new IrConstantValue(targetType, converted);
            return true;
        }
        catch
        {
            value = null!;
            return false;
        }
    }

    private static bool TryGetTruthiness(IrValue value, out bool truthy)
    {
        if (value is not IrConstantValue constant)
        {
            truthy = false;
            return false;
        }

        switch (constant.Value)
        {
            case bool boolean:
                truthy = boolean;
                return true;

            case long intValue:
                truthy = intValue != 0;
                return true;

            case double floatValue:
                truthy = Math.Abs(floatValue) > double.Epsilon;
                return true;
        }

        truthy = false;
        return false;
    }

    private static bool TryAsLong(object? value, out long result)
    {
        switch (value)
        {
            case long longValue:
                result = longValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case char charValue:
                result = charValue;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryAsDouble(object? value, out double result)
    {
        switch (value)
        {
            case double doubleValue:
                result = doubleValue;
                return true;
            case float floatValue:
                result = floatValue;
                return true;
            case long longValue:
                result = longValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case char charValue:
                result = charValue;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}
