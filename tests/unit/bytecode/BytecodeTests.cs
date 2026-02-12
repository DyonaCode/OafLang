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
            ("executes_explicit_cast_with_truncation", ExecutesExplicitCastWithTruncation),
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

    private static void ExecutesExplicitCastWithTruncation()
    {
        const string source = "float f = 3.9; int i = (int)f; return i;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(3L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
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
