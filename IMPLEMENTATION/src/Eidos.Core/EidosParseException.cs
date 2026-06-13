namespace Eidos.Core;

public sealed class EidosParseException : Exception
{
    public EidosParseException(string message, SourceSpan span)
        : base($"{message} at {span.Start.Line}:{span.Start.Column}.")
    {
        Span = span;
    }

    public SourceSpan Span { get; }
}