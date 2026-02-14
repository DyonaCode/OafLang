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
            ("allows_integer_bitwise_and_or", AllowsIntegerBitwiseAndOr),
            ("rejects_narrowing_numeric_conversion", RejectsNarrowingNumericConversion),
            ("allows_explicit_narrowing_cast", AllowsExplicitNarrowingCast),
            ("rejects_invalid_explicit_cast", RejectsInvalidExplicitCast),
            ("supports_constructor_and_field_access", SupportsConstructorAndFieldAccess),
            ("supports_module_scoped_constructor_and_field_access", SupportsModuleScopedConstructorAndFieldAccess),
            ("supports_enum_variant_access", SupportsEnumVariantAccess),
            ("supports_match_statement", SupportsMatchStatement),
            ("supports_throw_statement", SupportsThrowStatement),
            ("supports_gc_statement", SupportsGcStatement),
            ("supports_public_class_declaration_with_modifiers", SupportsPublicClassDeclarationWithModifiers),
            ("supports_if_comma_separated_conditions", SupportsIfCommaSeparatedConditions),
            ("supports_jot_statement", SupportsJotStatement),
            ("supports_http_intrinsic_constructors", SupportsHttpIntrinsicConstructors),
            ("rejects_http_intrinsic_argument_mismatch", RejectsHttpIntrinsicArgumentMismatch),
            ("supports_array_literals_and_indexing", SupportsArrayLiteralsAndIndexing),
            ("rejects_non_integer_array_index", RejectsNonIntegerArrayIndex),
            ("supports_counted_paralloop_with_indexed_writes", SupportsCountedParalloopWithIndexedWrites),
            ("supports_counted_paralloop_plus_equals_reduction", SupportsCountedParalloopPlusEqualsReduction),
            ("rejects_counted_paralloop_non_reduction_assignment", RejectsCountedParalloopNonReductionAssignment),
            ("rejects_counted_paralloop_self_referencing_reduction", RejectsCountedParalloopSelfReferencingReduction),
            ("rejects_counted_paralloop_break_statement", RejectsCountedParalloopBreakStatement),
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
        const string source = "flux x = 1; if true => x += 1;";
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
        const string source = "flag = 1 < 2; if flag => return;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected inferred bool local to be valid in if condition.");
    }

    private static void AppliesNumericWideningCoercions()
    {
        const string source = "int i = 'A'; float f = i; float g = 'B';";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected char->int and int/char->float coercions to be valid.");
    }

    private static void AllowsIntegerBitwiseAndOr()
    {
        const string source = "int a = 6; int b = 3; int c = a & b; int d = a | b;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected '&' and '|' to support integer operands.");
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

    private static void SupportsConstructorAndFieldAccess()
    {
        const string source = "struct Point [int x, int y]; start = Point[3, 4]; int x = start.x; int y = start.y;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected constructor and field access syntax to type-check.");
    }

    private static void SupportsModuleScopedConstructorAndFieldAccess()
    {
        const string source = "module app.core; struct Point [int x, int y]; p = Point[3, 4]; int x = p.x; int y = p.y;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected module-scoped constructor and field access to type-check.");
    }

    private static void SupportsEnumVariantAccess()
    {
        const string source = "enum Status => Active, Inactive; status = Status.Active; if status == Status.Inactive => return 0; return 1;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected enum variant access to type-check.");
    }

    private static void SupportsMatchStatement()
    {
        const string source = "flux value = 2; value match => 1 -> value = 10; 2 -> value = 20; -> value = 0;;; return value;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected match statement to type-check.");
    }

    private static void SupportsThrowStatement()
    {
        const string source = "if false => throw \"OperationFailed\", \"Division by zero\"; return 1;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected throw statement to type-check.");
    }

    private static void SupportsGcStatement()
    {
        const string source = "flux total = 1; gc => { total += 2; } return total;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected gc block to type-check.");
    }

    private static void SupportsPublicClassDeclarationWithModifiers()
    {
        const string source = "public class Program public, gcoff; instance = Program[]; return 0;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected public class declaration modifiers to type-check.");
    }

    private static void SupportsIfCommaSeparatedConditions()
    {
        const string source = "flux ready = true; flux ok = false; if ready, !ok => return 1; -> return 0;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected comma-separated if conditions to type-check.");
    }

    private static void SupportsJotStatement()
    {
        const string source = "flux x = 3; Jot(x); Jot(\"done\"); return x;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected Jot statement to type-check.");
    }

    private static void SupportsHttpIntrinsicConstructors()
    {
        const string source = "enum Method => Get, Post; flux body = HttpGet[\"http://localhost\"]; flux sent = HttpSend[\"http://localhost\", Method.Post, \"x=1\", 2000, \"Accept: application/json\\nX-Trace: abc123\"]; flux status = HttpLastStatus[]; flux error = HttpLastError[]; flux reason = HttpLastReason[]; flux ct = HttpLastContentType[]; flux hs = HttpLastHeaders[]; flux server = HttpLastHeader[\"Server\"]; flux hb = HttpHeader[\"\", \"Accept\", \"application/json\"]; flux q = HttpQuery[\"http://localhost/search\", \"q\", \"bakery in berlin\"]; flux enc = HttpUrlEncode[\"bakery in berlin\"]; flux client = HttpClientOpen[\"http://localhost\"]; flux cfg = HttpClientConfigure[client, 8000, true, 5, \"oaf-http/1.0\"]; flux retryCfg = HttpClientConfigureRetry[client, 3, 100]; flux proxyCfg = HttpClientConfigureProxy[client, \"\"]; flux defs = HttpClientDefaultHeaders[client, \"Authorization: Bearer t\"]; flux resp = HttpClientSend[client, \"/search\", Method.Get, \"\", \"Accept: application/json\"]; flux sentCount = HttpClientRequestsSent[client]; flux retryCount = HttpClientRetriesUsed[client]; flux closed = HttpClientClose[client]; flux lb = HttpLastBody[]; return status + cfg + retryCfg + proxyCfg + defs + sentCount + retryCount + closed;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected HTTP intrinsic constructors to type-check.");
    }

    private static void RejectsHttpIntrinsicArgumentMismatch()
    {
        const string source = "flux body = HttpGet[]; flux sent = HttpSend[\"http://localhost\", true, 42, \"slow\", 123]; flux status = HttpLastStatus[1]; flux error = HttpLastError[\"x\"]; flux reason = HttpLastReason[1]; flux ct = HttpLastContentType[1]; flux hs = HttpLastHeaders[1]; flux server = HttpLastHeader[]; flux hb = HttpHeader[\"\", 1, \"x\"]; flux q = HttpQuery[1, \"k\", true]; flux enc = HttpUrlEncode[5]; flux client = HttpClientOpen[123]; flux cfg = HttpClientConfigure[\"x\", \"slow\", 1, false, 5]; flux retryCfg = HttpClientConfigureRetry[client, \"three\", false]; flux proxyCfg = HttpClientConfigureProxy[client, 9]; flux defs = HttpClientDefaultHeaders[client, 7]; flux resp = HttpClientSend[client, 10, true, 1, 2]; flux sentCount = HttpClientRequestsSent[]; flux retryCount = HttpClientRetriesUsed[\"x\"]; flux closed = HttpClientClose[\"x\"]; flux lb = HttpLastBody[1]; return 0;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.True(diagnostics.HasErrors, "Expected HTTP intrinsic arity mismatch to report errors.");
    }

    private static void SupportsArrayLiteralsAndIndexing()
    {
        const string source = "flux [int] values = [1, 2, 3]; flux idx = 1; values[idx] = 7; int picked = values[idx]; return picked;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected array declaration/index assignment/index read to type-check.");
    }

    private static void RejectsNonIntegerArrayIndex()
    {
        const string source = "flux values = [1, 2, 3]; flux i = 1.5; return values[i];";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.True(diagnostics.HasErrors, "Expected non-integer index to fail.");
    }

    private static void SupportsCountedParalloopWithIndexedWrites()
    {
        const string source = "flux values = [0, 0, 0, 0]; paralloop 4, i => values[i] = i + 1; return values[3];";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected counted paralloop indexed writes to type-check.");
    }

    private static void SupportsCountedParalloopPlusEqualsReduction()
    {
        const string source = "flux total = 0; paralloop 4, i => total += i; return total;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.False(diagnostics.HasErrors, "Expected counted paralloop to allow '+=' integer reduction.");
    }

    private static void RejectsCountedParalloopNonReductionAssignment()
    {
        const string source = "flux total = 0; paralloop 4, i => total = total + i; return total;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.True(diagnostics.HasErrors, "Expected counted paralloop to reject outer scalar assignment.");
    }

    private static void RejectsCountedParalloopSelfReferencingReduction()
    {
        const string source = "flux total = 0; paralloop 4, i => total += total + i; return total;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.True(diagnostics.HasErrors, "Expected counted paralloop to reject self-referencing '+=' reduction expression.");
    }

    private static void RejectsCountedParalloopBreakStatement()
    {
        const string source = "paralloop 4, i => { if i == 2 => break; } return 0;";
        var diagnostics = CompileAndTypeCheck(source);
        TestAssertions.True(diagnostics.HasErrors, "Expected counted paralloop to reject break.");
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
        const string source = "loop true => { break; continue; }";
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
