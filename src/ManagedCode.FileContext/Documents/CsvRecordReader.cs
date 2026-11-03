using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace ManagedCode.FileContext;

/// <summary>Scans logical CSV records and materializes field values only for a requested header.</summary>
internal sealed class CsvRecordReader(TextReader reader, char delimiter, CancellationToken token)
{
    private const char Quote = '"';
    private const int EndOfStream = -1;
    private readonly char[] _buffer = new char[4_096];
    private int _position;
    private int _length;
    private long _line = 1;
    private bool _previousCarriageReturn;

    public bool TryRead(bool captureFields, out Record record)
    {
        var fields = captureFields ? new List<string>() : null;
        var field = captureFields ? new StringBuilder() : null;
        while (Peek() != EndOfStream)
        {
            token.ThrowIfCancellationRequested();
            fields?.Clear();
            record = ReadRecord(fields, field, out var blank);
            if (!blank) { return true; }
        }

        record = default;
        return false;
    }

    private Record ReadRecord(List<string>? fields, StringBuilder? field, out bool blank)
    {
        var startLine = _line;
        var count = 0;
        var hasValue = false;
        var meaningful = false;
        while (true)
        {
            field?.Clear();
            hasValue |= ReadField(field, startLine, out var hasSyntax);
            meaningful |= hasSyntax;
            count++;
            fields?.Add(field!.ToString());

            if (Peek() != delimiter) { break; }
            meaningful = true;
            _position++;
        }

        ConsumeRecordEnding();
        blank = !meaningful;
        return new Record(count, hasValue, fields);
    }

    private bool ReadField(StringBuilder? field, long startLine, out bool meaningful)
    {
        var hasPadding = false;
        meaningful = false;
        while (Peek() is var next && IsPadding(next))
        {
            _position++;
            hasPadding = true;
            meaningful |= !char.IsWhiteSpace((char)next);
            field?.Append((char)next);
        }

        if (Peek() == Quote)
        {
            _position++;
            field?.Clear();
            meaningful = true;
            return ReadQuotedField(field, startLine);
        }

        var hasText = ReadUnquotedField(field);
        meaningful |= hasText;
        return hasPadding || hasText;
    }

    private bool ReadUnquotedField(StringBuilder? field)
    {
        var hasValue = false;
        while (EnsureBuffered())
        {
            var remaining = _buffer.AsSpan(_position, _length - _position);
            var ending = remaining.IndexOfAny(delimiter, '\r', '\n');
            var value = ending < 0 ? remaining : remaining[..ending];
            field?.Append(value);
            hasValue |= !value.IsEmpty;
            _position += value.Length;
            if (ending >= 0) { break; }
        }
        return hasValue;
    }

    private bool ReadQuotedField(StringBuilder? field, long startLine)
    {
        var hasValue = false;
        while (EnsureBuffered())
        {
            var remaining = _buffer.AsSpan(_position, _length - _position);
            var quote = remaining.IndexOf(Quote);
            var value = quote < 0 ? remaining : remaining[..quote];
            CountLineEndings(value);
            field?.Append(value);
            hasValue |= !value.IsEmpty;
            _position += value.Length;
            if (quote < 0) { continue; }

            _position++;
            _previousCarriageReturn = false;
            if (Peek() != Quote)
            {
                while (IsPadding(Peek())) { _position++; }
                if (Peek() is var next && next != delimiter && next is not (EndOfStream or '\r' or '\n'))
                { throw Malformed(startLine); }
                return hasValue;
            }

            _position++;
            field?.Append(Quote);
            hasValue = true;
        }

        throw Malformed(startLine);
    }

    private void CountLineEndings(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) { return; }

        var firstEnding = value.IndexOfAny('\r', '\n');
        if (firstEnding != 0) { _previousCarriageReturn = false; }
        if (firstEnding < 0) { return; }

        foreach (var character in value[firstEnding..])
        {
            if (character == '\r' || (character == '\n' && !_previousCarriageReturn)) { _line++; }
            _previousCarriageReturn = character == '\r';
        }
    }

    private void ConsumeRecordEnding()
    {
        var ending = Peek();
        if (ending == EndOfStream) { return; }
        _position++;
        if (ending == '\r' && Peek() == '\n') { _position++; }
        _line++;
    }

    private bool IsPadding(int character) => character >= 0 && character != delimiter && character is not ('\r' or '\n') &&
        (char.IsWhiteSpace((char)character) || character is '\u200b' or '\ufeff');

    private int Peek() => EnsureBuffered() ? _buffer[_position] : EndOfStream;

    private bool EnsureBuffered()
    {
        if (_position < _length) { return true; }
        token.ThrowIfCancellationRequested();
        _length = reader.Read(_buffer.AsSpan());
        _position = 0;
        return _length > 0;
    }

    private static MalformedLineException Malformed(long line) => new($"CSV record starting on line {line} is malformed.", line);

    public readonly record struct Record(int ColumnCount, bool HasValue, IReadOnlyList<string>? Fields);
}
