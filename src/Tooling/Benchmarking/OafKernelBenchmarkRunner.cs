using System.Diagnostics;
using Oaf.Frontend.Compiler.CodeGen.Bytecode;
using Oaf.Frontend.Compiler.Driver;

namespace Oaf.Tooling.Benchmarking;

public readonly record struct OafKernelBenchmarkResult(
    string Language,
    string Algorithm,
    int Iterations,
    double TotalMilliseconds,
    double MeanMilliseconds,
    ulong Checksum);

public enum OafKernelExecutionMode
{
    BytecodeVm,
    NativeBinary,
    BytecodeVmTieredNative
}

public static class OafKernelBenchmarkRunner
{
    private const int TieredNativeThreshold = 3;

    public static IReadOnlyList<OafKernelBenchmarkResult> Run(
        int iterations,
        long sumN,
        int primeN,
        int matrixN,
        OafKernelExecutionMode executionMode = OafKernelExecutionMode.BytecodeVm)
    {
        if (iterations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), "Iterations must be positive.");
        }

        if (sumN <= 0 || primeN <= 1 || matrixN <= 0)
        {
            throw new ArgumentOutOfRangeException("Workload parameters must be positive.");
        }

        var driver = new CompilerDriver(enableCompilationCache: false);
        var results = new List<OafKernelBenchmarkResult>(capacity: 3);

        results.Add(RunAlgorithm(driver, "sum_xor", BuildSumXorSource(sumN), iterations, executionMode));
        results.Add(RunAlgorithm(driver, "prime_trial", BuildPrimeTrialSource(primeN), iterations, executionMode));
        results.Add(RunAlgorithm(driver, "affine_grid", BuildAffineGridSource(matrixN), iterations, executionMode));

        return results;
    }

    public static void PrintCsv(TextWriter output, IReadOnlyList<OafKernelBenchmarkResult> results)
    {
        output.WriteLine("language,algorithm,iterations,total_ms,mean_ms,checksum");
        foreach (var result in results)
        {
            output.WriteLine(
                $"{result.Language},{result.Algorithm},{result.Iterations}," +
                $"{result.TotalMilliseconds:F3},{result.MeanMilliseconds:F6},{result.Checksum}");
        }
    }

    private static OafKernelBenchmarkResult RunAlgorithm(
        CompilerDriver driver,
        string algorithm,
        string source,
        int iterations,
        OafKernelExecutionMode executionMode)
    {
        var compilation = driver.CompileSource(source);
        if (!compilation.Success)
        {
            var diagnostics = string.Join(Environment.NewLine, compilation.Diagnostics.Select(static d => d.ToString()));
            throw new InvalidOperationException($"Failed to compile Oaf benchmark '{algorithm}':{Environment.NewLine}{diagnostics}");
        }

        return executionMode switch
        {
            OafKernelExecutionMode.NativeBinary => OafNativeKernelExecutor.Execute(algorithm, compilation.BytecodeProgram, iterations),
            OafKernelExecutionMode.BytecodeVmTieredNative => RunTieredVmNative(algorithm, compilation.BytecodeProgram, iterations),
            _ => RunBytecodeVm(algorithm, compilation.BytecodeProgram, iterations)
        };
    }

    private static OafKernelBenchmarkResult RunBytecodeVm(string algorithm, BytecodeProgram program, int iterations)
    {
        var vm = new BytecodeVirtualMachine();
        var stopwatch = Stopwatch.StartNew();
        ulong checksum = 0;

        for (var i = 0; i < iterations; i++)
        {
            var execution = vm.Execute(program);
            if (!execution.Success)
            {
                throw new InvalidOperationException(
                    $"Oaf benchmark '{algorithm}' execution failed on iteration {i}: {execution.ErrorMessage}");
            }

            var value = ConvertToUInt64(execution.ReturnValue);
            checksum = MixChecksum(checksum, value, (ulong)i);
        }

        stopwatch.Stop();
        var totalMs = stopwatch.Elapsed.TotalMilliseconds;
        var meanMs = totalMs / iterations;
        return new OafKernelBenchmarkResult("oaf", algorithm, iterations, totalMs, meanMs, checksum);
    }

    private static OafKernelBenchmarkResult RunTieredVmNative(string algorithm, BytecodeProgram program, int iterations)
    {
        var vm = new BytecodeVirtualMachine();
        OafNativeKernelExecutor.NativeKernelHandle? nativeHandle = null;
        var canUseNative = OafNativeKernelExecutor.IsNativeCompilerAvailable();

        var stopwatch = Stopwatch.StartNew();
        ulong checksum = 0;

        try
        {
            for (var i = 0; i < iterations; i++)
            {
                object? value;
                if (canUseNative && i >= TieredNativeThreshold)
                {
                    nativeHandle ??= OafNativeKernelExecutor.CreateHandle(program);
                    value = nativeHandle.ExecuteOnce();
                }
                else
                {
                    var execution = vm.Execute(program);
                    if (!execution.Success)
                    {
                        throw new InvalidOperationException(
                            $"Oaf benchmark '{algorithm}' execution failed on iteration {i}: {execution.ErrorMessage}");
                    }

                    value = execution.ReturnValue;
                }

                checksum = MixChecksum(checksum, ConvertToUInt64(value), (ulong)i);
            }
        }
        finally
        {
            nativeHandle?.Dispose();
        }

        stopwatch.Stop();
        var totalMs = stopwatch.Elapsed.TotalMilliseconds;
        var meanMs = totalMs / iterations;
        return new OafKernelBenchmarkResult("oaf", algorithm, iterations, totalMs, meanMs, checksum);
    }

    private static ulong ConvertToUInt64(object? value)
    {
        return value switch
        {
            null => 0,
            ulong ulongValue => ulongValue,
            long longValue => unchecked((ulong)longValue),
            int intValue => unchecked((ulong)intValue),
            short shortValue => unchecked((ulong)shortValue),
            byte byteValue => byteValue,
            bool boolValue => boolValue ? 1UL : 0UL,
            _ => unchecked((ulong)Convert.ToInt64(value))
        };
    }

    private static ulong MixChecksum(ulong current, ulong value, ulong iteration)
    {
        var mixed = current ^ (value + 0x9e3779b97f4a7c15UL + (iteration << 6) + (iteration >> 2));
        return (mixed << 13) | (mixed >> (64 - 13));
    }

    private static string BuildSumXorSource(long sumN)
    {
        return $$"""
flux n = {{sumN}};
flux i = 1;
flux acc = 0;
loop i <= n =>
    acc += (i ^ (i >> 3)) + (i % 8);
    i += 1;
;;
return acc;
""";
    }

    private static string BuildPrimeTrialSource(int primeN)
    {
        return $$"""
flux n = {{primeN}};
flux candidate = 2;
flux primeCount = 0;
flux checksum = 0;
loop candidate <= n =>
    flux divisor = 2;
    flux isPrime = true;
    loop divisor * divisor <= candidate =>
        if candidate % divisor == 0 =>
            isPrime = false;
            break;
        ;;
        divisor += 1;
    ;;
    if isPrime =>
        primeCount += 1;
        checksum += candidate * ((primeCount % 16) + 1);
    ;;
    candidate += 1;
;;
return (primeCount << 32) ^ checksum;
""";
    }

    private static string BuildAffineGridSource(int matrixN)
    {
        return $$"""
flux n = {{matrixN}};
flux row = 0;
flux checksum = 0;
loop row < n =>
    flux col = 0;
    loop col < n =>
        flux acc = 0;
        flux k = 0;
        loop k < n =>
            flux a = (row * 131 + k * 17 + 13) % 256;
            flux b = (k * 19 + col * 97 + 53) % 256;
            acc += a * b;
            k += 1;
        ;;
        checksum = checksum ^ (acc + ((row * n + col) * 2654435761));
        col += 1;
    ;;
    row += 1;
;;
return checksum;
""";
    }
}
