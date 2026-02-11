using System.Diagnostics;
using Oaf.Frontend.Compiler.CodeGen.Bytecode;
using Oaf.Frontend.Compiler.Driver;
using Oaf.Frontend.Compiler.Lexer;

namespace Oaf.Tooling.Benchmarking;

public readonly record struct OafBenchmarkResult(
    string Name,
    string Runtime,
    int Iterations,
    double TotalMilliseconds,
    double MeanMilliseconds,
    double OpsPerSecond);

public readonly record struct OafBenchmarkRegression(
    string Name,
    double MeanRatio,
    double MaxAllowedMeanRatio);

public static class OafBenchmarkRunner
{
    public const double DefaultMaxAllowedMeanRatio = 5.0;

    private const string LexerBenchmarkSource = """
        struct Pair<T> [T left, T right];
        enum Option<T> => Some(T), None;
        class Person [string name, int age];
        flux value = 0x10 + 0b1010;
        loop value > 0 => value -= 1;;;
        """;

    private const string CompilerBenchmarkSource = """
        struct Vec2 [float x, float y];
        flux x = 1;
        flux y = 2;
        float scale = 3.5;
        flux i = 100;
        loop i > 0 =>
            x += 1;
            y += (int)scale;
            i -= 1;
        ;;
        return x + y;
        """;

    private const string BytecodeBenchmarkSource = """
        flux total = 0;
        flux i = 1000;
        loop i > 0 =>
            total += i;
            i -= 1;
        ;;
        return total;
        """;

    public static IReadOnlyList<OafBenchmarkResult> RunAll(int iterations)
    {
        if (iterations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), "Iterations must be positive.");
        }

        var results = new List<OafBenchmarkResult>
        {
            RunLexerBenchmark(iterations),
            RunCSharpLexerBaseline(iterations),
            RunCompilerBenchmark(iterations),
            RunCSharpCompilerBaseline(iterations),
            RunBytecodeBenchmark(iterations),
            RunCSharpBytecodeBaseline(iterations)
        };

        return results;
    }

    public static void PrintReport(TextWriter output, IReadOnlyList<OafBenchmarkResult> results)
    {
        output.WriteLine("Oaf Benchmark Report");
        output.WriteLine("------------------------");

        foreach (var result in results)
        {
            output.WriteLine(
                $"{result.Name,-20} runtime={result.Runtime,-8} iterations={result.Iterations,5} total_ms={result.TotalMilliseconds,10:F3} " +
                $"mean_ms={result.MeanMilliseconds,8:F4} ops/s={result.OpsPerSecond,10:F2}");
        }

        output.WriteLine();
        output.WriteLine("Comparison (Oaf vs C# baseline)");
        output.WriteLine("-----------------------------------");

        foreach (var group in results.GroupBy(static result => result.Name, StringComparer.Ordinal))
        {
            var oaf = group.FirstOrDefault(static result => string.Equals(result.Runtime, "oaf", StringComparison.Ordinal));
            var csharp = group.FirstOrDefault(static result => string.Equals(result.Runtime, "csharp", StringComparison.Ordinal));

            if (string.IsNullOrEmpty(oaf.Name) || string.IsNullOrEmpty(csharp.Name) || csharp.MeanMilliseconds <= 0)
            {
                continue;
            }

            var ratio = oaf.MeanMilliseconds / csharp.MeanMilliseconds;
            output.WriteLine($"{group.Key,-20} mean_ratio(oaf/csharp)={ratio:F3}x");
        }
    }

    public static IReadOnlyList<OafBenchmarkRegression> AnalyzeAgainstBaselines(
        IReadOnlyList<OafBenchmarkResult> results,
        double maxAllowedMeanRatio)
    {
        if (maxAllowedMeanRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAllowedMeanRatio), "Maximum allowed ratio must be positive.");
        }

        var regressions = new List<OafBenchmarkRegression>();
        foreach (var group in results.GroupBy(static result => result.Name, StringComparer.Ordinal))
        {
            var oaf = group.FirstOrDefault(static result => string.Equals(result.Runtime, "oaf", StringComparison.Ordinal));
            var csharp = group.FirstOrDefault(static result => string.Equals(result.Runtime, "csharp", StringComparison.Ordinal));
            if (string.IsNullOrEmpty(oaf.Name) || string.IsNullOrEmpty(csharp.Name) || csharp.MeanMilliseconds <= 0)
            {
                continue;
            }

            var ratio = oaf.MeanMilliseconds / csharp.MeanMilliseconds;
            if (ratio > maxAllowedMeanRatio)
            {
                regressions.Add(new OafBenchmarkRegression(group.Key, ratio, maxAllowedMeanRatio));
            }
        }

        return regressions;
    }

    public static void PrintRegressionReport(TextWriter output, IReadOnlyList<OafBenchmarkRegression> regressions)
    {
        output.WriteLine();
        output.WriteLine("Regression Gate");
        output.WriteLine("---------------");
        if (regressions.Count == 0)
        {
            output.WriteLine("No baseline regressions detected.");
            return;
        }

        foreach (var regression in regressions.OrderBy(static item => item.Name, StringComparer.Ordinal))
        {
            output.WriteLine(
                $"{regression.Name,-20} mean_ratio(oaf/csharp)={regression.MeanRatio:F3}x " +
                $"threshold={regression.MaxAllowedMeanRatio:F3}x");
        }
    }

    private static OafBenchmarkResult RunLexerBenchmark(int iterations)
    {
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++)
        {
            var lexer = new Lexer(LexerBenchmarkSource);
            var tokens = lexer.Lex();
            if (tokens.Count == 0)
            {
                throw new InvalidOperationException("Lexer benchmark produced no tokens.");
            }
        }

        stopwatch.Stop();
        return BuildResult("lexer", "oaf", iterations, stopwatch.Elapsed);
    }

    private static OafBenchmarkResult RunCompilerBenchmark(int iterations)
    {
        var driver = new CompilerDriver(enableCompilationCache: false);
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++)
        {
            var result = driver.CompileSource(CompilerBenchmarkSource);
            if (!result.Success)
            {
                throw new InvalidOperationException("Compiler benchmark source failed compilation.");
            }
        }

        stopwatch.Stop();
        return BuildResult("compiler_pipeline", "oaf", iterations, stopwatch.Elapsed);
    }

    private static OafBenchmarkResult RunBytecodeBenchmark(int iterations)
    {
        var driver = new CompilerDriver(enableCompilationCache: false);
        var compilation = driver.CompileSource(BytecodeBenchmarkSource);
        if (!compilation.Success)
        {
            throw new InvalidOperationException("Bytecode benchmark source failed compilation.");
        }

        var program = compilation.BytecodeProgram;
        var vm = new BytecodeVirtualMachine();
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++)
        {
            var execution = vm.Execute(program);
            if (!execution.Success)
            {
                throw new InvalidOperationException($"Bytecode benchmark execution failed: {execution.ErrorMessage}");
            }
        }

        stopwatch.Stop();
        return BuildResult("bytecode_vm", "oaf", iterations, stopwatch.Elapsed);
    }

    private static OafBenchmarkResult RunCSharpLexerBaseline(int iterations)
    {
        var stopwatch = Stopwatch.StartNew();
        var checksum = 0;

        for (var i = 0; i < iterations; i++)
        {
            checksum ^= ScanTokensWithCSharpBaseline(LexerBenchmarkSource);
        }

        stopwatch.Stop();
        if (checksum == int.MinValue)
        {
            throw new InvalidOperationException("Unreachable checksum sentinel.");
        }

        return BuildResult("lexer", "csharp", iterations, stopwatch.Elapsed);
    }

    private static OafBenchmarkResult RunCSharpCompilerBaseline(int iterations)
    {
        var stopwatch = Stopwatch.StartNew();
        var checksum = 0L;

        for (var i = 0; i < iterations; i++)
        {
            checksum += RunCompilerLikeCSharpWorkload(CompilerBenchmarkSource);
        }

        stopwatch.Stop();
        if (checksum == long.MinValue)
        {
            throw new InvalidOperationException("Unreachable checksum sentinel.");
        }

        return BuildResult("compiler_pipeline", "csharp", iterations, stopwatch.Elapsed);
    }

    private static OafBenchmarkResult RunCSharpBytecodeBaseline(int iterations)
    {
        var stopwatch = Stopwatch.StartNew();
        long total = 0;

        for (var i = 0; i < iterations; i++)
        {
            total += ExecuteBytecodeEquivalentInCSharp();
        }

        stopwatch.Stop();
        if (total == long.MinValue)
        {
            throw new InvalidOperationException("Unreachable checksum sentinel.");
        }

        return BuildResult("bytecode_vm", "csharp", iterations, stopwatch.Elapsed);
    }

    private static int ScanTokensWithCSharpBaseline(string source)
    {
        var tokenCount = 0;
        var inToken = false;

        foreach (var ch in source)
        {
            var separator = char.IsWhiteSpace(ch) || ch is '(' or ')' or '[' or ']' or '{' or '}' or ';' or ',';
            if (separator)
            {
                if (inToken)
                {
                    tokenCount++;
                    inToken = false;
                }

                continue;
            }

            inToken = true;
        }

        if (inToken)
        {
            tokenCount++;
        }

        return tokenCount;
    }

    private static long RunCompilerLikeCSharpWorkload(string source)
    {
        var lines = source.Split('\n');
        long checksum = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            checksum += trimmed.Length;
            checksum ^= ScanTokensWithCSharpBaseline(trimmed);
            checksum += trimmed.Count(static ch => ch is '<' or '>' or '(' or ')' or '[' or ']' or '{' or '}');
        }

        return checksum;
    }

    private static long ExecuteBytecodeEquivalentInCSharp()
    {
        long total = 0;
        long i = 1000;

        while (i > 0)
        {
            total += i;
            i -= 1;
        }

        return total;
    }

    private static OafBenchmarkResult BuildResult(string name, string runtime, int iterations, TimeSpan elapsed)
    {
        var totalMs = elapsed.TotalMilliseconds;
        var meanMs = totalMs / iterations;
        var opsPerSecond = elapsed.TotalSeconds > 0
            ? iterations / elapsed.TotalSeconds
            : 0;

        return new OafBenchmarkResult(name, runtime, iterations, totalMs, meanMs, opsPerSecond);
    }
}
