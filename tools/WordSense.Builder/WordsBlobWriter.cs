using System.IO.Compression;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     Writes the compact binary form of the word lexicon (<c>words.bin</c>) that the core package reads.
/// </summary>
/// <remarks>
///     The blob is Brotli-compressed. Its content is the magic number <c>WSW1</c>, a string table (length-prefixed UTF-8),
///     a table of distinct <see cref="WordDetails"/> (so equal senses share one instance) and a table that maps every key
///     to a list of details indexes. Every integer is a 7-bit varint. Keys are sorted ordinally, so equal input always
///     gives equal bytes.
/// </remarks>
internal static class WordsBlobWriter
{
    private static readonly byte[] Magic = "WSW1"u8.ToArray();

    /// <summary>
    ///     Serializes a word lexicon.
    /// </summary>
    /// <param name="entries">The senses of every key; each array holds between 1 and 255 elements.</param>
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
