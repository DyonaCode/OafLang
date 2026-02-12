using Oaf.Frontend.Compiler.CodeGen;
using Oaf.Frontend.Compiler.Diagnostics;
using Oaf.Frontend.Compiler.Driver;
using Oaf.Tests.Framework;

namespace Oaf.Tests.Unit.CodeGen;

public static class MlirPrinterTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("prints_mlir_module_for_basic_program", PrintsMlirModuleForBasicProgram),
            ("prints_mlir_branch_metadata_for_control_flow", PrintsMlirBranchMetadataForControlFlow)
        ];
    }

    private static void PrintsMlirModuleForBasicProgram()
    {
        var result = Compile("flux x = 3; loop x > 0 => x -= 1; return x + 2;");
        TestAssertions.False(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), "Expected source to compile.");

        var mlir = MlirPrinter.Print(result.IrModule);
        TestAssertions.True(mlir.Contains("module {", StringComparison.Ordinal), "Expected MLIR module header.");
        TestAssertions.True(mlir.Contains("func.func @main()", StringComparison.Ordinal), "Expected MLIR function emission.");
        TestAssertions.True(mlir.Contains("\"oaf.binary\"", StringComparison.Ordinal), "Expected binary operation emission.");
        TestAssertions.True(mlir.Contains("\"oaf.return\"", StringComparison.Ordinal), "Expected return operation emission.");
    }

    private static void PrintsMlirBranchMetadataForControlFlow()
    {
        var result = Compile("flux x = 1; loop x > 0 => x -= 1; if x > 0 => return 1; -> return 2;");
        TestAssertions.False(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), "Expected source to compile.");

        var mlir = MlirPrinter.Print(result.IrModule);
        TestAssertions.True(mlir.Contains("\"oaf.cond_br\"", StringComparison.Ordinal), "Expected conditional branch emission.");
        TestAssertions.True(mlir.Contains("true = \"if_then_", StringComparison.Ordinal), "Expected true branch label metadata.");
        TestAssertions.True(mlir.Contains("false = \"if_else_", StringComparison.Ordinal), "Expected false branch label metadata.");
    }

    private static CompilationResult Compile(string source)
    {
        var driver = new CompilerDriver();
        return driver.CompileSource(source);
    }
}
