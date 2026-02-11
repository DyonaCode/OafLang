namespace Oaf.Frontend.Compiler.Diagnostics;

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info
}

public sealed class Diagnostic
{
    public Diagnostic(
        string code,
        string message,
        int line,
        int column,
        int length,
        DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        Code = code;
        Message = message;
        Line = line;
        Column = column;
        Length = length;
        Severity = severity;
    }

    public string Code { get; }

    public string Message { get; }

    public int Line { get; }

    public int Column { get; }

    public int Length { get; }

    public DiagnosticSeverity Severity { get; }

    public override string ToString()
    {
        return $"{Severity} {Code} ({Line},{Column}): {Message}";
    }
}
