using Oaf.Frontend.Compiler.CodeGen.Bytecode;
using Oaf.Frontend.Compiler.Diagnostics;
using Oaf.Frontend.Compiler.Driver;
using Oaf.Tests.Framework;

namespace Oaf.Tests.Unit.Bytecode;

public static class BytecodeTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("generates_branching_bytecode_for_if", GeneratesBranchingBytecodeForIf),
            ("emits_integer_specialized_binary_instructions", EmitsIntegerSpecializedBinaryInstructions),
            ("emits_integer_const_right_binary_instruction", EmitsIntegerConstRightBinaryInstruction),
            ("fuses_integer_condition_jumps", FusesIntegerConditionJumps),
            ("executes_arithmetic_return", ExecutesArithmeticReturn),
            ("executes_loop_countdown", ExecutesLoopCountdown),
            ("executes_if_with_comma_separated_conditions", ExecutesIfWithCommaSeparatedConditions),
            ("executes_module_scoped_field_access", ExecutesModuleScopedFieldAccess),
            ("executes_match_statement", ExecutesMatchStatement),
            ("executes_gc_statement", ExecutesGcStatement),
            ("executes_counted_paralloop_with_indexed_writes", ExecutesCountedParalloopWithIndexedWrites),
            ("executes_counted_paralloop_plus_equals_reduction", ExecutesCountedParalloopPlusEqualsReduction),
            ("throw_statement_returns_failed_execution", ThrowStatementReturnsFailedExecution),
            ("executes_explicit_cast_with_truncation", ExecutesExplicitCastWithTruncation),
            ("executes_jot_statement_with_console_output", ExecutesJotStatementWithConsoleOutput),
            ("executes_array_index_assignment", ExecutesArrayIndexAssignment),
            ("returns_boolean_from_fast_path", ReturnsBooleanFromFastPath),
            ("returns_char_from_fast_path", ReturnsCharFromFastPath)
        ];
    }

    private static void GeneratesBranchingBytecodeForIf()
    {
        const string source = "flux flag = 1; loop flag > 0 => flag -= 1; if flag > 0 => return 1; -> return 2;";
        var result = Compile(source);

        TestAssertions.False(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));

        var instructions = result.BytecodeProgram.Functions[0].Instructions;
        TestAssertions.True(
            instructions.Any(i => i.OpCode is BytecodeOpCode.JumpIfTrue
                or BytecodeOpCode.JumpIfBinaryIntTrue
                or BytecodeOpCode.JumpIfBinaryIntConstRightTrue),
            "Expected conditional jump instruction.");
        TestAssertions.True(instructions.Any(i => i.OpCode == BytecodeOpCode.Jump), "Expected jump instruction.");
    }

    private static void EmitsIntegerSpecializedBinaryInstructions()
    {
        const string source = "flux x = 3; flux y = 2; loop x > 0 => { y += x; x -= 1; } return y * x;";
        var result = Compile(source);
        TestAssertions.False(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));

        var instructions = result.BytecodeProgram.Functions[0].Instructions;
        TestAssertions.True(
            instructions.Any(i => i.OpCode == BytecodeOpCode.BinaryInt),
            "Expected integer-specialized binary instruction.");
    }

    private static void EmitsIntegerConstRightBinaryInstruction()
    {
        const string source = "flux x = 3; loop x > 0 => x -= 1; x += 2; return x;";
        var result = Compile(source);
        TestAssertions.False(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));

        var instructions = result.BytecodeProgram.Functions[0].Instructions;
        TestAssertions.True(
            instructions.Any(i => i.OpCode == BytecodeOpCode.BinaryIntConstRight),
            "Expected integer const-right binary instruction.");
    }

    private static void FusesIntegerConditionJumps()
    {
        const string source = "flux i = 10; loop i > 0 => i -= 1; return i;";
        var result = Compile(source);
        TestAssertions.False(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));

        var instructions = result.BytecodeProgram.Functions[0].Instructions;
        TestAssertions.True(
            instructions.Any(i => i.OpCode == BytecodeOpCode.JumpIfBinaryIntTrue
                || i.OpCode == BytecodeOpCode.JumpIfBinaryIntConstRightTrue),
            "Expected fused integer condition jump instruction.");
    }

    private static void ExecutesArithmeticReturn()
    {
        const string source = "return 1 + 2 * 3;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(7L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesLoopCountdown()
    {
        const string source = "flux x = 3; loop x > 0 => x -= 1; return x;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(0L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesIfWithCommaSeparatedConditions()
    {
        const string source = "flux a = 3; flux b = 1; if a > 2, b < 2 => return 1; -> return 0;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(1L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesModuleScopedFieldAccess()
    {
        const string source = "module app.core; struct Point [int x, int y]; p = Point[3, 4]; return p.x + p.y;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(7L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesMatchStatement()
    {
        const string source = "flux value = 2; value match => 1 -> value = 10; 2 -> value = 20; -> value = 0;;; return value;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(20L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesGcStatement()
    {
        const string source = "flux total = 1; gc => { total += 4; } return total;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(5L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesCountedParalloopWithIndexedWrites()
    {
        const string source = """
            flux values = [0, 0, 0, 0, 0, 0, 0, 0];
            paralloop 8, i => {
                values[i] = (i + 1) * (i + 1);
            }

            flux idx = 0;
            flux sum = 0;
            loop idx < 8 => {
                sum += values[idx];
                idx += 1;
            }

            return sum;
            """;
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(204L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesCountedParalloopPlusEqualsReduction()
    {
        const string source = """
            flux total = 0;
            paralloop 8, i => {
                total += i + 1;
            }

            return total;
            """;
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(36L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ThrowStatementReturnsFailedExecution()
    {
        const string source = "throw \"OperationFailed\", \"Division by zero\"; return 0;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.False(execution.Success, "Expected throw statement to fail execution.");
        var error = execution.ErrorMessage ?? string.Empty;
        TestAssertions.True(error.Contains("OperationFailed", StringComparison.Ordinal), "Expected thrown error name in runtime message.");
        TestAssertions.True(error.Contains("Division by zero", StringComparison.Ordinal), "Expected thrown error detail in runtime message.");
    }

    private static void ExecutesExplicitCastWithTruncation()
    {
        const string source = "float f = 3.9; int i = (int)f; return i;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(3L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesJotStatementWithConsoleOutput()
    {
        const string source = "Jot(42); Jot(\"ok\"); return 0;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var originalOut = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var execution = vm.Execute(result.BytecodeProgram);
            TestAssertions.True(execution.Success, execution.ErrorMessage);
            TestAssertions.Equal(0L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        TestAssertions.True(output.Contains("42\n", StringComparison.Ordinal), "Expected Jot to print integer value.");
        TestAssertions.True(output.Contains("ok\n", StringComparison.Ordinal), "Expected Jot to print string value.");
    }

    private static void ExecutesArrayIndexAssignment()
    {
        const string source = "flux values = [1, 2, 3]; values[1] = 9; return values[1];";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(9L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ReturnsBooleanFromFastPath()
    {
        const string source = "return 2 < 3;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.True(execution.ReturnValue is bool, "Expected bool return value.");
        TestAssertions.Equal(true, (bool)execution.ReturnValue!);
    }

    private static void ReturnsCharFromFastPath()
    {
        const string source = "char c = (char)65; return c;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.True(execution.ReturnValue is char, "Expected char return value.");
        TestAssertions.Equal('A', (char)execution.ReturnValue!);
    }

    private static CompilationResult Compile(string source)
    {
        var driver = new CompilerDriver();
        return driver.CompileSource(source);
    }
}
