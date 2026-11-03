using System.Text;

namespace ManagedCode.FileContext;

/// <summary>Counts decoded text as UTF-8 even when chunks split a surrogate pair.</summary>
internal struct Utf8ByteCounter
{
    private const int ReplacementBytes = 3;
    private const int SurrogatePairBytes = 4;
    private long _bytes;
    private bool _pendingHighSurrogate;

    public readonly long TotalBytes => _bytes + (_pendingHighSurrogate ? ReplacementBytes : 0);

    public void Append(ReadOnlySpan<char> characters)
    {
        if (characters.IsEmpty)
        {
            return;
        }

        if (_pendingHighSurrogate)
        {
            if (char.IsLowSurrogate(characters[0]))
            {
                _bytes += SurrogatePairBytes;
                characters = characters[1..];
            }
            else
            {
                _bytes += ReplacementBytes;
            }
        }

        _pendingHighSurrogate = !characters.IsEmpty && char.IsHighSurrogate(characters[^1]);
        _bytes += Encoding.UTF8.GetByteCount(_pendingHighSurrogate ? characters[..^1] : characters);
    }
}
