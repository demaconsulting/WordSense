namespace WordSense.Builder;

/// <summary>
///     Writes unsigned integers as 7-bit varints, the integer format of the weights blobs and the basic table.
/// </summary>
internal static class VarintWriter
{
    /// <summary>
    ///     Writes an unsigned integer as a 7-bit varint.
    /// </summary>
    /// <param name="stream">The destination.</param>
    /// <param name="value">The non-negative value.</param>
    internal static void WriteVarint(Stream stream, int value)
    {
        var v = (uint)value;
        while (v >= 0x80)
        {
            stream.WriteByte((byte)(v | 0x80));
            v >>= 7;
        }

        stream.WriteByte((byte)v);
    }
}
