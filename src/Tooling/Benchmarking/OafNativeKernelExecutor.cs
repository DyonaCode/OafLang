using System.Diagnostics;
using System.Globalization;
using System.Text;
using Oaf.Frontend.Compiler.CodeGen;
using Oaf.Frontend.Compiler.CodeGen.Bytecode;

namespace Oaf.Tooling.Benchmarking;

public static class OafNativeKernelExecutor
{
    public sealed class NativeKernelHandle : IDisposable
    {
        private bool _disposed;

        internal NativeKernelHandle(string rootDirectory, string executablePath)
        {
            RootDirectory = rootDirectory;
            ExecutablePath = executablePath;
        }

        public string RootDirectory { get; }

        public string ExecutablePath { get; }

        public (int Iterations, double TotalMs, double MeanMs, ulong Checksum) ExecuteIterations(int iterations)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NativeKernelHandle));
            }

            var psi = new ProcessStartInfo
            {
                FileName = ExecutablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(iterations.ToString(CultureInfo.InvariantCulture));

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to execute native kernel '{ExecutablePath}'.");

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Native kernel exited with code {process.ExitCode}.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
            }

            var line = stdout
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()
                ?? throw new InvalidOperationException($"Native kernel produced no output.{Environment.NewLine}{stderr}");

            var cells = line.Split(',', StringSplitOptions.TrimEntries);
            if (cells.Length != 4)
            {
                throw new InvalidOperationException($"Unexpected native kernel output '{line}'.");
            }

            if (!int.TryParse(cells[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIterations) ||
                !double.TryParse(cells[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var totalMs) ||
                !double.TryParse(cells[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var meanMs) ||
                !ulong.TryParse(cells[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var checksum))
            {
                throw new InvalidOperationException($"Unable to parse native kernel output '{line}'.");
            }

            return (parsedIterations, totalMs, meanMs, checksum);
        }

        public long ExecuteOnce()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NativeKernelHandle));
            }

            var psi = new ProcessStartInfo
            {
                FileName = ExecutablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--raw");

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to execute native kernel '{ExecutablePath}'.");

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Native raw execution exited with code {process.ExitCode}.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
            }

            var line = stdout
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()
                ?? throw new InvalidOperationException($"Native raw execution produced no output.{Environment.NewLine}{stderr}");

            if (!long.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                throw new InvalidOperationException($"Unable to parse native raw output '{line}'.");
            }

            return value;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
    }

    public static bool IsNativeCompilerAvailable()
    {
        var compiler = ResolveCompiler();
        return compiler is not null;
    }

    public static NativeKernelHandle CreateHandle(BytecodeProgram program)
    {
        var function = program.FindFunction(program.EntryFunctionName)
            ?? throw new InvalidOperationException($"Entry function '{program.EntryFunctionName}' not found.");

        var compiler = ResolveCompiler()
            ?? throw new InvalidOperationException("No C compiler found. Set CC or install a system compiler.");

        var tempRoot = Path.Combine(Path.GetTempPath(), $"oaf_native_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourcePath = Path.Combine(tempRoot, "kernel.c");
            var executableName = OperatingSystem.IsWindows() ? "kernel.exe" : "kernel";
            var executablePath = Path.Combine(tempRoot, executableName);

            File.WriteAllText(sourcePath, EmitNativeKernelSource(function));
            CompileNativeSource(compiler, sourcePath, executablePath);

            return new NativeKernelHandle(tempRoot, executablePath);
        }
        catch
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }

            throw;
        }
    }

    public static OafKernelBenchmarkResult Execute(
        string algorithm,
        BytecodeProgram program,
        int iterations)
    {
        using var handle = CreateHandle(program);
        var (parsedIterations, totalMs, meanMs, checksum) = handle.ExecuteIterations(iterations);
        return new OafKernelBenchmarkResult(
            Language: "oaf",
            Algorithm: algorithm,
            Iterations: parsedIterations,
            TotalMilliseconds: totalMs,
            MeanMilliseconds: meanMs,
            Checksum: checksum);
    }

    private static string? ResolveCompiler()
    {
        var cc = Environment.GetEnvironmentVariable("CC");
        if (!string.IsNullOrWhiteSpace(cc))
        {
            return cc;
        }

        foreach (var candidate in new[] { "cc", "clang", "gcc" })
        {
            if (CommandExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool CommandExists(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--version");

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit(3000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void CompileNativeSource(string compiler, string sourcePath, string executablePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = compiler,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-O3");
        psi.ArgumentList.Add("-std=c11");
        if (!OperatingSystem.IsWindows())
        {
            psi.ArgumentList.Add("-march=native");
            psi.ArgumentList.Add("-mtune=native");
        }
        psi.ArgumentList.Add(sourcePath);
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(executablePath);
        psi.ArgumentList.Add("-lm");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start compiler '{compiler}'.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Native compilation failed with exit code {process.ExitCode}.{Environment.NewLine}" +
                $"{stdout}{Environment.NewLine}{stderr}");
        }
    }

    private static string EmitNativeKernelSource(BytecodeFunction function)
    {
        ValidateFunctionForNativeExecution(function);

        var slotCount = Math.Max(function.SlotCount, 1);
        var builder = new StringBuilder();

        builder.AppendLine("#define _POSIX_C_SOURCE 200809L");
        builder.AppendLine();
        builder.AppendLine("#include <inttypes.h>");
        builder.AppendLine("#include <math.h>");
        builder.AppendLine("#include <stdint.h>");
        builder.AppendLine("#include <stdio.h>");
        builder.AppendLine("#include <stdlib.h>");
        builder.AppendLine("#include <string.h>");
        builder.AppendLine("#include <time.h>");
        builder.AppendLine();

        builder.AppendLine("static uint64_t mix_checksum(uint64_t current, uint64_t value, uint64_t iteration) {");
        builder.AppendLine("    uint64_t mixed = current ^ (value + 0x9e3779b97f4a7c15ull + (iteration << 6) + (iteration >> 2));");
        builder.AppendLine("    return (mixed << 13) | (mixed >> (64 - 13));");
        builder.AppendLine("}");
        builder.AppendLine();

        builder.AppendLine("static int64_t run_once(void) {");
        for (var i = 0; i < slotCount; i++)
        {
            builder.AppendLine($"    int64_t s{i} = 0;");
        }
        builder.AppendLine("    goto L0;");
        builder.AppendLine();

        for (var i = 0; i < function.Instructions.Count; i++)
        {
            var instruction = function.Instructions[i];
            builder.AppendLine($"L{i}:");
            builder.AppendLine(RenderInstruction(instruction, i, function.Instructions.Count, function.Constants));
        }

        builder.AppendLine($"L{function.Instructions.Count}:");
        builder.AppendLine("    return 0;");
        builder.AppendLine("}");
        builder.AppendLine();

        builder.AppendLine("int main(int argc, char** argv) {");
        builder.AppendLine("    if (argc > 1 && strcmp(argv[1], \"--raw\") == 0) {");
        builder.AppendLine("        printf(\"%\" PRId64 \"\\n\", run_once());");
        builder.AppendLine("        return 0;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    int iterations = 5;");
        builder.AppendLine("    if (argc > 1) {");
        builder.AppendLine("        iterations = atoi(argv[1]);");
        builder.AppendLine("    }");
        builder.AppendLine("    if (iterations <= 0) {");
        builder.AppendLine("        return 2;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    struct timespec started;");
        builder.AppendLine("    struct timespec ended;");
        builder.AppendLine("    if (clock_gettime(CLOCK_MONOTONIC, &started) != 0) {");
        builder.AppendLine("        return 3;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    uint64_t checksum = 0;");
        builder.AppendLine("    for (int i = 0; i < iterations; ++i) {");
        builder.AppendLine("        uint64_t value = (uint64_t)run_once();");
        builder.AppendLine("        checksum = mix_checksum(checksum, value, (uint64_t)i);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    if (clock_gettime(CLOCK_MONOTONIC, &ended) != 0) {");
        builder.AppendLine("        return 4;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    double total_ms = ((double)(ended.tv_sec - started.tv_sec) * 1000.0) +");
        builder.AppendLine("        ((double)(ended.tv_nsec - started.tv_nsec) / 1000000.0);");
        builder.AppendLine("    double mean_ms = total_ms / (double)iterations;");
        builder.AppendLine("    printf(\"%d,%.3f,%.6f,%\" PRIu64 \"\\n\", iterations, total_ms, mean_ms, checksum);");
        builder.AppendLine("    return 0;");
        builder.AppendLine("}");

        return builder.ToString();
    }

    private static string RenderInstruction(
        BytecodeInstruction instruction,
        int index,
        int instructionCount,
        IReadOnlyList<object?> constants)
    {
        var nextLabel = $"L{index + 1}";
        return instruction.OpCode switch
        {
            BytecodeOpCode.Nop => $"    goto {nextLabel};",
            BytecodeOpCode.LoadConst => $"    {Slot(instruction.A)} = {RenderConstant(constants[instruction.B])}; goto {nextLabel};",
            BytecodeOpCode.Move => $"    {Slot(instruction.A)} = {Slot(instruction.B)}; goto {nextLabel};",
            BytecodeOpCode.Unary => $"    {Slot(instruction.A)} = {RenderUnary((BytecodeUnaryOperator)instruction.B, Slot(instruction.C))}; goto {nextLabel};",
            BytecodeOpCode.Binary => $"    {Slot(instruction.A)} = {RenderBinary((BytecodeBinaryOperator)instruction.B, Slot(instruction.C), Slot(instruction.D))}; goto {nextLabel};",
            BytecodeOpCode.BinaryInt => $"    {Slot(instruction.A)} = {RenderBinary((BytecodeBinaryOperator)instruction.B, Slot(instruction.C), Slot(instruction.D))}; goto {nextLabel};",
            BytecodeOpCode.BinaryIntConstRight => $"    {Slot(instruction.A)} = {RenderBinary((BytecodeBinaryOperator)instruction.B, Slot(instruction.C), RenderConstant(constants[instruction.D]))}; goto {nextLabel};",
            BytecodeOpCode.JumpIfBinaryIntTrue => $"    if ({RenderBinary((BytecodeBinaryOperator)instruction.C, Slot(instruction.A), Slot(instruction.B))} != 0) goto L{NormalizeJumpTarget(instruction.D, instructionCount)}; goto {nextLabel};",
            BytecodeOpCode.JumpIfBinaryIntConstRightTrue => $"    if ({RenderBinary((BytecodeBinaryOperator)instruction.C, Slot(instruction.A), RenderConstant(constants[instruction.B]))} != 0) goto L{NormalizeJumpTarget(instruction.D, instructionCount)}; goto {nextLabel};",
            BytecodeOpCode.Cast => $"    {Slot(instruction.A)} = {RenderCast((IrTypeKind)instruction.C, Slot(instruction.B))}; goto {nextLabel};",
            BytecodeOpCode.Jump => $"    goto L{NormalizeJumpTarget(instruction.A, instructionCount)};",
            BytecodeOpCode.JumpIfTrue => $"    if ({Slot(instruction.A)} != 0) goto L{NormalizeJumpTarget(instruction.B, instructionCount)}; goto {nextLabel};",
            BytecodeOpCode.JumpIfFalse => $"    if ({Slot(instruction.A)} == 0) goto L{NormalizeJumpTarget(instruction.B, instructionCount)}; goto {nextLabel};",
            BytecodeOpCode.Return => instruction.A < 0 ? "    return 0;" : $"    return {Slot(instruction.A)};",
            _ => throw new InvalidOperationException($"Unsupported opcode '{instruction.OpCode}' for native kernel execution.")
        };
    }

    private static string RenderUnary(BytecodeUnaryOperator operation, string operand)
    {
        return operation switch
        {
            BytecodeUnaryOperator.Identity => operand,
            BytecodeUnaryOperator.Negate => $"(-({operand}))",
            BytecodeUnaryOperator.LogicalNot => $"(({operand}) == 0 ? 1 : 0)",
            BytecodeUnaryOperator.BitwiseNot => $"(~({operand}))",
            _ => throw new InvalidOperationException($"Unsupported unary operation '{operation}' for native kernels.")
        };
    }

    private static string RenderBinary(
        BytecodeBinaryOperator operation,
        string left,
        string right)
    {
        return operation switch
        {
            BytecodeBinaryOperator.Add => $"(({left}) + ({right}))",
            BytecodeBinaryOperator.Subtract => $"(({left}) - ({right}))",
            BytecodeBinaryOperator.Multiply => $"(({left}) * ({right}))",
            BytecodeBinaryOperator.Divide => $"(({left}) / ({right}))",
            BytecodeBinaryOperator.Modulo => $"(({left}) % ({right}))",
            BytecodeBinaryOperator.Root => $"((int64_t)pow((double)({left}), 1.0 / (double)({right})))",
            BytecodeBinaryOperator.ShiftLeft => $"(({left}) << (int)({right}))",
            BytecodeBinaryOperator.ShiftRight => $"(({left}) >> (int)({right}))",
            BytecodeBinaryOperator.UnsignedShiftLeft => $"((int64_t)((uint64_t)({left}) << (int)({right})))",
            BytecodeBinaryOperator.UnsignedShiftRight => $"((int64_t)((uint64_t)({left}) >> (int)({right})))",
            BytecodeBinaryOperator.Less => $"(({left}) < ({right}) ? 1 : 0)",
            BytecodeBinaryOperator.LessOrEqual => $"(({left}) <= ({right}) ? 1 : 0)",
            BytecodeBinaryOperator.Greater => $"(({left}) > ({right}) ? 1 : 0)",
            BytecodeBinaryOperator.GreaterOrEqual => $"(({left}) >= ({right}) ? 1 : 0)",
            BytecodeBinaryOperator.Equal => $"(({left}) == ({right}) ? 1 : 0)",
            BytecodeBinaryOperator.NotEqual => $"(({left}) != ({right}) ? 1 : 0)",
            BytecodeBinaryOperator.LogicalAnd => $"((({left}) != 0 && ({right}) != 0) ? 1 : 0)",
            BytecodeBinaryOperator.LogicalOr => $"((({left}) != 0 || ({right}) != 0) ? 1 : 0)",
            BytecodeBinaryOperator.LogicalXor => $"(((({left}) != 0) ^ (({right}) != 0)) ? 1 : 0)",
            BytecodeBinaryOperator.LogicalXand => $"((!(((({left}) != 0) ^ (({right}) != 0)))) ? 1 : 0)",
            BytecodeBinaryOperator.BitAnd => $"(({left}) & ({right}))",
            BytecodeBinaryOperator.BitOr => $"(({left}) | ({right}))",
            BytecodeBinaryOperator.BitXor => $"(({left}) ^ ({right}))",
            BytecodeBinaryOperator.BitXand => $"(~(({left}) ^ ({right})))",
            _ => throw new InvalidOperationException($"Unsupported binary operation '{operation}' for native kernels.")
        };
    }

    private static string RenderCast(IrTypeKind targetType, string value)
    {
        return targetType switch
        {
            IrTypeKind.Int => $"(int64_t)({value})",
            IrTypeKind.Char => $"(int64_t)({value})",
            IrTypeKind.Bool => $"(({value}) != 0 ? 1 : 0)",
            _ => throw new InvalidOperationException($"Unsupported cast target '{targetType}' for native kernels.")
        };
    }

    private static int NormalizeJumpTarget(int target, int instructionCount)
    {
        if (target < 0 || target > instructionCount)
        {
            return instructionCount;
        }

        return target;
    }

    private static string Slot(int index)
    {
        return $"s{index}";
    }

    private static string RenderConstant(object? value)
    {
        return value switch
        {
            null => "0",
            bool boolean => boolean ? "1" : "0",
            byte b => b.ToString(CultureInfo.InvariantCulture),
            sbyte b => b.ToString(CultureInfo.InvariantCulture),
            short s => s.ToString(CultureInfo.InvariantCulture),
            ushort s => s.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            uint i => i.ToString(CultureInfo.InvariantCulture) + "u",
            long l => l.ToString(CultureInfo.InvariantCulture) + "ll",
            ulong l => l.ToString(CultureInfo.InvariantCulture) + "ull",
            char c => ((int)c).ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Unsupported constant '{value}' for native kernels.")
        };
    }

    private static void ValidateFunctionForNativeExecution(BytecodeFunction function)
    {
        foreach (var constant in function.Constants)
        {
            _ = RenderConstant(constant);
        }

        foreach (var instruction in function.Instructions)
        {
            switch (instruction.OpCode)
            {
                case BytecodeOpCode.Nop:
                case BytecodeOpCode.LoadConst:
                case BytecodeOpCode.Move:
                case BytecodeOpCode.Unary:
                case BytecodeOpCode.Binary:
                case BytecodeOpCode.BinaryInt:
                case BytecodeOpCode.BinaryIntConstRight:
                case BytecodeOpCode.JumpIfBinaryIntTrue:
                case BytecodeOpCode.JumpIfBinaryIntConstRightTrue:
                case BytecodeOpCode.Cast:
                case BytecodeOpCode.Jump:
                case BytecodeOpCode.JumpIfTrue:
                case BytecodeOpCode.JumpIfFalse:
                case BytecodeOpCode.Return:
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported opcode '{instruction.OpCode}' for native kernels.");
            }
        }
    }
}
