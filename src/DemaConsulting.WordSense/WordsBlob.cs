using System.Collections.Frozen;
using System.IO.Compression;
using System.Text;

namespace DemaConsulting.WordSense;

/// <summary>
///     Reads the compact binary form of the word lexicon behind <see cref="WordLexicon"/>.
/// </summary>
/// <remarks>
///     The blob is Brotli-compressed. Its content is a magic number (<c>WSW1</c>), a string table (length-prefixed
///     UTF-8), a table of distinct <see cref="WordDetails"/> (so equal senses share one instance) and a table that maps
///     every key to a list of details indexes. Every integer is a 7-bit varint. The blob is written by the repository's
///     build tool, never at run time.
/// </remarks>
internal static class WordsBlob
{
    private static readonly byte[] Magic = "WSW1"u8.ToArray();

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    ///     Deserializes a word lexicon.
    /// </summary>
    /// <param name="blob">The compressed blob.</param>
    /// <returns>The lexicon, with ordinal key comparison.</returns>
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
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            throw new InvalidDataException("The word lexicon is corrupt.", ex);
        }

        var span = data.AsSpan();
        if (span.Length < Magic.Length || !span[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("The word lexicon has an unknown format.");
        }

        var position = Magic.Length;
        var stringCount = ReadCount(span, ref position, 1);
        var strings = new string[stringCount];
        for (var i = 0; i < stringCount; i++)
        {
            var length = ReadVarint(span, ref position);
            Require(span, position, length);
            try
            {
                strings[i] = StrictUtf8.GetString(span.Slice(position, length));
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException("The word lexicon has an invalid text.", ex);
            }

            position += length;
        }

        var detailsCount = ReadCount(span, ref position, 4);
        var details = new WordDetails[detailsCount];
        for (var i = 0; i < detailsCount; i++)
        {
            var lemma = ReadVarint(span, ref position);
            Require(span, position, 1);
            var pos = span[position++];
            var meaning = ReadVarint(span, ref position);
            var respelling = ReadVarint(span, ref position);
            if (lemma >= stringCount || pos > (byte)PartOfSpeech.Determiner || meaning > stringCount || respelling > stringCount)
            {
                throw new InvalidDataException("The word lexicon has an invalid sense.");
            }

            details[i] = new WordDetails(
                strings[lemma],
                (PartOfSpeech)pos,
                meaning == 0 ? null : strings[meaning - 1],
                respelling == 0 ? null : strings[respelling - 1]);
        }

        var entryCount = ReadCount(span, ref position, 3);
        var entries = new Dictionary<string, WordDetails[]>(entryCount, StringComparer.Ordinal);
        for (var i = 0; i < entryCount; i++)
        {
            var key = ReadVarint(span, ref position);
            var count = ReadCount(span, ref position, 1);
            if (key >= stringCount || count == 0)
            {
                throw new InvalidDataException("The word lexicon has an invalid entry.");
            }

            var senses = new WordDetails[count];
            for (var s = 0; s < count; s++)
            {
                var id = ReadVarint(span, ref position);
                senses[s] = id < detailsCount ? details[id] : throw new InvalidDataException("The word lexicon has an invalid sense reference.");
            }

            if (!entries.TryAdd(strings[key], senses))
            {
                throw new InvalidDataException("The word lexicon has a duplicate key.");
            }
        }

        if (position != span.Length)
        {
            throw new InvalidDataException("The word lexicon has trailing data.");
        }

        return entries.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    ///     Reads an unsigned 7-bit varint.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="position">The read position, advanced past the value.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidDataException">Thrown when the data ends early or the value is too large.</exception>
    private static int ReadVarint(ReadOnlySpan<byte> span, ref int position)
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
            if (shift == 28 && (b & 0x7F) > 0x0F)
            {
                throw new InvalidDataException("A number is too large.");
            }

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
        if (length < 0 || length > span.Length - position)
        {
            throw new InvalidDataException("The data is truncated.");
        }
    }

    /// <summary>
    ///     Reads an item count and rejects a count that cannot fit in the remaining data, before anything is allocated.
    /// </summary>
    /// <param name="span">The data.</param>
    /// <param name="position">The read position, advanced past the count.</param>
    /// <param name="minimumItemBytes">The least number of bytes one item occupies.</param>
    /// <returns>The count.</returns>
    /// <exception cref="InvalidDataException">Thrown when the count exceeds what the remaining data can hold.</exception>
    private static int ReadCount(ReadOnlySpan<byte> span, ref int position, int minimumItemBytes)
    {
        var count = ReadVarint(span, ref position);
        if (count > (span.Length - position) / minimumItemBytes)
        {
            throw new InvalidDataException("The data is truncated.");
        }

        return count;
    }
}
