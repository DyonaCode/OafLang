using Oaf.Frontend.Compiler.Driver;
using Oaf.Tests.Framework;
using Oaf.Tooling.Sdk;

namespace Oaf.Tests.Unit.Tooling;

public static class SdkProjectToolsTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("scaffolder_creates_project_files", ScaffolderCreatesProjectFiles),
            ("scaffolder_requires_force_for_non_empty_directory", ScaffolderRequiresForceForNonEmptyDirectory),
            ("project_tester_runs_scaffolded_tests_and_examples", ProjectTesterRunsScaffoldedTestsAndExamples)
        ];
    }

    private static void ScaffolderCreatesProjectFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_scaffold_{Guid.NewGuid():N}");
        try
        {
            var result = OafProjectScaffolder.Create(root);

            TestAssertions.True(result.Success, result.Message);
            TestAssertions.True(File.Exists(Path.Combine(root, "main.oaf")));
            TestAssertions.True(File.Exists(Path.Combine(root, "tests", "smoke.oaf")));
            TestAssertions.True(File.Exists(Path.Combine(root, "examples", "hello.oaf")));
            TestAssertions.True(File.Exists(Path.Combine(root, "README.md")));
            TestAssertions.True(File.Exists(Path.Combine(root, "packages.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void ScaffolderRequiresForceForNonEmptyDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_scaffold_nonempty_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "existing.txt"), "keep");

        try
        {
            var result = OafProjectScaffolder.Create(root);
            TestAssertions.False(result.Success, "Expected scaffolder to fail for non-empty directories without --force.");

            var forcedResult = OafProjectScaffolder.Create(root, force: true);
            TestAssertions.True(forcedResult.Success, forcedResult.Message);
            TestAssertions.True(File.Exists(Path.Combine(root, "main.oaf")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void ProjectTesterRunsScaffoldedTestsAndExamples()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_project_test_{Guid.NewGuid():N}");
        try
        {
            var scaffold = OafProjectScaffolder.Create(root);
            TestAssertions.True(scaffold.Success, scaffold.Message);

            var report = OafProjectTester.Run(
                root,
                CompilerCompilationTarget.Bytecode,
                OafProjectTestRuntime.Vm);

            TestAssertions.Equal(2, report.TotalFiles);
            TestAssertions.Equal(2, report.PassedFiles);
            TestAssertions.Equal(0, report.FailedFiles);
            TestAssertions.False(report.PackageVerificationAttempted, "Expected package verify to be skipped without a lock file.");
            TestAssertions.False(report.HasFailures, "Expected scaffolded project tests to pass.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
