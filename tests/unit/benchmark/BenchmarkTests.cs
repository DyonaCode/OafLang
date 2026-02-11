using Oaf.Tests.Framework;
using Oaf.Tooling.Benchmarking;

namespace Oaf.Tests.Unit.Benchmark;

public static class BenchmarkTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("benchmark_runner_returns_expected_metrics", BenchmarkRunnerReturnsExpectedMetrics),
            ("benchmark_regression_gate_flags_slowdown", BenchmarkRegressionGateFlagsSlowdown),
            ("benchmark_regression_gate_ignores_results_within_threshold", BenchmarkRegressionGateIgnoresResultsWithinThreshold)
        ];
    }

    private static void BenchmarkRunnerReturnsExpectedMetrics()
    {
        var results = OafBenchmarkRunner.RunAll(iterations: 5);

        TestAssertions.Equal(6, results.Count);
        foreach (var result in results)
        {
            TestAssertions.True(result.Name.Length > 0, "Benchmark name should be populated.");
            TestAssertions.True(result.Runtime.Length > 0, "Runtime label should be populated.");
            TestAssertions.True(result.Iterations == 5, "Expected iteration count to be preserved.");
            TestAssertions.True(result.TotalMilliseconds >= 0, "Total time must be non-negative.");
            TestAssertions.True(result.MeanMilliseconds >= 0, "Mean time must be non-negative.");
            TestAssertions.True(result.OpsPerSecond >= 0, "Ops/s must be non-negative.");
        }

        var benchmarkNames = results.Select(static result => result.Name).Distinct().ToArray();
        TestAssertions.SequenceEqual(["lexer", "compiler_pipeline", "bytecode_vm"], benchmarkNames);

        foreach (var name in benchmarkNames)
        {
            var runtimes = results.Where(result => result.Name == name)
                .Select(static result => result.Runtime)
                .OrderBy(static runtime => runtime, StringComparer.Ordinal)
                .ToArray();

            TestAssertions.SequenceEqual(["csharp", "oaf"], runtimes, $"Expected baseline and Oaf results for '{name}'.");
        }
    }

    private static void BenchmarkRegressionGateFlagsSlowdown()
    {
        var results = new[]
        {
            new OafBenchmarkResult("lexer", "oaf", 10, 200, 20, 0.5),
            new OafBenchmarkResult("lexer", "csharp", 10, 20, 2, 5),
            new OafBenchmarkResult("bytecode_vm", "oaf", 10, 80, 8, 1.25),
            new OafBenchmarkResult("bytecode_vm", "csharp", 10, 20, 2, 5)
        };

        var regressions = OafBenchmarkRunner.AnalyzeAgainstBaselines(results, maxAllowedMeanRatio: 5.0);
        TestAssertions.Equal(1, regressions.Count);

        var regression = regressions[0];
        TestAssertions.True(string.Equals(regression.Name, "lexer", StringComparison.Ordinal));
        TestAssertions.True(regression.MeanRatio > regression.MaxAllowedMeanRatio);
    }

    private static void BenchmarkRegressionGateIgnoresResultsWithinThreshold()
    {
        var results = new[]
        {
            new OafBenchmarkResult("compiler_pipeline", "oaf", 10, 45, 4.5, 2.22),
            new OafBenchmarkResult("compiler_pipeline", "csharp", 10, 20, 2, 5)
        };

        var regressions = OafBenchmarkRunner.AnalyzeAgainstBaselines(results, maxAllowedMeanRatio: 3.0);
        TestAssertions.Equal(0, regressions.Count);
    }
}
