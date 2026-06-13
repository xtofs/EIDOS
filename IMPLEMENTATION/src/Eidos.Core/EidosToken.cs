namespace Eidos.Core;

public enum EidosTokenKind
{
    Identifier,
    IntegerLiteral,
    StringLiteral,
    DocComment,

    At,
    Colon,
    Comma,
    Dot,
    Plus,
    Pipe,
    Bang,
    Arrow,
    AndAnd,
    OrOr,

    LBrace,
    RBrace,
    LBracket,
    RBracket,
    LParen,
    RParen,
    Less,
    Greater,

    Whitespace,
    LineComment,
    BlockComment,
    EndOfFile
}

public readonly record struct EidosToken(EidosTokenKind Kind, string Lexeme, SourceSpan Span);