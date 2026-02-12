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
            ("benchmark_regression_gate_ignores_results_within_threshold", BenchmarkRegressionGateIgnoresResultsWithinThreshold),
            ("benchmark_regression_gate_supports_per_benchmark_threshold_overrides", BenchmarkRegressionGateSupportsPerBenchmarkThresholdOverrides),
            ("benchmark_regression_gate_uses_selected_statistic", BenchmarkRegressionGateUsesSelectedStatistic)
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
            TestAssertions.True(result.MedianMilliseconds >= 0, "Median time must be non-negative.");
            TestAssertions.True(result.P95Milliseconds >= 0, "P95 time must be non-negative.");
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
            new OafBenchmarkResult("lexer", "oaf", 10, 200, 20, 19, 23, 0.5),
            new OafBenchmarkResult("lexer", "csharp", 10, 20, 2, 2, 3, 5),
            new OafBenchmarkResult("bytecode_vm", "oaf", 10, 80, 8, 7, 10, 1.25),
            new OafBenchmarkResult("bytecode_vm", "csharp", 10, 20, 2, 2, 3, 5)
        };

        var regressions = OafBenchmarkRunner.AnalyzeAgainstBaselines(results, maxAllowedRatio: 5.0);
        TestAssertions.Equal(1, regressions.Count);

        var regression = regressions[0];
        TestAssertions.True(string.Equals(regression.Name, "lexer", StringComparison.Ordinal));
        TestAssertions.True(regression.Statistic == BenchmarkStatistic.Mean);
        TestAssertions.True(regression.Ratio > regression.MaxAllowedRatio);
    }

    private static void BenchmarkRegressionGateIgnoresResultsWithinThreshold()
    {
        var results = new[]
        {
            new OafBenchmarkResult("compiler_pipeline", "oaf", 10, 45, 4.5, 4.2, 5.5, 2.22),
            new OafBenchmarkResult("compiler_pipeline", "csharp", 10, 20, 2, 1.9, 2.8, 5)
        };

        var regressions = OafBenchmarkRunner.AnalyzeAgainstBaselines(results, maxAllowedRatio: 3.0);
        TestAssertions.Equal(0, regressions.Count);
    }

    private static void BenchmarkRegressionGateSupportsPerBenchmarkThresholdOverrides()
    {
        var results = new[]
        {
            new OafBenchmarkResult("lexer", "oaf", 10, 40, 4.0, 4.0, 4.0, 2.5),
            new OafBenchmarkResult("lexer", "csharp", 10, 20, 2.0, 2.0, 2.0, 5),
            new OafBenchmarkResult("bytecode_vm", "oaf", 10, 70, 7.0, 7.0, 7.0, 1.4),
            new OafBenchmarkResult("bytecode_vm", "csharp", 10, 20, 2.0, 2.0, 2.0, 5)
        };

        var overrides = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["lexer"] = 2.5,
            ["bytecode_vm"] = 3.0
        };

        var regressions = OafBenchmarkRunner.AnalyzeAgainstBaselines(
            results,
            maxAllowedRatio: 2.0,
            statistic: BenchmarkStatistic.Mean,
            perBenchmarkMaxAllowedRatios: overrides);

        TestAssertions.Equal(1, regressions.Count);
        TestAssertions.True(string.Equals(regressions[0].Name, "bytecode_vm", StringComparison.Ordinal));
        TestAssertions.True(Math.Abs(regressions[0].MaxAllowedRatio - 3.0) < 0.0001);
    }

    private static void BenchmarkRegressionGateUsesSelectedStatistic()
    {
        var results = new[]
        {
            new OafBenchmarkResult("lexer", "oaf", 10, 21, 2.1, 2.0, 4.0, 4.76),
            new OafBenchmarkResult("lexer", "csharp", 10, 20, 2.0, 2.0, 2.0, 5)
        };

        var meanRegressions = OafBenchmarkRunner.AnalyzeAgainstBaselines(
            results,
            maxAllowedRatio: 1.5,
            statistic: BenchmarkStatistic.Mean);
        TestAssertions.Equal(0, meanRegressions.Count);

        var p95Regressions = OafBenchmarkRunner.AnalyzeAgainstBaselines(
            results,
            maxAllowedRatio: 1.5,
            statistic: BenchmarkStatistic.P95);
        TestAssertions.Equal(1, p95Regressions.Count);
        TestAssertions.True(p95Regressions[0].Statistic == BenchmarkStatistic.P95);
    }
}
