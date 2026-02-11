using Oaf.Tests.Framework;
using Oaf.Tooling.Formatting;

namespace Oaf.Tests.Unit.Tooling;

public static class FormatterTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("formats_operators_and_indentation", FormatsOperatorsAndIndentation),
            ("preserves_inline_comment", PreservesInlineComment)
        ];
    }

    private static void FormatsOperatorsAndIndentation()
    {
        const string source = """
            flux   total=1+2;
            if total>0=>{
            return total;
            }
            """;

        var formatted = OafCodeFormatter.Format(source);
        var lines = formatted.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        TestAssertions.Equal("flux total = 1 + 2;", lines[0]);
        TestAssertions.Equal("if total > 0 => {", lines[1]);
        TestAssertions.Equal("    return total;", lines[2]);
        TestAssertions.Equal("}", lines[3]);
    }

    private static void PreservesInlineComment()
    {
        const string source = "flux value=1; // keep comment";
        var formatted = OafCodeFormatter.Format(source);

        TestAssertions.True(formatted.Contains("// keep comment", StringComparison.Ordinal));
        TestAssertions.True(formatted.Contains("flux value = 1;", StringComparison.Ordinal));
    }
}
