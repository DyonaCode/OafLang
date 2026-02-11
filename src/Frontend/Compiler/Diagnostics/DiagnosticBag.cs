using Oaf.Frontend.Compiler.Lexer;

namespace Oaf.Frontend.Compiler.Diagnostics;

public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public void Add(Diagnostic diagnostic)
    {
        _diagnostics.Add(diagnostic);
    }

    public void AddRange(IEnumerable<Diagnostic> diagnostics)
    {
        _diagnostics.AddRange(diagnostics);
    }

    public void Report(
        string code,
        string message,
        int line,
        int column,
        int length,
        DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        _diagnostics.Add(new Diagnostic(code, message, line, column, length, severity));
    }

    public void ReportLexerError(string message, int line, int column, int length)
    {
        Report("LEX001", message, line, column, length);
    }

    public void ReportParserError(string message, Token token)
    {
        Report("PAR001", message, token.Line, token.Column, Math.Max(token.Text.Length, 1));
    }

    public void ReportTypeError(string message, int line, int column, int length)
    {
        Report("TYP001", message, line, column, length);
    }

    public void ReportOwnershipError(string message, int line, int column, int length)
    {
        Report("OWN001", message, line, column, length);
    }
}
