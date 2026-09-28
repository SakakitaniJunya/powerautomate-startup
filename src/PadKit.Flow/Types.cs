namespace PadKit.Flow;

public enum Severity
{
    Warning,
    Error,
}

/// <summary>レンダリング時の診断 (行番号は 1 始まり、0 は行特定不能)。</summary>
public sealed record Diagnostic(string Code, int Line, Severity Severity, string Message);

public sealed record RenderResult(string Text, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>error 重大度の診断が 1 件以上あるときに Renderer が投げる例外。</summary>
public sealed class RenderException : Exception
{
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public RenderException(IReadOnlyList<Diagnostic> diagnostics)
        : base(string.Join(Environment.NewLine,
            diagnostics.Where(d => d.Severity == Severity.Error)
                .Select(d => $"{d.Code} (行 {d.Line}): {d.Message}")))
    {
        Diagnostics = diagnostics;
    }
}

/// <summary>lint 1 件分の指摘。</summary>
public sealed record Finding(string RuleId, int Line, Severity Severity, string Message, string? Fix);
