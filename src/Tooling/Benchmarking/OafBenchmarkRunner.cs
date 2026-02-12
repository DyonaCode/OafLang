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
    double MedianMilliseconds,
    double P95Milliseconds,
    double OpsPerSecond);

public readonly record struct OafBenchmarkRegression(
    string Name,
    BenchmarkStatistic Statistic,
    double Ratio,
    double MaxAllowedRatio);

public enum BenchmarkStatistic
{
    Mean,
    Median,
    P95
}

public static class OafBenchmarkRunner
{
    public const double DefaultMaxAllowedMeanRatio = 5.0;

    private const string LexerBenchmarkSource = """
        struct Pair<T> [T left, T right];
        enum Option<T> => Some(T), None;
        class Person [string name, int age];
        flux value = 0x10 + 0b1010;
        loop value > 0 => value -= 1;
        """;

    private const string CompilerBenchmarkSource = """
        struct Vec2 [float x, float y];
        flux x = 1;
        flux y = 2;
        float scale = 3.5;
        flux i = 100;
        loop i > 0 => {
            x += 1;
            y += (int)scale;
            i -= 1;
        }
        return x + y;
        """;

    private const string BytecodeBenchmarkSource = """
        flux total = 0;
        flux i = 1000;
        loop i > 0 => {
            total += i;
            i -= 1;
        }
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

    public static void PrintReport(
        TextWriter output,
        IReadOnlyList<OafBenchmarkResult> results,
        BenchmarkStatistic comparisonStatistic = BenchmarkStatistic.Mean)
    {
        output.WriteLine("Oaf Benchmark Report");
        output.WriteLine("------------------------");

        foreach (var result in results)
        {
            output.WriteLine(
                $"{result.Name,-20} runtime={result.Runtime,-8} iterations={result.Iterations,5} total_ms={result.TotalMilliseconds,10:F3} " +
                $"mean_ms={result.MeanMilliseconds,8:F4} median_ms={result.MedianMilliseconds,8:F4} " +
                $"p95_ms={result.P95Milliseconds,8:F4} ops/s={result.OpsPerSecond,10:F2}");
        }

        output.WriteLine();
        output.WriteLine($"Comparison (Oaf vs C# baseline, statistic={comparisonStatistic.ToString().ToLowerInvariant()})");
        output.WriteLine("-----------------------------------");

        foreach (var group in results.GroupBy(static result => result.Name, StringComparer.Ordinal))
        {
            var oaf = group.FirstOrDefault(static result => string.Equals(result.Runtime, "oaf", StringComparison.Ordinal));
            var csharp = group.FirstOrDefault(static result => string.Equals(result.Runtime, "csharp", StringComparison.Ordinal));

            if (string.IsNullOrEmpty(oaf.Name) || string.IsNullOrEmpty(csharp.Name))
            {
                continue;
            }

            var ratio = ComputeRatio(oaf, csharp, comparisonStatistic);
            output.WriteLine($"{group.Key,-20} {comparisonStatistic.ToString().ToLowerInvariant()}_ratio(oaf/csharp)={ratio:F3}x");
        }
    }

    public static IReadOnlyList<OafBenchmarkRegression> AnalyzeAgainstBaselines(
        IReadOnlyList<OafBenchmarkResult> results,
        double maxAllowedRatio,
        BenchmarkStatistic statistic = BenchmarkStatistic.Mean,
        IReadOnlyDictionary<string, double>? perBenchmarkMaxAllowedRatios = null)
    {
        if (maxAllowedRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAllowedRatio), "Maximum allowed ratio must be positive.");
        }

        var regressions = new List<OafBenchmarkRegression>();
        foreach (var group in results.GroupBy(static result => result.Name, StringComparer.Ordinal))
        {
            var oaf = group.FirstOrDefault(static result => string.Equals(result.Runtime, "oaf", StringComparison.Ordinal));
            var csharp = group.FirstOrDefault(static result => string.Equals(result.Runtime, "csharp", StringComparison.Ordinal));
            if (string.IsNullOrEmpty(oaf.Name) || string.IsNullOrEmpty(csharp.Name))
            {
                continue;
            }

            var threshold = maxAllowedRatio;
            if (perBenchmarkMaxAllowedRatios is not null &&
                perBenchmarkMaxAllowedRatios.TryGetValue(group.Key, out var overrideThreshold))
            {
                if (overrideThreshold <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(perBenchmarkMaxAllowedRatios), $"Threshold override for '{group.Key}' must be positive.");
                }

                threshold = overrideThreshold;
            }

            var ratio = ComputeRatio(oaf, csharp, statistic);
            if (ratio > threshold)
            {
                regressions.Add(new OafBenchmarkRegression(group.Key, statistic, ratio, threshold));
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
                $"{regression.Name,-20} {regression.Statistic.ToString().ToLowerInvariant()}_ratio(oaf/csharp)={regression.Ratio:F3}x " +
                $"threshold={regression.MaxAllowedRatio:F3}x");
        }
    }

    private static OafBenchmarkResult RunLexerBenchmark(int iterations)
    {
        var timings = MeasureIterations(iterations, () =>
        {
            var lexer = new Lexer(LexerBenchmarkSource);
            var tokens = lexer.Lex();
            if (tokens.Count == 0)
            {
                throw new InvalidOperationException("Lexer benchmark produced no tokens.");
            }
        });

        return BuildResult("lexer", "oaf", iterations, timings);
    }

    private static OafBenchmarkResult RunCompilerBenchmark(int iterations)
    {
        var driver = new CompilerDriver(enableCompilationCache: false);
        var timings = MeasureIterations(iterations, () =>
        {
            var result = driver.CompileSource(CompilerBenchmarkSource);
            if (!result.Success)
            {
                throw new InvalidOperationException("Compiler benchmark source failed compilation.");
            }
        });

        return BuildResult("compiler_pipeline", "oaf", iterations, timings);
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
        var timings = MeasureIterations(iterations, () =>
        {
            var execution = vm.Execute(program);
            if (!execution.Success)
            {
                throw new InvalidOperationException($"Bytecode benchmark execution failed: {execution.ErrorMessage}");
            }
        });

        return BuildResult("bytecode_vm", "oaf", iterations, timings);
    }

    private static OafBenchmarkResult RunCSharpLexerBaseline(int iterations)
    {
        var checksum = 0;

        var timings = MeasureIterations(iterations, () =>
        {
            checksum ^= ScanTokensWithCSharpBaseline(LexerBenchmarkSource);
        });

        if (checksum == int.MinValue)
        {
            throw new InvalidOperationException("Unreachable checksum sentinel.");
        }

        return BuildResult("lexer", "csharp", iterations, timings);
    }

    private static OafBenchmarkResult RunCSharpCompilerBaseline(int iterations)
    {
        var checksum = 0L;

        var timings = MeasureIterations(iterations, () =>
        {
            checksum += RunCompilerLikeCSharpWorkload(CompilerBenchmarkSource);
        });

        if (checksum == long.MinValue)
        {
            throw new InvalidOperationException("Unreachable checksum sentinel.");
        }

        return BuildResult("compiler_pipeline", "csharp", iterations, timings);
    }

    private static OafBenchmarkResult RunCSharpBytecodeBaseline(int iterations)
    {
        long total = 0;

        var timings = MeasureIterations(iterations, () =>
        {
            total += ExecuteBytecodeEquivalentInCSharp();
        });

        if (total == long.MinValue)
        {
            throw new InvalidOperationException("Unreachable checksum sentinel.");
        }

        return BuildResult("bytecode_vm", "csharp", iterations, timings);
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

    private static OafBenchmarkResult BuildResult(
        string name,
        string runtime,
        int iterations,
        BenchmarkTimingSummary timing)
    {
        return new OafBenchmarkResult(
            name,
            runtime,
            iterations,
            timing.TotalMilliseconds,
            timing.MeanMilliseconds,
            timing.MedianMilliseconds,
            timing.P95Milliseconds,
            timing.OpsPerSecond);
    }

    private static BenchmarkTimingSummary MeasureIterations(int iterations, Action body)
    {
        var samples = new double[iterations];
        for (var i = 0; i < iterations; i++)
        {
            var start = Stopwatch.GetTimestamp();
            body();
            var end = Stopwatch.GetTimestamp();
            samples[i] = (end - start) * 1000.0 / Stopwatch.Frequency;
        }

        var totalMs = samples.Sum();
        var meanMs = totalMs / iterations;
        var sorted = samples.ToArray();
        Array.Sort(sorted);
        var medianMs = ComputeMedian(sorted);
        var p95Ms = ComputePercentile(sorted, 0.95);
        var opsPerSecond = totalMs > 0
            ? iterations / (totalMs / 1000.0)
            : 0;

        return new BenchmarkTimingSummary(totalMs, meanMs, medianMs, p95Ms, opsPerSecond);
    }

    private static double ComputeRatio(OafBenchmarkResult oaf, OafBenchmarkResult csharp, BenchmarkStatistic statistic)
    {
        const double epsilon = 0.000001;
        var denominator = Math.Max(GetStatistic(csharp, statistic), epsilon);
        var numerator = GetStatistic(oaf, statistic);
        return numerator / denominator;
    }

    private static double GetStatistic(OafBenchmarkResult result, BenchmarkStatistic statistic)
    {
        return statistic switch
        {
            BenchmarkStatistic.Median => result.MedianMilliseconds,
            BenchmarkStatistic.P95 => result.P95Milliseconds,
            _ => result.MeanMilliseconds
        };
    }

    private static double ComputeMedian(double[] sortedSamples)
    {
        if (sortedSamples.Length == 0)
        {
            return 0;
        }

        var middle = sortedSamples.Length / 2;
        return sortedSamples.Length % 2 == 0
            ? (sortedSamples[middle - 1] + sortedSamples[middle]) / 2.0
            : sortedSamples[middle];
    }

    private static double ComputePercentile(double[] sortedSamples, double percentile)
    {
        if (sortedSamples.Length == 0)
        {
            return 0;
        }

        var position = (int)Math.Ceiling(percentile * sortedSamples.Length) - 1;
        var clamped = Math.Clamp(position, 0, sortedSamples.Length - 1);
        return sortedSamples[clamped];
    }

    private readonly record struct BenchmarkTimingSummary(
        double TotalMilliseconds,
        double MeanMilliseconds,
        double MedianMilliseconds,
        double P95Milliseconds,
        double OpsPerSecond);
}
