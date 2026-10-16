using System.Buffers.Binary;
using System.Text;

namespace DemaConsulting.WordSense;

/// <summary>
///     Reads a serialized table body with bounds checking on every access. It is shared by the lexicon table and the
///     weights table.
/// </summary>
internal ref struct TableReader
{
    /// <summary>
    ///     The bytes not yet consumed.
    /// </summary>
    private ReadOnlySpan<byte> _remaining;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TableReader"/> struct.
    /// </summary>
    /// <param name="data">The body bytes.</param>
    public TableReader(ReadOnlySpan<byte> data)
    {
        _remaining = data;
    }

    /// <summary>
    ///     Gets a value indicating whether every byte has been consumed.
    /// </summary>
    public readonly bool AtEnd => _remaining.IsEmpty;

    /// <summary>
    ///     Gets the number of bytes not yet consumed.
    /// </summary>
    public readonly int Remaining => _remaining.Length;

    /// <summary>
    ///     Consumes a fixed number of bytes.
    /// </summary>
    /// <param name="count">The number of bytes.</param>
    /// <returns>The consumed bytes.</returns>
    /// <exception cref="WordSenseTableFormatException">Thrown when fewer bytes remain.</exception>
    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (count < 0 || count > _remaining.Length)
        {
            throw new WordSenseTableFormatException("The table is truncated.");
        }

        var result = _remaining[..count];
        _remaining = _remaining[count..];
        return result;
    }

    /// <summary>
    ///     Consumes one byte.
    /// </summary>
    /// <returns>The byte.</returns>
    public byte ReadByte() => ReadBytes(1)[0];

    /// <summary>
    ///     Consumes a little-endian 16-bit unsigned value.
    /// </summary>
    /// <returns>The value.</returns>
    public ushort ReadU16() => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(2));

    /// <summary>
    ///     Consumes a little-endian 32-bit unsigned value.
    /// </summary>
    /// <returns>The value.</returns>
    public uint ReadU32() => BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(4));

    /// <summary>
    ///     Consumes a 32-bit count and checks it against a limit.
    /// </summary>
    /// <param name="max">The largest accepted value.</param>
    /// <param name="min">The smallest accepted value.</param>
    /// <returns>The count.</returns>
    public int ReadCount(int max, int min)
    {
        var value = ReadU32();
        if (value > (uint)max || value < (uint)min)
        {
            throw new WordSenseTableFormatException("A count in the table is out of range.");
        }

        return (int)value;
    }

    /// <summary>
    ///     Consumes a length-prefixed UTF-8 string.
    /// </summary>
    /// <returns>The string.</returns>
    public string ReadString()
    {
        var length = ReadU16();
        try
        {
            return new UTF8Encoding(false, true).GetString(ReadBytes(length));
        }
        catch (ArgumentException ex)
        {
            throw new WordSenseTableFormatException("A string in the table is not valid UTF-8.", ex);
        }
    }

    /// <summary>
    ///     Consumes single-precision values.
    /// </summary>
    /// <param name="count">The number of values.</param>
    /// <returns>The values.</returns>
    public float[] ReadFloats(int count)
    {
        // Check the byte budget before allocating so a huge count cannot cause a huge allocation
        var bytes = ReadBytes(checked(count * 4));
        var values = new float[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(i * 4)..]);
        }

        return values;
    }
}

/// <summary>
///     Writes the primitive values of the table formats, little-endian.
/// </summary>
internal static class TableWriter
{
    /// <summary>
    ///     Writes a 16-bit unsigned value.
    /// </summary>
    /// <param name="stream">The destination.</param>
    /// <param name="value">The value, which must fit in 16 bits.</param>
    public static void WriteU16(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, checked((ushort)value));
        stream.Write(bytes);
    }

    /// <summary>
    ///     Writes a 32-bit unsigned value.
    /// </summary>
    /// <param name="stream">The destination.</param>
    /// <param name="value">The non-negative value.</param>
    public static void WriteU32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, checked((uint)value));
        stream.Write(bytes);
    }

    /// <summary>
    ///     Writes single-precision values.
    /// </summary>
    /// <param name="stream">The destination.</param>
    /// <param name="values">The values.</param>
    public static void WriteFloats(Stream stream, float[] values)
    {
        Span<byte> bytes = stackalloc byte[4];
        foreach (var value in values)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
            stream.Write(bytes);
        }
    }

    /// <summary>
    ///     Writes a length-prefixed UTF-8 string.
    /// </summary>
    /// <param name="stream">The destination.</param>
    /// <param name="value">The string.</param>
    /// <exception cref="InvalidOperationException">Thrown when the encoded string exceeds 65,535 bytes.</exception>
    public static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
        {
            throw new InvalidOperationException("A string is too long for the table format.");
        }

        WriteU16(stream, bytes.Length);
        stream.Write(bytes);
    }
}
