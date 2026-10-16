using System.IO.Compression;
using System.Text;

namespace DemaConsulting.WordSense;

/// <summary>
///     The weight-less dictionary words of a table: a sorted set of lower-case <c>a-z</c> words, each with a mask of
///     the parts of speech it can have, kept as one front-coded byte array that is searched in place.
/// </summary>
/// <remarks>
///     <para>
///     Record layout: u8 prefix length (shared with the previous word; 0 at the start of every block of
///     <see cref="BlockSize"/> words), u8 suffix length (at least 1), the suffix bytes, u8 part-of-speech mask (bit
///     <c>i</c> is <see cref="PartOfSpeech"/> value <c>i</c>; never 0). Words are strictly ascending in ordinal
///     order, so the encoding of a word set is unique. Nothing is expanded into per-word objects: a lookup binary
///     searches the block head words, then decodes at most <see cref="BlockSize"/> records without allocating.
///     </para>
///     <para>
///     Instances are immutable and thread-safe.
///     </para>
/// </remarks>
internal sealed class LexicalIndex
{
    /// <summary>
    ///     The number of words per block.
    /// </summary>
    internal const int BlockSize = 32;

    /// <summary>
    ///     The largest word count accepted.
    /// </summary>
    internal const int MaxWords = 400_000;

    /// <summary>
    ///     The largest decoded size in bytes accepted.
    /// </summary>
    internal const int MaxRawLength = 4 * 1024 * 1024;

    /// <summary>
    ///     The largest ratio DEFLATE can achieve, so a declared decoded size beyond it cannot be genuine.
    /// </summary>
    private const int MaxDeflateRatio = 1032;

    /// <summary>
    ///     The longest word accepted, in characters.
    /// </summary>
    internal const int MaxWordLength = 255;

    /// <summary>
    ///     The mask that has a bit for every defined part of speech.
    /// </summary>
    private const int AllPartsMask = 0xFF;

    /// <summary>
    ///     The front-coded records.
    /// </summary>
    private readonly byte[] _raw;

    /// <summary>
    ///     The offset of the first record of every block.
    /// </summary>
    private readonly int[] _blocks;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LexicalIndex"/> class.
    /// </summary>
    /// <param name="raw">The records.</param>
    /// <param name="blocks">The block offsets.</param>
    /// <param name="count">The word count.</param>
    private LexicalIndex(byte[] raw, int[] blocks, int count)
    {
        _raw = raw;
        _blocks = blocks;
        Count = count;
    }

    /// <summary>
    ///     Gets an index without words.
    /// </summary>
    public static LexicalIndex Empty { get; } = new([], [], 0);

    /// <summary>
    ///     Gets the number of words.
    /// </summary>
    public int Count { get; }

    /// <summary>
    ///     Gets the size of the front-coded records in bytes.
    /// </summary>
    public int RawLength => _raw.Length;

    /// <summary>
    ///     Encodes a word set.
    /// </summary>
    /// <param name="words">The words in strictly ascending ordinal order, each with its non-zero part-of-speech mask.</param>
    /// <returns>The index.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when a word is not 1 to 255 lower-case letters <c>a-z</c>, the words are not strictly ascending, a mask
    ///     is 0 or has no defined bits, or there are too many words.
    /// </exception>
    public static LexicalIndex Build(IReadOnlyList<(string Word, byte Mask)> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (words.Count > MaxWords)
        {
            throw new ArgumentException($"A table holds at most {MaxWords} dictionary words.", nameof(words));
        }

        var raw = new List<byte>(words.Count * 6);
        var blocks = new List<int>((words.Count / BlockSize) + 1);
        var previous = string.Empty;
        for (var i = 0; i < words.Count; i++)
        {
            var (word, mask) = words[i];
            if (!IsValidWord(word) || mask == 0 || (mask & ~AllPartsMask) != 0)
            {
                throw new ArgumentException($"The dictionary word '{word}' is invalid.", nameof(words));
            }

            if (i > 0 && string.CompareOrdinal(previous, word) >= 0)
            {
                throw new ArgumentException("Dictionary words must be unique and in ordinal order.", nameof(words));
            }

            // A block always starts with a full word
            var prefix = 0;
            if (i % BlockSize == 0)
            {
                blocks.Add(raw.Count);
            }
            else
            {
                while (prefix < previous.Length && prefix < word.Length && previous[prefix] == word[prefix])
                {
                    prefix++;
                }
            }

            raw.Add((byte)prefix);
            raw.Add((byte)(word.Length - prefix));
            for (var c = prefix; c < word.Length; c++)
            {
                raw.Add((byte)word[c]);
            }

            raw.Add(mask);
            previous = word;
        }

        if (raw.Count > MaxRawLength)
        {
            throw new ArgumentException("The dictionary words are too large for the table format.", nameof(words));
        }

        return new LexicalIndex(raw.ToArray(), blocks.ToArray(), words.Count);
    }

    /// <summary>
    ///     Inflates and strictly validates a serialized index.
    /// </summary>
    /// <param name="count">The declared word count.</param>
    /// <param name="rawLength">The declared decoded size.</param>
    /// <param name="deflated">The raw-DEFLATE bytes; empty exactly when there are no words.</param>
    /// <returns>The index.</returns>
    /// <exception cref="WordSenseTableFormatException">
    ///     Thrown when a count is out of range, the data does not inflate to exactly the declared size, or the records
    ///     violate the format.
    /// </exception>
    public static LexicalIndex Decompress(int count, int rawLength, ReadOnlySpan<byte> deflated)
    {
        if (count < 0 || count > MaxWords || rawLength < 0 || rawLength > MaxRawLength)
        {
            throw new WordSenseTableFormatException("A count in the dictionary section is out of range.");
        }

        if (count == 0)
        {
            return rawLength == 0 && deflated.IsEmpty
                ? Empty
                : throw new WordSenseTableFormatException("An empty dictionary section has data.");
        }

        if (rawLength < count * 4 || deflated.IsEmpty || rawLength > (long)deflated.Length * MaxDeflateRatio)
        {
            throw new WordSenseTableFormatException("The dictionary section sizes are inconsistent.");
        }

        // Inflate into exactly the declared size: more or less data is a format error, which bounds a zip bomb
        var raw = new byte[rawLength];
        try
        {
            using var stream = new DeflateStream(new MemoryStream(deflated.ToArray()), CompressionMode.Decompress);
            stream.ReadExactly(raw);
            if (stream.ReadByte() >= 0)
            {
                throw new WordSenseTableFormatException("The dictionary section is longer than declared.");
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            throw new WordSenseTableFormatException("The dictionary section is corrupted.", ex);
        }

        return Parse(raw, count);
    }

    /// <summary>
    ///     Compresses the records.
    /// </summary>
    /// <returns>The raw-DEFLATE bytes; empty when there are no words.</returns>
    public byte[] Compress()
    {
        if (Count == 0)
        {
            return [];
        }

        using var output = new MemoryStream();
        using (var stream = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            stream.Write(_raw);
        }

        return output.ToArray();
    }

    /// <summary>
    ///     Looks a word up.
    /// </summary>
    /// <param name="word">The lower-case word; anything but 1 to 255 letters <c>a-z</c> is simply not found.</param>
    /// <param name="mask">Receives the part-of-speech mask (bit <c>i</c> is <see cref="PartOfSpeech"/> value <c>i</c>).</param>
    /// <returns>True when the word is present.</returns>
    public bool TryFind(ReadOnlySpan<char> word, out byte mask)
    {
        mask = 0;
        if (Count == 0 || word.Length == 0 || word.Length > MaxWordLength || !IsLowerAscii(word))
        {
            return false;
        }

        // Find the last block whose head word is not after the word
        var low = 0;
        var high = _blocks.Length - 1;
        var block = -1;
        while (low <= high)
        {
            var mid = (low + high) >>> 1;
            var offset = _blocks[mid];
            var head = _raw.AsSpan(offset + 2, _raw[offset + 1]);
            if (Compare(word, head) >= 0)
            {
                block = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (block < 0)
        {
            return false;
        }

        // Decode the block's records in sequence until the word is found or passed
        Span<byte> buffer = stackalloc byte[MaxWordLength];
        var position = _blocks[block];
        var index = block * BlockSize;
        var end = Math.Min(Count, index + BlockSize);
        for (; index < end; index++)
        {
            int prefix = _raw[position];
            int suffix = _raw[position + 1];
            _raw.AsSpan(position + 2, suffix).CopyTo(buffer[prefix..]);
            var order = Compare(word, buffer[..(prefix + suffix)]);
            if (order == 0)
            {
                mask = _raw[position + 2 + suffix];
                return true;
            }

            if (order < 0)
            {
                return false;
            }

            position += suffix + 3;
        }

        return false;
    }

    /// <summary>
    ///     Enumerates the words in ordinal order, decoding each on demand.
    /// </summary>
    /// <returns>The words with their part-of-speech masks.</returns>
    public IEnumerable<(string Word, byte Mask)> Enumerate()
    {
        var buffer = new byte[MaxWordLength];
        var position = 0;
        for (var index = 0; index < Count; index++)
        {
            int prefix = _raw[position];
            int suffix = _raw[position + 1];
            Array.Copy(_raw, position + 2, buffer, prefix, suffix);
            yield return (Encoding.ASCII.GetString(buffer, 0, prefix + suffix), _raw[position + 2 + suffix]);
            position += suffix + 3;
        }
    }

    /// <summary>
    ///     Strictly validates decoded records.
    /// </summary>
    /// <param name="raw">The records.</param>
    /// <param name="count">The declared word count.</param>
    /// <returns>The index.</returns>
    /// <exception cref="WordSenseTableFormatException">Thrown when a rule of the format is violated.</exception>
    private static LexicalIndex Parse(byte[] raw, int count)
    {
        var blocks = new List<int>((count / BlockSize) + 1);
        var previous = new byte[MaxWordLength];
        var current = new byte[MaxWordLength];
        var previousLength = 0;
        var position = 0;
        for (var i = 0; i < count; i++)
        {
            if (position + 2 > raw.Length)
            {
                throw new WordSenseTableFormatException("The dictionary section is truncated.");
            }

            int prefix = raw[position];
            int suffix = raw[position + 1];
            var isHead = i % BlockSize == 0;
            if (isHead)
            {
                blocks.Add(position);
            }

            if ((isHead && prefix != 0) || prefix > previousLength || suffix < 1 || prefix + suffix > MaxWordLength ||
                position + 3 + suffix > raw.Length)
            {
                throw new WordSenseTableFormatException("A dictionary record is malformed.");
            }

            Array.Copy(previous, current, prefix);
            for (var c = 0; c < suffix; c++)
            {
                var letter = raw[position + 2 + c];
                if (letter is < (byte)'a' or > (byte)'z')
                {
                    throw new WordSenseTableFormatException("A dictionary word has a character outside a-z.");
                }

                current[prefix + c] = letter;
            }

            var length = prefix + suffix;
            var mask = raw[position + 2 + suffix];
            if (mask == 0)
            {
                throw new WordSenseTableFormatException("A dictionary word has no part of speech.");
            }

            // Strictly ascending, and the prefix is the longest shared one so the encoding is canonical
            if (i > 0)
            {
                var common = 0;
                while (common < previousLength && common < length && previous[common] == current[common])
                {
                    common++;
                }

                if (current.AsSpan(0, length).SequenceCompareTo(previous.AsSpan(0, previousLength)) <= 0 ||
                    (!isHead && common != prefix))
                {
                    throw new WordSenseTableFormatException("The dictionary words are not in canonical ordinal order.");
                }
            }

            (previous, current) = (current, previous);
            previousLength = length;
            position += suffix + 3;
        }

        if (position != raw.Length)
        {
            throw new WordSenseTableFormatException("The dictionary section has unexpected trailing data.");
        }

        return new LexicalIndex(raw, blocks.ToArray(), count);
    }

    /// <summary>
    ///     Checks that a word is 1 to 255 letters <c>a-z</c>.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <returns>True when it can be stored.</returns>
    internal static bool IsValidWord(string word) =>
        word.Length is >= 1 and <= MaxWordLength && IsLowerAscii(word);

    /// <summary>
    ///     Checks that every character is <c>a-z</c>.
    /// </summary>
    /// <param name="word">The characters.</param>
    /// <returns>True when all are lower-case ASCII letters.</returns>
    private static bool IsLowerAscii(ReadOnlySpan<char> word)
    {
        foreach (var c in word)
        {
            if (c is < 'a' or > 'z')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Compares a word in place with stored ASCII bytes, ordinally.
    /// </summary>
    /// <param name="word">The lower-case ASCII word.</param>
    /// <param name="stored">The stored bytes.</param>
    /// <returns>Negative, zero or positive when the word sorts before, equal to or after the stored bytes.</returns>
    private static int Compare(ReadOnlySpan<char> word, ReadOnlySpan<byte> stored)
    {
        var shared = Math.Min(word.Length, stored.Length);
        for (var i = 0; i < shared; i++)
        {
            var difference = word[i] - stored[i];
            if (difference != 0)
            {
                return difference;
            }
        }

        return word.Length - stored.Length;
    }
}
