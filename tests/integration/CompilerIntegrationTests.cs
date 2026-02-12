using Oaf.Frontend.Compiler.CodeGen.Bytecode;
using Oaf.Frontend.Compiler.Diagnostics;
using Oaf.Frontend.Compiler.Driver;
using Oaf.Tests.Framework;
using Oaf.Tooling.Documentation;
using Oaf.Tooling.Formatting;
using Oaf.Tooling.PackageManagement;

namespace Oaf.Tests.Integration;

public static class CompilerIntegrationTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("compiles_and_executes_end_to_end_program", CompilesAndExecutesEndToEndProgram),
            ("compilation_pipeline_reports_type_errors_with_location", CompilationPipelineReportsTypeErrorsWithLocation),
            ("tooling_pipeline_generates_docs_and_installs_packages", ToolingPipelineGeneratesDocsAndInstallsPackages)
        ];
    }

    private static void CompilesAndExecutesEndToEndProgram()
    {
        const string source = """
            struct Pair<T> [T left, T right];
            flux sum = 0;
            flux i = 10;
            loop i > 0 => {
                sum += i;
                i -= 1;
            }
            return sum;
            """;

        var driver = new CompilerDriver();
        var result = driver.CompileSource(source);

        TestAssertions.True(result.Success, "Expected full compilation pipeline to succeed.");

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);
        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(55L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void CompilationPipelineReportsTypeErrorsWithLocation()
    {
        const string source = """
            float f = 1.2;
            int i = f;
            return i;
            """;

        var driver = new CompilerDriver();
        var result = driver.CompileSource(source);

        TestAssertions.False(result.Success, "Expected invalid narrowing conversion to fail.");
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        TestAssertions.True(error is not null, "Expected at least one error diagnostic.");
        TestAssertions.True(error!.Line > 0, "Expected diagnostic line information.");
        TestAssertions.True(error.Column > 0, "Expected diagnostic column information.");
    }

    private static void ToolingPipelineGeneratesDocsAndInstallsPackages()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_integration_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var sourcePath = Path.Combine(root, "main.oaf");
            var source = """
                struct User [string name];
                flux value=1+2;
                return value;
                """;
            File.WriteAllText(sourcePath, source);

            var formatted = OafCodeFormatter.Format(source);
            TestAssertions.True(formatted.Contains("flux value = 1 + 2;", StringComparison.Ordinal));
            File.WriteAllText(sourcePath, formatted);

            var manifestPath = Path.Combine(root, "packages.txt");
            TestAssertions.True(OafPackageManager.InitManifest(manifestPath, out _));
            TestAssertions.True(OafPackageManager.AddDependency(manifestPath, "stdlib.core@1.0.0", out _));
            TestAssertions.True(OafPackageManager.Install(manifestPath, out _));

            var docsPath = Path.Combine(root, "main.md");
            TestAssertions.True(OafDocumentationGenerator.GenerateFromPath(sourcePath, docsPath, out _));
            TestAssertions.True(File.Exists(docsPath));

            var driver = new CompilerDriver();
            var result = driver.CompileSource(File.ReadAllText(sourcePath));
            TestAssertions.True(result.Success, "Expected formatted source to compile.");

            var lockPath = Path.Combine(root, "packages.lock");
            TestAssertions.True(File.Exists(lockPath), "Expected package lock file.");
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
