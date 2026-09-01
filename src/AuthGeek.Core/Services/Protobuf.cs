namespace AuthGeek.Core.Services;

/// <summary>
/// Just enough protocol buffers to read one message shape.
///
/// Google Authenticator's export QR contains a protobuf, and reading it is the difference between
/// somebody moving their accounts across in one go and typing twenty secrets by hand. Pulling in
/// the whole protobuf runtime and a generated class for the sake of four field types would be a
/// large dependency for a small job, so the wire format is read directly: it is a sequence of a
/// varint key, which carries the field number and how the value is encoded, followed by the value.
/// </summary>
internal ref struct Protobuf
{
    private readonly ReadOnlySpan<byte> _data;
    private int _at;

    public Protobuf(ReadOnlySpan<byte> data)
    {
        _data = data;
        _at = 0;
    }

    public bool HasMore => _at < _data.Length;

    /// <summary>Reads the next field's number and wire type.</summary>
    public (int Field, int WireType) ReadKey()
    {
        var key = ReadVarint();
        return ((int)(key >> 3), (int)(key & 0x07));
    }

    public ulong ReadVarint()
    {
        ulong value = 0;
        var shift = 0;

        while (true)
        {
            if (_at >= _data.Length) throw new FormatException("The data ends in the middle of a number.");
            if (shift > 63) throw new FormatException("A number in the data is too long to be valid.");

            var b = _data[_at++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
        }
    }

    /// <summary>A length-prefixed run of bytes: a string, a byte array or a nested message.</summary>
    public ReadOnlySpan<byte> ReadBytes()
    {
        var length = (int)ReadVarint();
        if (length < 0 || _at + length > _data.Length)
            throw new FormatException("The data claims a longer field than it contains.");

        var slice = _data.Slice(_at, length);
        _at += length;
        return slice;
    }

    /// <summary>
    /// Steps over a field we do not care about. Without this, one unexpected field in a future
    /// version of the format would make the whole import fail rather than ignoring what it cannot
    /// use, which is the entire point of the wire format being self-describing.
    /// </summary>
    public void Skip(int wireType)
    {
        switch (wireType)
        {
            case 0: ReadVarint(); break;
            case 1: Advance(8); break;
            case 2: ReadBytes(); break;
            case 5: Advance(4); break;
            default: throw new FormatException($"Wire type {wireType} is not something this reader knows.");
        }
    }

    private void Advance(int n)
    {
        if (_at + n > _data.Length) throw new FormatException("The data ends unexpectedly.");
        _at += n;
    }
}
