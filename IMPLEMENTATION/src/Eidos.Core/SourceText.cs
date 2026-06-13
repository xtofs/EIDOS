namespace Eidos.Core;

public readonly record struct SourcePosition(long Offset, int Line, int Column)
{
    public override string ToString()
    {
        return $"{Line}:{Column}";
    }
}

public readonly record struct SourceSpan(SourcePosition Start, SourcePosition End)
{
    public static SourceSpan From(SourcePosition start, SourcePosition end)
    {
        return new SourceSpan(start, end);
    }

    public override string ToString()
    {
        return $"{Start}-{End}";
    }
}