using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace Eidos.Core;

/// <summary>
/// Convenience entry point for parsing Eidos schemas from a <see cref="PipeReader"/>,
/// e.g. when reading directly from a file or network stream.
/// </summary>
public static class EidosSchemaReader
{
    /// <summary>
    /// Reads the complete schema text from <paramref name="reader"/>, decodes it using
    /// <paramref name="encoding"/> (UTF-8 by default), and parses it into a syntax tree.
    /// </summary>
    public static async Task<EidosDocumentSyntax> ParseAsync(
        PipeReader reader,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        encoding ??= Encoding.UTF8;

        var decoder = encoding.GetDecoder();
        CharSegment? first = null;
        CharSegment? last = null;

        while (true)
        {
            var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;

            foreach (var segment in buffer)
            {
                AppendChars(decoder, segment.Span, flush: false, ref first, ref last);
            }

            reader.AdvanceTo(buffer.End);

            if (result.IsCompleted)
            {
                AppendChars(decoder, ReadOnlySpan<byte>.Empty, flush: true, ref first, ref last);
                break;
            }
        }

        await reader.CompleteAsync().ConfigureAwait(false);

        var sequence = first is null || last is null
            ? ReadOnlySequence<char>.Empty
            : new ReadOnlySequence<char>(first, 0, last, last.Memory.Length);

        return EidosGrammarParser.Parse(sequence);
    }

    private static void AppendChars(
        Decoder decoder,
        ReadOnlySpan<byte> bytes,
        bool flush,
        ref CharSegment? first,
        ref CharSegment? last)
    {
        var charCount = decoder.GetCharCount(bytes, flush);
        if (charCount == 0)
        {
            return;
        }

        var chars = new char[charCount];
        decoder.GetChars(bytes, chars, flush);

        var segment = new CharSegment(chars);
        if (last is null)
        {
            first = segment;
        }
        else
        {
            last.Append(segment);
        }

        last = segment;
    }

    private sealed class CharSegment : ReadOnlySequenceSegment<char>
    {
        public CharSegment(ReadOnlyMemory<char> memory)
        {
            Memory = memory;
        }

        public void Append(CharSegment next)
        {
            next.RunningIndex = RunningIndex + Memory.Length;
            Next = next;
        }
    }
}
