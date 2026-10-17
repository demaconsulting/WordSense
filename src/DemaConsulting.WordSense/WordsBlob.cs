using System.Collections.Frozen;
using System.IO.Compression;
using System.Text;

namespace DemaConsulting.WordSense;

/// <summary>
///     Reads and writes the compact binary form of the word table behind <see cref="Words"/>.
/// </summary>
/// <remarks>
///     The blob is Brotli-compressed. Its content is a magic number, a string table (length-prefixed UTF-8), a table of
///     distinct <see cref="WordDetails"/> (so equal senses share one instance) and a table that maps every key to a list of
///     details indexes. Every integer is a 7-bit varint. The writer sorts the keys ordinally, so equal input always
///     gives equal bytes.
/// </remarks>
internal static class WordsBlob
{
    private static readonly byte[] Magic = "WSW1"u8.ToArray();

    /// <summary>
    ///     Serializes a word table.
    /// </summary>
    /// <param name="entries">The senses of every key; each array holds at least one element.</param>
    /// <returns>The compressed blob.</returns>
    public static byte[] Write(IReadOnlyDictionary<string, WordDetails[]> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // Intern strings and details in a deterministic order
        var strings = new List<string>();
        var stringIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        int Intern(string value)
        {
            if (!stringIndex.TryGetValue(value, out var index))
            {
                index = strings.Count;
                strings.Add(value);
                stringIndex[value] = index;
            }

            return index;
        }

        var details = new List<WordDetails>();
        var detailsIndex = new Dictionary<WordDetails, int>();
        var keys = entries.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var lists = new List<(int Key, int[] Details)>(keys.Count);
        foreach (var key in keys)
        {
            var senses = entries[key];
            if (senses.Length is 0 or > 255)
            {
                throw new ArgumentException($"The key '{key}' needs between 1 and 255 senses.", nameof(entries));
            }

            var keyId = Intern(key);
            var list = new int[senses.Length];
            for (var i = 0; i < senses.Length; i++)
            {
                if (!detailsIndex.TryGetValue(senses[i], out var id))
                {
                    id = details.Count;
                    details.Add(senses[i]);
                    detailsIndex[senses[i]] = id;
                    Intern(senses[i].Lemma);
                    if (senses[i].Meaning is { } meaning)
                    {
                        Intern(meaning);
                    }

                    if (senses[i].Respelling is { } respelling)
                    {
                        Intern(respelling);
                    }
                }

                list[i] = id;
            }

            lists.Add((keyId, list));
        }

        using var raw = new MemoryStream();
        raw.Write(Magic);
        WriteVarint(raw, strings.Count);
        foreach (var value in strings)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            WriteVarint(raw, bytes.Length);
            raw.Write(bytes);
        }

        WriteVarint(raw, details.Count);
        foreach (var d in details)
        {
            WriteVarint(raw, stringIndex[d.Lemma]);
            raw.WriteByte((byte)d.PartOfSpeech);
            WriteVarint(raw, d.Meaning is null ? 0 : stringIndex[d.Meaning] + 1);
            WriteVarint(raw, d.Respelling is null ? 0 : stringIndex[d.Respelling] + 1);
        }

        WriteVarint(raw, lists.Count);
        foreach (var (key, list) in lists)
        {
            WriteVarint(raw, key);
            WriteVarint(raw, list.Length);
            foreach (var id in list)
            {
                WriteVarint(raw, id);
            }
        }

        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            brotli.Write(raw.GetBuffer(), 0, (int)raw.Length);
        }

        return output.ToArray();
    }

    /// <summary>
    ///     Deserializes a word table.
    /// </summary>
    /// <param name="blob">The compressed blob.</param>
    /// <returns>The table, with ordinal key comparison.</returns>
    /// <exception cref="InvalidDataException">Thrown when the blob is corrupt.</exception>
    public static FrozenDictionary<string, WordDetails[]> Read(ReadOnlySpan<byte> blob)
    {
        byte[] data;
        try
        {
            using var input = new MemoryStream(blob.ToArray());
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var raw = new MemoryStream();
            brotli.CopyTo(raw);
            data = raw.ToArray();
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            throw new InvalidDataException("The word table is corrupt.", ex);
        }

        var span = data.AsSpan();
        if (span.Length < Magic.Length || !span[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("The word table has an unknown format.");
        }

        var position = Magic.Length;
        var stringCount = ReadVarint(span, ref position);
        var strings = new string[stringCount];
        for (var i = 0; i < stringCount; i++)
        {
            var length = ReadVarint(span, ref position);
            Require(span, position, length);
            strings[i] = Encoding.UTF8.GetString(span.Slice(position, length));
            position += length;
        }

        var detailsCount = ReadVarint(span, ref position);
        var details = new WordDetails[detailsCount];
        for (var i = 0; i < detailsCount; i++)
        {
            var lemma = ReadVarint(span, ref position);
            Require(span, position, 1);
            var pos = span[position++];
            var meaning = ReadVarint(span, ref position);
            var respelling = ReadVarint(span, ref position);
            if (lemma >= stringCount || pos > (byte)PartOfSpeech.Interjection || meaning > stringCount || respelling > stringCount)
            {
                throw new InvalidDataException("The word table has an invalid sense.");
            }

            details[i] = new WordDetails(
                strings[lemma],
                (PartOfSpeech)pos,
                meaning == 0 ? null : strings[meaning - 1],
                respelling == 0 ? null : strings[respelling - 1]);
        }

        var entryCount = ReadVarint(span, ref position);
        var entries = new Dictionary<string, WordDetails[]>(entryCount, StringComparer.Ordinal);
        for (var i = 0; i < entryCount; i++)
        {
            var key = ReadVarint(span, ref position);
            var count = ReadVarint(span, ref position);
            if (key >= stringCount || count == 0)
            {
                throw new InvalidDataException("The word table has an invalid entry.");
            }

            var senses = new WordDetails[count];
            for (var s = 0; s < count; s++)
            {
                var id = ReadVarint(span, ref position);
                senses[s] = id < detailsCount ? details[id] : throw new InvalidDataException("The word table has an invalid sense reference.");
            }

            entries[strings[key]] = senses;
        }

        if (position != span.Length)
        {
            throw new InvalidDataException("The word table has trailing data.");
        }

        return entries.ToFrozenDictionary(StringComparer.Ordinal);
    }

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

    /// <summary>
    ///     Reads an unsigned 7-bit varint.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="position">The read position, advanced past the value.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidDataException">Thrown when the data ends early or the value is too large.</exception>
    internal static int ReadVarint(ReadOnlySpan<byte> span, ref int position)
    {
        uint result = 0;
        var shift = 0;
        while (true)
        {
            if (position >= span.Length || shift > 28)
            {
                throw new InvalidDataException("The data is truncated or malformed.");
            }

            var b = span[position++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                break;
            }

            shift += 7;
        }

        return result > int.MaxValue ? throw new InvalidDataException("A number is too large.") : (int)result;
    }

    /// <summary>
    ///     Checks that a number of bytes is available.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="position">The position.</param>
    /// <param name="length">The needed length.</param>
    private static void Require(ReadOnlySpan<byte> span, int position, int length)
    {
        if (length < 0 || position + length > span.Length)
        {
            throw new InvalidDataException("The data is truncated.");
        }
    }
}
