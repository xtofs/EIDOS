using System.Buffers;

namespace Eidos.Core;

public ref struct EidosScanner
{
    private SequenceReader<char> _reader;
    private long _offset;
    private int _line;
    private int _column;

    public EidosScanner(ReadOnlySequence<char> input)
    {
        _reader = new SequenceReader<char>(input);
        _offset = 0;
        _line = 1;
        _column = 1;
    }

    public List<EidosToken> ScanTokens()
    {
        var tokens = new List<EidosToken>();

        while (!_reader.End)
        {
            var start = CurrentPosition();
            var ch = PeekRequired();

            if (char.IsWhiteSpace(ch))
            {
                var lexeme = ReadWhile(static c => char.IsWhiteSpace(c));
                tokens.Add(new EidosToken(EidosTokenKind.Whitespace, lexeme, SourceSpan.From(start, CurrentPosition())));
                continue;
            }

            if (ch == '/' && Peek(1) == '/')
            {
                var lexeme = ReadLineComment();
                tokens.Add(new EidosToken(EidosTokenKind.LineComment, lexeme, SourceSpan.From(start, CurrentPosition())));
                continue;
            }

            if (ch == '/' && Peek(1) == '*')
            {
                var lexeme = ReadBlockComment();
                tokens.Add(new EidosToken(EidosTokenKind.BlockComment, lexeme, SourceSpan.From(start, CurrentPosition())));
                continue;
            }

            if (ch == '"' && Peek(1) == '"' && Peek(2) == '"')
            {
                var lexeme = ReadDocComment();
                tokens.Add(new EidosToken(EidosTokenKind.DocComment, lexeme, SourceSpan.From(start, CurrentPosition())));
                continue;
            }

            if (ch == '"')
            {
                var lexeme = ReadStringLiteral();
                tokens.Add(new EidosToken(EidosTokenKind.StringLiteral, lexeme, SourceSpan.From(start, CurrentPosition())));
                continue;
            }

            if (char.IsAsciiDigit(ch))
            {
                var lexeme = ReadWhile(static c => char.IsAsciiDigit(c));
                tokens.Add(new EidosToken(EidosTokenKind.IntegerLiteral, lexeme, SourceSpan.From(start, CurrentPosition())));
                continue;
            }

            if (IsIdentifierStart(ch))
            {
                var lexeme = ReadIdentifier();
                tokens.Add(new EidosToken(EidosTokenKind.Identifier, lexeme, SourceSpan.From(start, CurrentPosition())));
                continue;
            }

            if (ch == '-' && Peek(1) == '>')
            {
                ReadChar();
                ReadChar();
                tokens.Add(new EidosToken(EidosTokenKind.Arrow, "->", SourceSpan.From(start, CurrentPosition())));
                continue;
            }

            if (ch == '&' && Peek(1) == '&')
            {
                ReadChar();
                ReadChar();
                tokens.Add(new EidosToken(EidosTokenKind.AndAnd, "&&", SourceSpan.From(start, CurrentPosition())));
                continue;
            }

            if (ch == '|' && Peek(1) == '|')
            {
                ReadChar();
                ReadChar();
                tokens.Add(new EidosToken(EidosTokenKind.OrOr, "||", SourceSpan.From(start, CurrentPosition())));
                continue;
            }

            var kind = ch switch
            {
                '@' => EidosTokenKind.At,
                ':' => EidosTokenKind.Colon,
                ',' => EidosTokenKind.Comma,
                '.' => EidosTokenKind.Dot,
                '+' => EidosTokenKind.Plus,
                '|' => EidosTokenKind.Pipe,
                '!' => EidosTokenKind.Bang,
                '{' => EidosTokenKind.LBrace,
                '}' => EidosTokenKind.RBrace,
                '[' => EidosTokenKind.LBracket,
                ']' => EidosTokenKind.RBracket,
                '(' => EidosTokenKind.LParen,
                ')' => EidosTokenKind.RParen,
                '<' => EidosTokenKind.Less,
                '>' => EidosTokenKind.Greater,
                _ => (EidosTokenKind?)null
            };

            if (kind is not null)
            {
                ReadChar();
                tokens.Add(new EidosToken(kind.Value, ch.ToString(), SourceSpan.From(start, CurrentPosition())));
                continue;
            }

            throw new EidosParseException($"Unexpected character '{ch}'", SourceSpan.From(start, start));
        }

        var eof = CurrentPosition();
        tokens.Add(new EidosToken(EidosTokenKind.EndOfFile, string.Empty, SourceSpan.From(eof, eof)));
        return tokens;
    }

    private string ReadIdentifier()
    {
        return ReadWhile(static c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-');
    }

    private string ReadLineComment()
    {
        ReadChar();
        ReadChar();
        var body = ReadWhile(static c => c != '\r' && c != '\n');
        return $"//{body}";
    }

    private string ReadBlockComment()
    {
        ReadChar();
        ReadChar();

        var chars = new List<char>();
        while (true)
        {
            if (_reader.End)
            {
                throw new EidosParseException("Unterminated block comment", SourceSpan.From(CurrentPosition(), CurrentPosition()));
            }

            if (TryPeek(out var ch) && ch == '*' && Peek(1) == '/')
            {
                ReadChar();
                ReadChar();
                break;
            }

            chars.Add(ReadChar());
        }

        var body = new string([.. chars]);
        return $"/*{body}*/";
    }

    private string ReadDocComment()
    {
        ReadChar();
        ReadChar();
        ReadChar();

        var chars = new List<char>();
        while (true)
        {
            if (_reader.End)
            {
                throw new EidosParseException("Unterminated doc comment", SourceSpan.From(CurrentPosition(), CurrentPosition()));
            }

            if (TryPeek(out var ch) && ch == '"' && Peek(1) == '"' && Peek(2) == '"')
            {
                ReadChar();
                ReadChar();
                ReadChar();
                break;
            }

            chars.Add(ReadChar());
        }

        var body = new string([.. chars]);
        return $"\"\"\"{body}\"\"\"";
    }

    private string ReadStringLiteral()
    {
        ReadChar();

        var chars = new List<char>();
        while (true)
        {
            if (_reader.End)
            {
                throw new EidosParseException("Unterminated string literal", SourceSpan.From(CurrentPosition(), CurrentPosition()));
            }

            var ch = ReadChar();
            if (ch == '"')
            {
                break;
            }

            if (ch == '\\')
            {
                if (_reader.End)
                {
                    throw new EidosParseException("Unterminated escape sequence", SourceSpan.From(CurrentPosition(), CurrentPosition()));
                }

                var escaped = ReadChar();
                chars.Add('\\');
                chars.Add(escaped);
                continue;
            }

            chars.Add(ch);
        }

        var body = new string([.. chars]);
        return $"\"{body}\"";
    }

    private string ReadWhile(Func<char, bool> predicate)
    {
        var chars = new List<char>();
        while (TryPeek(out var ch) && predicate(ch))
        {
            chars.Add(ReadChar());
        }

        return new string([.. chars]);
    }

    private static bool IsIdentifierStart(char ch)
    {
        return char.IsAsciiLetter(ch) || ch == '_';
    }

    private char Peek(int offset)
    {
        var sequence = _reader.UnreadSequence;
        var index = 0;
        foreach (var segment in sequence)
        {
            var span = segment.Span;
            for (var i = 0; i < span.Length; i++)
            {
                if (index == offset)
                {
                    return span[i];
                }

                index++;
            }
        }

        return '\0';
    }

    private bool TryPeek(out char ch)
    {
        return _reader.TryPeek(out ch);
    }

    private char PeekRequired()
    {
        if (!_reader.TryPeek(out var ch))
        {
            throw new InvalidOperationException("No more characters available.");
        }

        return ch;
    }

    private char ReadChar()
    {
        if (!_reader.TryRead(out var ch))
        {
            throw new InvalidOperationException("Unexpected end of sequence.");
        }

        _offset++;
        if (ch == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }

        return ch;
    }

    private SourcePosition CurrentPosition()
    {
        return new SourcePosition(_offset, _line, _column);
    }
}