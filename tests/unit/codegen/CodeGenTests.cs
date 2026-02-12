using Oaf.Frontend.Compiler.CodeGen;
using Oaf.Frontend.Compiler.CodeGen.Bytecode;
using Oaf.Frontend.Compiler.Diagnostics;
using Oaf.Frontend.Compiler.Driver;
using Oaf.Tests.Framework;

namespace Oaf.Tests.Unit.CodeGen;

public static class CodeGenTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("lowers_assignments_to_ir", LowersAssignmentsToIr),
            ("lowers_if_statement_to_branch_blocks", LowersIfStatementToBranchBlocks),
            ("constant_folding_reduces_arithmetic", ConstantFoldingReducesArithmetic),
            ("copy_propagation_removes_redundant_variable_hops", CopyPropagationRemovesRedundantVariableHops),
            ("dead_store_elimination_removes_overwritten_assignment", DeadStoreEliminationRemovesOverwrittenAssignment),
            ("eliminates_dead_temporaries_from_expression_statements", EliminatesDeadTemporariesFromExpressionStatements),
            ("mlir_target_compiles_to_runnable_bytecode", MlirTargetCompilesToRunnableBytecode)
        ];
    }

    private static void LowersAssignmentsToIr()
    {
        const string source = "flux x = 3; loop x > 0 => x -= 1; return x + 1;";
        var result = Compile(source);

        TestAssertions.False(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));

        var instructions = result.IrModule.Functions[0].Blocks.SelectMany(block => block.Instructions).ToList();
        TestAssertions.True(instructions.Any(i => i is IrBinaryInstruction), "Expected binary instruction for x + 2.");
        TestAssertions.True(
            instructions.Any(i => i is IrAssignInstruction assign && assign.Destination is IrVariableValue variable && variable.Name == "x"),
            "Expected assignment into variable 'x'.");
    }

    private static void LowersIfStatementToBranchBlocks()
    {
        const string source = "flux flag = 1; loop flag > 0 => flag -= 1; if flag > 0 => return 1; -> return 2;";
        var result = Compile(source);

        TestAssertions.False(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));

        var function = result.IrModule.Functions[0];
        TestAssertions.True(function.Blocks.Count >= 3, "Expected multiple blocks for if lowering.");
        TestAssertions.True(
            function.Blocks.SelectMany(block => block.Instructions).Any(i => i is IrBranchInstruction),
            "Expected branch instruction for if statement.");
    }

    private static void ConstantFoldingReducesArithmetic()
    {
        const string source = "flux x = 1 + 2 * 3; return x;";
        var result = Compile(source);

        var instructions = result.IrModule.Functions[0].Blocks.SelectMany(block => block.Instructions).ToList();
        TestAssertions.False(instructions.Any(i => i is IrBinaryInstruction), "Expected binary instructions to be folded away.");
        var ret = instructions.OfType<IrReturnInstruction>().LastOrDefault();
        TestAssertions.True(ret is not null, "Expected return instruction.");
        TestAssertions.True(
            ret!.Value is IrConstantValue constant && constant.Value is long longValue && longValue == 7,
            "Expected folded constant return value 7.");
    }

    private static void EliminatesDeadTemporariesFromExpressionStatements()
    {
        const string source = "1 + 2;";
        var result = Compile(source);

        var instructions = result.IrModule.Functions[0].Blocks.SelectMany(block => block.Instructions).ToList();
        TestAssertions.False(instructions.Any(i => i is IrBinaryInstruction or IrUnaryInstruction or IrCastInstruction), "Expected dead temporary computations to be removed.");

        var temporaryAssignments = instructions
            .OfType<IrAssignInstruction>()
            .Where(assign => assign.Destination is IrTemporaryValue)
            .ToList();

        TestAssertions.Equal(0, temporaryAssignments.Count, "Expected no remaining assignments into temporaries.");
    }

    private static void CopyPropagationRemovesRedundantVariableHops()
    {
        const string source = "flux x = 3; loop x > 0 => x -= 1; flux y = x; return y;";
        var result = Compile(source);
        TestAssertions.False(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));

        var instructions = result.IrModule.Functions[0].Blocks.SelectMany(block => block.Instructions).ToList();
        TestAssertions.False(
            instructions.Any(i => i is IrAssignInstruction assign
                && assign.Destination is IrVariableValue variable
                && string.Equals(variable.Name, "y", StringComparison.Ordinal)),
            "Expected copy-propagation + dead-store-elimination to remove assignment to y.");

        var ret = instructions.OfType<IrReturnInstruction>().LastOrDefault();
        TestAssertions.True(ret is not null, "Expected return instruction.");
        TestAssertions.True(
            ret!.Value is IrVariableValue returnVariable && string.Equals(returnVariable.Name, "x", StringComparison.Ordinal),
            "Expected return to reference x directly after copy propagation.");
    }

    private static void DeadStoreEliminationRemovesOverwrittenAssignment()
    {
        const string source = "flux x = 1; x = 2; return x;";
        var result = Compile(source);
        TestAssertions.False(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));

        var instructions = result.IrModule.Functions[0].Blocks.SelectMany(block => block.Instructions).ToList();
        var xAssignments = instructions
            .OfType<IrAssignInstruction>()
            .Where(assign => assign.Destination is IrVariableValue variable
                && string.Equals(variable.Name, "x", StringComparison.Ordinal))
            .ToList();
        TestAssertions.Equal(0, xAssignments.Count);

        var ret = instructions.OfType<IrReturnInstruction>().LastOrDefault();
        TestAssertions.True(ret is not null, "Expected return instruction.");
        TestAssertions.True(
            ret!.Value is IrConstantValue constant && constant.Value is long value && value == 2,
            "Expected dead-store elimination to preserve only the final observable value.");
    }

    private static void MlirTargetCompilesToRunnableBytecode()
    {
        const string source = "flux value = 1 + 2; return value;";
        var driver = new CompilerDriver();
        var result = driver.CompileSource(source, CompilerCompilationTarget.Mlir);

        TestAssertions.False(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), "Expected MLIR target compilation to succeed.");

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);
        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(3L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static CompilationResult Compile(string source)
    {
        var driver = new CompilerDriver();
        return driver.CompileSource(source);
    }
}
