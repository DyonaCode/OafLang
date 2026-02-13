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
