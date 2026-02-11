using Oaf.Frontend.Compiler.Diagnostics;
using Oaf.Tests.Framework;

namespace Oaf.Tests.Unit.TypeChecker;

public static class TypeCheckerTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("reports_immutable_assignment", ReportsImmutableAssignment),
            ("resolves_basic_scopes", ResolvesBasicScopes),
            ("binds_user_defined_and_generic_types", BindsUserDefinedAndGenericTypes),
            ("reports_generic_arity_mismatch", ReportsGenericArityMismatch),
            ("infers_local_type_from_expression", InfersLocalTypeFromExpression),
            ("applies_numeric_widening_coercions", AppliesNumericWideningCoercions),
            ("rejects_narrowing_numeric_conversion", RejectsNarrowingNumericConversion),
            ("allows_explicit_narrowing_cast", AllowsExplicitNarrowingCast),
            ("rejects_invalid_explicit_cast", RejectsInvalidExplicitCast),
            ("reports_break_outside_loop_with_location", ReportsBreakOutsideLoopWithLocation),
            ("allows_break_and_continue_inside_loop", AllowsBreakAndContinueInsideLoop)
        ];
    }

    private static void ReportsImmutableAssignment()
    {
        const string source = "count = 1; count += 2;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.True(diagnostics.HasErrors, "Expected immutable assignment error.");
    }

    private static void ResolvesBasicScopes()
    {
        const string source = "flux x = 1; if true => x += 1;;;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected no type errors for valid scope usage.");
    }

    private static void BindsUserDefinedAndGenericTypes()
    {
        const string source = "struct Box<T> [T value]; enum Option<T> => Some(T), None; class Person [string name]; int x = 1; float y = x;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected user-defined type declarations and primitive typed locals to pass.");
    }

    private static void ReportsGenericArityMismatch()
    {
        const string source = "struct Box<T> [T value]; Box value = 1;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.True(diagnostics.HasErrors, "Expected missing type argument to produce an error.");
    }

    private static void InfersLocalTypeFromExpression()
    {
        const string source = "flag = 1 < 2; if flag => return;;;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected inferred bool local to be valid in if condition.");
    }

    private static void AppliesNumericWideningCoercions()
    {
        const string source = "int i = 'A'; float f = i; float g = 'B';";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected char->int and int/char->float coercions to be valid.");
    }

    private static void RejectsNarrowingNumericConversion()
    {
        const string source = "float f = 1.25; int i = f;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.True(diagnostics.HasErrors, "Expected float->int assignment to fail without explicit cast.");
    }

    private static void AllowsExplicitNarrowingCast()
    {
        const string source = "float f = 1.25; int i = (int)f; int j = (int)-1.5;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected explicit numeric casts to allow narrowing conversions.");
    }

    private static void RejectsInvalidExplicitCast()
    {
        const string source = "value = (bool)1;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.True(diagnostics.HasErrors, "Expected invalid explicit cast to be rejected.");
    }

    private static void ReportsBreakOutsideLoopWithLocation()
    {
        const string source = "break;";
        var diagnostics = CompileAndTypeCheck(source);

        TestAssertions.True(diagnostics.HasErrors, "Expected break outside loop to fail.");
        var first = diagnostics.Diagnostics[0];
        TestAssertions.True(first.Line > 0, "Expected diagnostic to include source line.");
        TestAssertions.True(first.Column > 0, "Expected diagnostic to include source column.");
    }

    private static void AllowsBreakAndContinueInsideLoop()
    {
        const string source = "loop true => break; continue;;;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected break/continue inside loop to be valid.");
    }

    private static DiagnosticBag CompileAndTypeCheck(string source)
    {
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        var diagnostics = new DiagnosticBag();
        diagnostics.AddRange(parser.Diagnostics.Diagnostics);

        var checker = new Frontend.Compiler.TypeChecker.TypeChecker(diagnostics);
        checker.Check(unit);
        return diagnostics;
    }
}
