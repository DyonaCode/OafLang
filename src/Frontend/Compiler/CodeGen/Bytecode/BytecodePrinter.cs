using System.Text;

namespace Oaf.Frontend.Compiler.CodeGen.Bytecode;

public static class BytecodePrinter
{
    public static string Print(BytecodeProgram program)
    {
        var builder = new StringBuilder();
        builder.Append("entry ");
        builder.AppendLine(program.EntryFunctionName);

        foreach (var function in program.Functions)
        {
            builder.Append("function ");
            builder.Append(function.Name);
            builder.Append(" (slots=");
            builder.Append(function.SlotCount);
            builder.AppendLine("):");

            if (function.Constants.Count > 0)
            {
                builder.AppendLine("  constants:");
                for (var i = 0; i < function.Constants.Count; i++)
                {
                    builder.Append("    [");
                    builder.Append(i);
                    builder.Append("] = ");
                    builder.AppendLine(FormatConstant(function.Constants[i]));
                }
            }

            builder.AppendLine("  instructions:");
            for (var i = 0; i < function.Instructions.Count; i++)
            {
                var instruction = function.Instructions[i];
                builder.Append("    ");
                builder.Append(i.ToString().PadLeft(4));
                builder.Append(": ");
                builder.AppendLine(FormatInstruction(instruction));
            }
        }

        return builder.ToString();
    }

    private static string FormatInstruction(BytecodeInstruction instruction)
    {
        return instruction.OpCode switch
        {
            BytecodeOpCode.LoadConst => $"load_const s{instruction.A}, c{instruction.B}",
            BytecodeOpCode.Move => $"move s{instruction.A}, s{instruction.B}",
            BytecodeOpCode.Unary => $"unary s{instruction.A}, {(BytecodeUnaryOperator)instruction.B}, s{instruction.C}",
            BytecodeOpCode.Binary => $"binary s{instruction.A}, {(BytecodeBinaryOperator)instruction.B}, s{instruction.C}, s{instruction.D}",
            BytecodeOpCode.BinaryInt => $"binary_i64 s{instruction.A}, {(BytecodeBinaryOperator)instruction.B}, s{instruction.C}, s{instruction.D}",
            BytecodeOpCode.BinaryIntConstRight => $"binary_i64_const_r s{instruction.A}, {(BytecodeBinaryOperator)instruction.B}, s{instruction.C}, c{instruction.D}",
            BytecodeOpCode.JumpIfBinaryIntTrue => $"jump_if_binary_i64_true {(BytecodeBinaryOperator)instruction.C}, s{instruction.A}, s{instruction.B}, {instruction.D}",
            BytecodeOpCode.JumpIfBinaryIntConstRightTrue => $"jump_if_binary_i64_const_r_true {(BytecodeBinaryOperator)instruction.C}, s{instruction.A}, c{instruction.B}, {instruction.D}",
            BytecodeOpCode.Cast => $"cast s{instruction.A}, s{instruction.B}, {(IrTypeKind)instruction.C}",
            BytecodeOpCode.Jump => $"jump {instruction.A}",
            BytecodeOpCode.JumpIfTrue => $"jump_if_true s{instruction.A}, {instruction.B}",
            BytecodeOpCode.JumpIfFalse => $"jump_if_false s{instruction.A}, {instruction.B}",
            BytecodeOpCode.HttpGet => $"http_get s{instruction.A}, s{instruction.B}",
            BytecodeOpCode.HttpSend => $"http_send s{instruction.A}, url=s{instruction.B}, method=s{instruction.C}, body=s{instruction.D}, timeout=s{instruction.E}, headers=s{instruction.F}",
            BytecodeOpCode.HttpHeader => $"http_header s{instruction.A}, base=s{instruction.B}, name=s{instruction.C}, value=s{instruction.D}",
            BytecodeOpCode.HttpQuery => $"http_query s{instruction.A}, url=s{instruction.B}, key=s{instruction.C}, value=s{instruction.D}",
            BytecodeOpCode.HttpUrlEncode => $"http_url_encode s{instruction.A}, s{instruction.B}",
            BytecodeOpCode.HttpClientOpen => $"http_client_open s{instruction.A}, base=s{instruction.B}",
            BytecodeOpCode.HttpClientConfigure => $"http_client_configure s{instruction.A}, client=s{instruction.B}, timeout=s{instruction.C}, redirects=s{instruction.D}, max_redirects=s{instruction.E}, user_agent=s{instruction.F}",
            BytecodeOpCode.HttpClientConfigureRetry => $"http_client_configure_retry s{instruction.A}, client=s{instruction.B}, max_retries=s{instruction.C}, retry_delay_ms=s{instruction.D}",
            BytecodeOpCode.HttpClientConfigureProxy => $"http_client_configure_proxy s{instruction.A}, client=s{instruction.B}, proxy=s{instruction.C}",
            BytecodeOpCode.HttpClientDefaultHeaders => $"http_client_default_headers s{instruction.A}, client=s{instruction.B}, headers=s{instruction.C}",
            BytecodeOpCode.HttpClientSend => $"http_client_send s{instruction.A}, client=s{instruction.B}, target=s{instruction.C}, method=s{instruction.D}, body=s{instruction.E}, headers=s{instruction.F}",
            BytecodeOpCode.HttpClientClose => $"http_client_close s{instruction.A}, client=s{instruction.B}",
            BytecodeOpCode.HttpClientRequestsSent => $"http_client_requests_sent s{instruction.A}, client=s{instruction.B}",
            BytecodeOpCode.HttpClientRetriesUsed => $"http_client_retries_used s{instruction.A}, client=s{instruction.B}",
            BytecodeOpCode.HttpLastBody => $"http_last_body s{instruction.A}",
            BytecodeOpCode.HttpLastStatus => $"http_last_status s{instruction.A}",
            BytecodeOpCode.HttpLastError => $"http_last_error s{instruction.A}",
            BytecodeOpCode.HttpLastReason => $"http_last_reason s{instruction.A}",
            BytecodeOpCode.HttpLastContentType => $"http_last_content_type s{instruction.A}",
            BytecodeOpCode.HttpLastHeaders => $"http_last_headers s{instruction.A}",
            BytecodeOpCode.HttpLastHeader => $"http_last_header s{instruction.A}, s{instruction.B}",
            BytecodeOpCode.ParallelForBegin => $"parallel_for_begin count=s{instruction.A}, iter=s{instruction.B}, end={instruction.C}",
            BytecodeOpCode.ParallelForEnd => "parallel_for_end",
            BytecodeOpCode.ParallelReduceAdd => $"parallel_reduce_add s{instruction.A}, s{instruction.B}",
            BytecodeOpCode.Return => instruction.A < 0 ? "return" : $"return s{instruction.A}",
            _ => instruction.OpCode.ToString().ToLowerInvariant()
        };
    }

    private static string FormatConstant(object? value)
    {
        return value switch
        {
            null => "null",
            string text => $"\"{text}\"",
            char ch => $"'{ch}'",
            bool boolean => boolean ? "true" : "false",
            _ => value.ToString() ?? "null"
        };
    }
}
