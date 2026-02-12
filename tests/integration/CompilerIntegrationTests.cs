using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Oaf.Frontend.Compiler.CodeGen.Bytecode;
using Oaf.Frontend.Compiler.Diagnostics;
using Oaf.Frontend.Compiler.Driver;
using Oaf.Tests.Framework;
using Oaf.Tooling.Benchmarking;
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
            ("tooling_pipeline_generates_docs_and_installs_packages", ToolingPipelineGeneratesDocsAndInstallsPackages),
            ("package_prelude_can_be_composed_into_compilation", PackagePreludeCanBeComposedIntoCompilation),
            ("bytecode_and_mlir_targets_produce_identical_vm_results", BytecodeAndMlirTargetsProduceIdenticalVmResults),
            ("vm_and_native_runtimes_produce_identical_results", VmAndNativeRuntimesProduceIdenticalResults),
            ("mlir_target_vm_and_native_runtimes_produce_identical_results", MlirTargetVmAndNativeRuntimesProduceIdenticalResults)
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

    private static void PackagePreludeCanBeComposedIntoCompilation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_prelude_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var registryDir = Path.Combine(root, "registry");
            Directory.CreateDirectory(registryDir);

            var artifactPath = Path.Combine(registryDir, "pkg.math-1.0.0.oafpkg");
            using (var archive = ZipFile.Open(artifactPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("pkg/math.oaf");
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes("module pkg.math; flux pkgValue = 41;");
                stream.Write(bytes, 0, bytes.Length);
            }

            var artifactHash = ComputeFileSha256Hex(artifactPath);
            var sourceIndexPath = Path.Combine(registryDir, "index.json");
            File.WriteAllText(
                sourceIndexPath,
                $$"""
                {
                  "source": "localtest",
                  "packages": [
                    {
                      "name": "pkg.math",
                      "version": "1.0.0",
                      "artifact": "./pkg.math-1.0.0.oafpkg",
                      "sha256": "{{artifactHash}}"
                    }
                  ]
                }
                """);

            File.WriteAllText(Path.Combine(root, OafPackageManager.DefaultSourcesFileName), sourceIndexPath + Environment.NewLine);
            var manifestPath = Path.Combine(root, OafPackageManager.DefaultManifestFileName);
            TestAssertions.True(OafPackageManager.InitManifest(manifestPath, out _));
            TestAssertions.True(OafPackageManager.AddDependency(manifestPath, "pkg.math@1.0.0", out _));
            TestAssertions.True(OafPackageManager.Install(manifestPath, out _));

            const string entrySource = "import pkg.math; return pkg.math.pkgValue + 1;";
            TestAssertions.True(
                OafPackageManager.TryComposeCompilationSource(entrySource, root, out var composedSource, out _),
                "Expected package prelude composition to succeed.");

            var driver = new CompilerDriver(enableCompilationCache: false);
            var result = driver.CompileSource(composedSource);
            TestAssertions.True(result.Success, "Expected composed package source to compile.");

            var vm = new BytecodeVirtualMachine();
            var execution = vm.Execute(result.BytecodeProgram);
            TestAssertions.True(execution.Success, "Expected execution to succeed.");
            TestAssertions.Equal(42L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string ComputeFileSha256Hex(string filePath)
    {
        var hash = SHA256.HashData(File.ReadAllBytes(filePath));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void BytecodeAndMlirTargetsProduceIdenticalVmResults()
    {
        const string source = """
            flux i = 1;
            flux acc = 0;
            loop i <= 250 => {
                if (i % 3) == 0 => {
                    acc += i * 2;
                } -> {
                    acc += i;
                }
                i += 1;
            }
            return acc;
            """;

        var driver = new CompilerDriver(enableCompilationCache: false);
        var bytecodeResult = driver.CompileSource(source, CompilerCompilationTarget.Bytecode);
        var mlirResult = driver.CompileSource(source, CompilerCompilationTarget.Mlir);

        TestAssertions.True(bytecodeResult.Success, "Expected bytecode target compilation to succeed.");
        TestAssertions.True(mlirResult.Success, "Expected MLIR target compilation to succeed.");

        var vm = new BytecodeVirtualMachine();
        var bytecodeExecution = vm.Execute(bytecodeResult.BytecodeProgram);
        var mlirExecution = vm.Execute(mlirResult.BytecodeProgram);

        TestAssertions.True(bytecodeExecution.Success, bytecodeExecution.ErrorMessage);
        TestAssertions.True(mlirExecution.Success, mlirExecution.ErrorMessage);
        TestAssertions.Equal(
            Convert.ToInt64(bytecodeExecution.ReturnValue),
            Convert.ToInt64(mlirExecution.ReturnValue),
            "Expected bytecode and MLIR targets to produce identical VM results.");
    }

    private static void VmAndNativeRuntimesProduceIdenticalResults()
    {
        if (!OafNativeKernelExecutor.IsNativeCompilerAvailable())
        {
            return;
        }

        const string source = """
            flux i = 1;
            flux acc = 0;
            loop i <= 1000 => {
                acc = acc ^ ((i * 31) + (i % 7));
                i += 1;
            }
            return acc;
            """;

        var driver = new CompilerDriver(enableCompilationCache: false);
        var compilation = driver.CompileSource(source, CompilerCompilationTarget.Bytecode);
        TestAssertions.True(compilation.Success, "Expected compilation to succeed.");

        var vm = new BytecodeVirtualMachine();
        var vmExecution = vm.Execute(compilation.BytecodeProgram);
        TestAssertions.True(vmExecution.Success, vmExecution.ErrorMessage);

        using var nativeHandle = OafNativeKernelExecutor.CreateHandle(compilation.BytecodeProgram);
        var nativeValue = nativeHandle.ExecuteOnce();

        TestAssertions.Equal(
            Convert.ToInt64(vmExecution.ReturnValue),
            Convert.ToInt64(nativeValue),
            "Expected VM and native executions to produce identical results.");
    }

    private static void MlirTargetVmAndNativeRuntimesProduceIdenticalResults()
    {
        if (!OafNativeKernelExecutor.IsNativeCompilerAvailable())
        {
            return;
        }

        const string source = """
            flux i = 1;
            flux acc = 1;
            loop i <= 500 => {
                acc += (i * 13) ^ (acc % 11);
                i += 1;
            }
            return acc;
            """;

        var driver = new CompilerDriver(enableCompilationCache: false);
        var compilation = driver.CompileSource(source, CompilerCompilationTarget.Mlir);
        TestAssertions.True(compilation.Success, "Expected MLIR target compilation to succeed.");

        var vm = new BytecodeVirtualMachine();
        var vmExecution = vm.Execute(compilation.BytecodeProgram);
        TestAssertions.True(vmExecution.Success, vmExecution.ErrorMessage);

        using var nativeHandle = OafNativeKernelExecutor.CreateHandle(compilation.BytecodeProgram);
        var nativeValue = nativeHandle.ExecuteOnce();

        TestAssertions.Equal(
            Convert.ToInt64(vmExecution.ReturnValue),
            Convert.ToInt64(nativeValue),
            "Expected MLIR-targeted VM and native executions to produce identical results.");
    }
}
