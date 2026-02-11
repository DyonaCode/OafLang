using Oaf.Frontend.Compiler.Diagnostics;
using Oaf.Frontend.Compiler.Ownership;
using Oaf.Tests.Framework;

namespace Oaf.Tests.Unit.Ownership;

public static class OwnershipTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("reports_use_after_move", ReportsUseAfterMove),
            ("allows_reassignment_after_move", AllowsReassignmentAfterMove),
            ("copy_types_are_not_moved", CopyTypesAreNotMoved),
            ("reports_self_move_assignment", ReportsSelfMoveAssignment),
            ("reports_move_from_already_moved_value", ReportsMoveFromAlreadyMovedValue)
        ];
    }

    private static void ReportsUseAfterMove()
    {
        const string source = "flux text = \"hello\"; flux other = text; text;";
        var diagnostics = AnalyzeOwnership(source);
        TestAssertions.True(HasOwnershipErrors(diagnostics), "Expected use-after-move diagnostic.");
    }

    private static void AllowsReassignmentAfterMove()
    {
        const string source = "flux text = \"hello\"; flux other = text; text = \"new\"; text;";
        var diagnostics = AnalyzeOwnership(source);
        TestAssertions.False(HasOwnershipErrors(diagnostics), "Expected reassignment to restore ownership.");
    }

    private static void CopyTypesAreNotMoved()
    {
        const string source = "flux a = 1; flux b = a; a;";
        var diagnostics = AnalyzeOwnership(source);
        TestAssertions.False(HasOwnershipErrors(diagnostics), "Expected primitive copy types to remain usable after assignment.");
    }

    private static void ReportsSelfMoveAssignment()
    {
        const string source = "flux text = \"hello\"; text = text;";
        var diagnostics = AnalyzeOwnership(source);
        TestAssertions.True(HasOwnershipErrors(diagnostics), "Expected self-move assignment diagnostic.");
    }

    private static void ReportsMoveFromAlreadyMovedValue()
    {
        const string source = "flux text = \"hello\"; flux other = text; flux third = text;";
        var diagnostics = AnalyzeOwnership(source);
        TestAssertions.True(HasOwnershipErrors(diagnostics), "Expected second move-from diagnostic.");
    }

    private static DiagnosticBag AnalyzeOwnership(string source)
    {
        var parser = new Frontend.Compiler.Parser.Parser(source);
        var unit = parser.ParseCompilationUnit();

        var diagnostics = new DiagnosticBag();
        diagnostics.AddRange(parser.Diagnostics.Diagnostics);

        var typeChecker = new Frontend.Compiler.TypeChecker.TypeChecker(diagnostics);
        typeChecker.Check(unit);

        var analyzer = new OwnershipAnalyzer(diagnostics);
        analyzer.Analyze(unit);
        return diagnostics;
    }

    private static bool HasOwnershipErrors(DiagnosticBag diagnostics)
    {
        return diagnostics.Diagnostics.Any(d => d.Code == "OWN001");
    }
}
