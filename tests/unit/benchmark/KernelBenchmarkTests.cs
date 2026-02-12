using Oaf.Tests.Framework;
using Oaf.Frontend.Compiler.Driver;
using Oaf.Tooling.Benchmarking;

namespace Oaf.Tests.Unit.Benchmark;

public static class KernelBenchmarkTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("oaf_kernel_runner_executes_all_algorithms", OafKernelRunnerExecutesAllAlgorithms),
            ("oaf_kernel_runner_supports_mlir_compilation_target", OafKernelRunnerSupportsMlirCompilationTarget)
        ];
    }

    private static void OafKernelRunnerExecutesAllAlgorithms()
    {
        var results = OafKernelBenchmarkRunner.Run(
            iterations: 2,
            sumN: 2000,
            primeN: 1000,
            matrixN: 12);

        TestAssertions.Equal(6, results.Count);
        var algorithms = results.Select(static result => result.Algorithm).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        TestAssertions.SequenceEqual(
            ["affine_grid", "branch_mix", "gcd_fold", "lcg_stream", "prime_trial", "sum_xor"],
            algorithms);

        foreach (var result in results)
        {
            TestAssertions.True(string.Equals(result.Language, "oaf", StringComparison.Ordinal));
            TestAssertions.Equal(2, result.Iterations);
            TestAssertions.True(result.TotalMilliseconds >= 0, "Total benchmark time should be non-negative.");
            TestAssertions.True(result.MeanMilliseconds >= 0, "Mean benchmark time should be non-negative.");
        }
    }

    private static void OafKernelRunnerSupportsMlirCompilationTarget()
    {
        var results = OafKernelBenchmarkRunner.Run(
            iterations: 1,
            sumN: 1000,
            primeN: 500,
            matrixN: 8,
            executionMode: OafKernelExecutionMode.BytecodeVm,
            compilationTarget: CompilerCompilationTarget.Mlir);

        TestAssertions.Equal(6, results.Count);
        foreach (var result in results)
        {
            TestAssertions.True(string.Equals(result.Language, "oaf", StringComparison.Ordinal));
            TestAssertions.Equal(1, result.Iterations);
        }
    }
}
