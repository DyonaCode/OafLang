using Oaf.Frontend.Compiler.CodeGen;
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
            ("eliminates_dead_temporaries_from_expression_statements", EliminatesDeadTemporariesFromExpressionStatements)
        ];
    }

    private static void LowersAssignmentsToIr()
    {
        const string source = "flux x = 1; x = x + 2;";
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
        const string source = "flag = 1 < 2; if flag => return 1; -> return 2;;;";
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
        const string source = "flux x = 1 + 2 * 3;";
        var result = Compile(source);

        var instructions = result.IrModule.Functions[0].Blocks.SelectMany(block => block.Instructions).ToList();
        TestAssertions.False(instructions.Any(i => i is IrBinaryInstruction), "Expected binary instructions to be folded away.");

        var assignment = instructions
            .OfType<IrAssignInstruction>()
            .FirstOrDefault(i => i.Destination is IrVariableValue variable && variable.Name == "x");

        TestAssertions.True(assignment is not null, "Expected final assignment to variable x.");
        TestAssertions.True(
            assignment!.Source is IrConstantValue constant && constant.Value is long longValue && longValue == 7,
            "Expected folded constant value 7.");
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

    private static CompilationResult Compile(string source)
    {
        var driver = new CompilerDriver();
        return driver.CompileSource(source);
    }
}
