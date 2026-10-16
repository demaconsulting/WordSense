using System.Security.Cryptography;

namespace DemaConsulting.WordSense;

/// <summary>
///     Advanced: the lexicon table: every known lemma with its senses, the form-to-lemma map and the morphology rules,
///     plus its versioned binary serialization. It holds no model data, so it works without any encoder.
/// </summary>
/// <remarks>
///     <para>
///     One vocabulary of <see cref="LemmaEntry"/> values backs every lookup: lemmas that need context to choose a
///     sense (<see cref="ContextLemmas"/>, held in memory, with their pronunciation meanings) and dictionary lemmas,
///     which are kept front-coded in one byte array and turned into entries only when asked for. A lemma appears once
///     and a form never spells another lemma. The per-lemma classifiers that choose between the senses of a context
///     lemma live in a <see cref="WordSenseWeightsTable"/> per encoder model, which names this table by its
///     <see cref="Sha256"/>.
///     </para>
///     <para>
///     Binary layout (little-endian): magic <c>WSNS</c>, u16 version (6), u16 flags (the enabled
///     <see cref="LemmaMorphologyRules"/>), u32 context lemma count, context lemmas in ordinal order (string lemma,
///     u16 sense count, per sense: u8 part of speech, string meaning (empty when none), string respelling (empty when
///     none)), u32 form count, forms (string form, u32 context lemma index; the writer emits them in ordinal order and
///     the reader accepts any order), the dictionary section (u32 word count,
///     u16 block size, u32 decoded length, u32 compressed length, raw-DEFLATE of front-coded records: u8 shared prefix
///     length, u8 suffix length, suffix, u8 part-of-speech mask), and a trailing
///     32-byte SHA-256 of everything before it. Strings are a u16 byte length followed by UTF-8.
///     </para>
///     <para>
///     The reader validates every length against the bytes that remain, caps every count, and checks
///     the trailing digest first, so truncated, corrupted or hostile input fails with
///     <see cref="WordSenseTableFormatException"/> rather than a different exception or an
///     oversized allocation. Only version 6 is read. Instances are thread-safe provided callers treat the
///     <see cref="Forms"/> map supplied to the constructor as read-only; the constructor copies the lemma list but not
///     the forms map.
///     </para>
///     <para>
///     Most callers never touch a table: <see cref="WordSenseClassifier.Create"/> loads the embedded
///     one. Use this type to inspect which lemmas and senses a build supports, or to read a table from
///     bytes with <see cref="Read"/>.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     WordSenseTable table = WordSenseTable.LoadEmbedded();
///     Console.WriteLine($"{table.Count} lemmas, {table.ContextLemmas.Count} of them need context");
///
///     if (table.TryGetLemma("record", out LemmaEntry entry))
///     {
///         foreach (var sense in entry.Senses)
///         {
///             Console.WriteLine($"{sense.Pos}: {sense.Meaning ?? "-"}");
///         }
///     }
///
///     // A table round-trips through its binary form
///     WordSenseTable copy = WordSenseTable.Read(table.ToArray());
///     </code>
/// </example>
public sealed class WordSenseTable
{
    /// <summary>
    ///     The four magic bytes that start every table.
    /// </summary>
    private static readonly byte[] Magic = "WSNS"u8.ToArray();

    /// <summary>
    ///     The only serialization version this library reads and writes.
    /// </summary>
    private const ushort CurrentVersion = 6;

    /// <summary>
    ///     The length of a SHA-256 digest in bytes.
    /// </summary>
    private const int HashLength = 32;

    /// <summary>
    ///     The largest context lemma count accepted from a file.
    /// </summary>
    private const int MaxLemmas = 100_000;

    /// <summary>
    ///     The largest form count accepted from a file.
    /// </summary>
    private const int MaxForms = 2_000_000;

    /// <summary>
    ///     The largest sense count per lemma accepted from a file.
    /// </summary>
    private const int MaxSenses = 64;

    /// <summary>
    ///     The fewest bytes a serialized form entry can take: a string length and a lemma index.
    /// </summary>
    private const int MinFormBytes = 6;

    /// <summary>
    ///     The fewest bytes a serialized context lemma can take: name length, sense count and two minimal senses.
    /// </summary>
    private const int MinLemmaBytes = 14;

    /// <summary>
    ///     The manifest resource name of the table embedded in this assembly.
    /// </summary>
    internal const string EmbeddedResourceName = "DemaConsulting.WordSense.wordsense-lexicon.bin";

    /// <summary>
    ///     Maps each context lemma to its index in <see cref="ContextLemmas"/>.
    /// </summary>
    private readonly Dictionary<string, int> _lemmaIndex;

    /// <summary>
    ///     The dictionary lemmas that need no context.
    /// </summary>
    private readonly LexicalIndex _lexicon;

    /// <summary>
    ///     The lazily computed digest of the serialized table.
    /// </summary>
    private readonly Lazy<string> _sha256;

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseTable"/> class.
    /// </summary>
    /// <param name="lemmas">
    ///     Every lemma of the table, unique and in ordinal order of lemma. An entry that is not a context lemma is a
    ///     dictionary word: its lemma must be 1 to 255 letters <c>a-z</c>, and its senses are stored as a
    ///     set, so it reads back with its senses in ascending <see cref="PartOfSpeech"/> order.
    /// </param>
    /// <param name="forms">
    ///     The map from lower-case word form to lemma; every lemma must be a context lemma, and a form must not spell a
    ///     different lemma of the table.
    /// </param>
    /// <param name="morphology">The suffix rules that may derive an absent word from a dictionary lemma.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="lemmas"/> or <paramref name="forms"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when the content is inconsistent.</exception>
    /// <remarks>
    ///     An element of <paramref name="lemmas"/> that is null also throws <see cref="ArgumentNullException"/>.
    /// </remarks>
    public WordSenseTable(
        IReadOnlyList<LemmaEntry> lemmas,
        IReadOnlyDictionary<string, string> forms,
        LemmaMorphologyRules morphology = LemmaMorphologyRules.Default)
        : this(Partition(lemmas, out var lexicon), lexicon, forms, morphology)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseTable"/> class from its two stores.
    /// </summary>
    /// <param name="trained">The context lemmas, unique and in ordinal order.</param>
    /// <param name="lexicon">The dictionary lemmas.</param>
    /// <param name="forms">The form map.</param>
    /// <param name="morphology">The enabled morphology rules.</param>
    private WordSenseTable(
        List<LemmaEntry> trained,
        LexicalIndex lexicon,
        IReadOnlyDictionary<string, string> forms,
        LemmaMorphologyRules morphology)
    {
        ArgumentNullException.ThrowIfNull(forms);

        // Validate everything the classifier will rely on
        if ((morphology & ~LemmaMorphologyRules.Default) != 0)
        {
            throw new ArgumentException("The morphology rules are not defined.", nameof(morphology));
        }

        _lemmaIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < trained.Count; i++)
        {
            // Partition guarantees uniqueness and order; a lemma must also not hide in the dictionary
            _lemmaIndex.Add(trained[i].Lemma, i);
            if (lexicon.TryFind(trained[i].Lemma, out _))
            {
                throw new ArgumentException("A lemma is both a context lemma and a dictionary word.", nameof(trained));
            }
        }

        // A form maps to a context lemma and may not spell another lemma
        foreach (var (form, lemma) in forms)
        {
            if (!_lemmaIndex.ContainsKey(lemma))
            {
                throw new ArgumentException($"A form refers to lemma '{lemma}' that is not a context lemma.", nameof(forms));
            }

            if ((form != lemma && _lemmaIndex.ContainsKey(form)) || lexicon.TryFind(form, out _))
            {
                throw new ArgumentException($"The form '{form}' shadows a lemma.", nameof(forms));
            }
        }

        ContextLemmas = trained;
        _lexicon = lexicon;
        Forms = forms;
        Morphology = morphology;
        _sha256 = new Lazy<string>(() => Convert.ToHexString(ToArray().AsSpan()[^HashLength..]).ToLowerInvariant());
    }

    /// <summary>
    ///     Gets the lower-case hexadecimal SHA-256 of the serialized table (the trailing digest of
    ///     <see cref="ToArray"/>), which a <see cref="WordSenseWeightsTable"/> names to say which table it was trained for.
    /// </summary>
    public string Sha256 => _sha256.Value;

    /// <summary>
    ///     Gets the number of lemmas in the table, with or without a need for context.
    /// </summary>
    public int Count => ContextLemmas.Count + _lexicon.Count;

    /// <summary>
    ///     Gets the lemmas that need context to choose a sense, in ordinal order of lemma. These are the lemmas an
    ///     encoder model has a classifier for.
    /// </summary>
    public IReadOnlyList<LemmaEntry> ContextLemmas { get; }

    /// <summary>
    ///     Gets every lemma of the table in ordinal order. Dictionary lemmas are decoded one at a time as the
    ///     sequence is enumerated, so enumerating it all is cheap in memory but not free in time.
    /// </summary>
    public IEnumerable<LemmaEntry> Lemmas => EnumerateLemmas();

    /// <summary>
    ///     Gets the map from lower-case word form to the context lemma it inflects.
    /// </summary>
    public IReadOnlyDictionary<string, string> Forms { get; }

    /// <summary>
    ///     Gets the suffix rules that may derive an absent word from a dictionary lemma.
    /// </summary>
    public LemmaMorphologyRules Morphology { get; }

    /// <summary>
    ///     Looks up a lemma, whether or not it needs context.
    /// </summary>
    /// <param name="lemma">The lower-case lemma.</param>
    /// <param name="entry">Receives the entry when found.</param>
    /// <returns>True when the table has the lemma.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lemma"/> is null.</exception>
    /// <remarks>
    ///     A dictionary entry is created for each call, so its <see cref="LemmaEntry.Candidates"/> are equal, but not
    ///     reference-equal, across calls.
    /// </remarks>
    public bool TryGetLemma(string lemma, out LemmaEntry entry)
    {
        ArgumentNullException.ThrowIfNull(lemma);
        if (_lemmaIndex.TryGetValue(lemma, out var index))
        {
            entry = ContextLemmas[index];
            return true;
        }

        if (_lexicon.TryFind(lemma, out var mask))
        {
            entry = CreateDictionaryEntry(lemma, mask);
            return true;
        }

        entry = null!;
        return false;
    }

    /// <summary>
    ///     Resolves a surface word to a lemma entry: the form map, then an exact lemma, then a possessive of a context
    ///     lemma, then (when dictionary words are enabled) the morphology rules.
    /// </summary>
    /// <param name="word">The word as written; it is lower-cased and a typographic apostrophe is straightened.</param>
    /// <param name="useDictionary">True when lemmas that need no context may be returned.</param>
    /// <param name="resolution">Receives the entry and the sense the word was narrowed to, if any.</param>
    /// <returns>True when the word resolves.</returns>
    internal bool TryResolve(string word, bool useDictionary, out LemmaResolution resolution)
    {
        var lower = word.ToLowerInvariant().Replace('\u2019', '\'');
        if (TryResolveExact(lower, useDictionary, out var entry))
        {
            resolution = new LemmaResolution(entry, -1);
            return true;
        }

        // A possessive shares the lemma of the bare word, but only for context lemmas
        if (lower.Length > 2 && lower.EndsWith("'s", StringComparison.Ordinal) &&
            TryResolveExact(lower[..^2], useDictionary, out entry) && entry.IsContextual)
        {
            resolution = new LemmaResolution(entry, -1);
            return true;
        }

        // Only a word the table lacks is derived, so a rule can never override an entry
        if (useDictionary)
        {
            return LemmaMorphology.TryResolve(lower, Morphology, this, out resolution);
        }

        resolution = default;
        return false;
    }

    /// <summary>
    ///     Looks a lower-case word up as an inflected form or as a lemma.
    /// </summary>
    /// <param name="lower">The lower-case word.</param>
    /// <param name="useDictionary">True when dictionary lemmas count.</param>
    /// <param name="entry">Receives the entry.</param>
    /// <returns>True when found.</returns>
    private bool TryResolveExact(string lower, bool useDictionary, out LemmaEntry entry)
    {
        if (Forms.TryGetValue(lower, out var lemma) && _lemmaIndex.TryGetValue(lemma, out var formIndex))
        {
            entry = ContextLemmas[formIndex];
            return true;
        }

        if (_lemmaIndex.TryGetValue(lower, out var index))
        {
            entry = ContextLemmas[index];
            return true;
        }

        if (useDictionary && _lexicon.TryFind(lower, out var mask))
        {
            entry = CreateDictionaryEntry(lower, mask);
            return true;
        }

        entry = null!;
        return false;
    }

    /// <summary>
    ///     Looks a dictionary word up without creating an entry.
    /// </summary>
    /// <param name="word">The lower-case word.</param>
    /// <param name="mask">Receives the part-of-speech mask.</param>
    /// <returns>True when the dictionary has the word.</returns>
    internal bool TryGetDictionaryMask(ReadOnlySpan<char> word, out byte mask) => _lexicon.TryFind(word, out mask);

    /// <summary>
    ///     Creates the entry of a dictionary word: one sense per part of speech in the mask, in ascending order.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="mask">The part-of-speech mask.</param>
    /// <returns>The entry.</returns>
    internal static LemmaEntry CreateDictionaryEntry(string lemma, byte mask)
    {
        var senses = new List<SenseDefinition>(2);
        for (var pos = 0; pos < 8; pos++)
        {
            if ((mask & (1 << pos)) != 0)
            {
                senses.Add(new SenseDefinition((PartOfSpeech)pos));
            }
        }

        return new LemmaEntry(lemma, senses);
    }

    /// <summary>
    ///     Splits the supplied lemmas into the context lemmas and the compiled dictionary.
    /// </summary>
    /// <param name="lemmas">All lemmas, unique and in ordinal order.</param>
    /// <param name="lexicon">Receives the dictionary lemmas.</param>
    /// <returns>The context lemmas.</returns>
    private static List<LemmaEntry> Partition(IReadOnlyList<LemmaEntry> lemmas, out LexicalIndex lexicon)
    {
        ArgumentNullException.ThrowIfNull(lemmas);
        var trained = new List<LemmaEntry>();
        var words = new List<(string Word, byte Mask)>();
        for (var i = 0; i < lemmas.Count; i++)
        {
            var entry = lemmas[i] ?? throw new ArgumentNullException(nameof(lemmas), "A lemma entry is null.");
            if (i > 0 && string.CompareOrdinal(lemmas[i - 1].Lemma, entry.Lemma) >= 0)
            {
                throw new ArgumentException("Lemma entries must be unique and in ordinal order.", nameof(lemmas));
            }

            if (entry.IsContextual)
            {
                trained.Add(entry);
            }
            else
            {
                var mask = entry.Senses.Aggregate(0, (m, s) => m | LemmaMorphology.Bit(s.Pos));
                words.Add((entry.Lemma, (byte)mask));
            }
        }

        lexicon = words.Count == 0 ? LexicalIndex.Empty : LexicalIndex.Build(words);
        return trained;
    }

    /// <summary>
    ///     Enumerates the context and dictionary lemmas as one ordinal sequence.
    /// </summary>
    /// <returns>Every lemma.</returns>
    private IEnumerable<LemmaEntry> EnumerateLemmas()
    {
        using var words = _lexicon.Enumerate().GetEnumerator();
        var hasWord = words.MoveNext();
        foreach (var trained in ContextLemmas)
        {
            while (hasWord && string.CompareOrdinal(words.Current.Word, trained.Lemma) < 0)
            {
                yield return CreateDictionaryEntry(words.Current.Word, words.Current.Mask);
                hasWord = words.MoveNext();
            }

            yield return trained;
        }

        while (hasWord)
        {
            yield return CreateDictionaryEntry(words.Current.Word, words.Current.Mask);
            hasWord = words.MoveNext();
        }
    }

    /// <summary>
    ///     Loads the table that is embedded in this assembly. It needs no model files and no network access, so it can
    ///     be used to browse <see cref="Lemmas"/> and <see cref="Forms"/> offline.
    /// </summary>
    /// <returns>The embedded table.</returns>
    /// <exception cref="WordSenseTableFormatException">Thrown when the table is missing or malformed.</exception>
    /// <example>
    ///     <code>
    ///     WordSenseTable table = WordSenseTable.LoadEmbedded();
    ///     bool hasRecord = table.TryGetLemma("record", out _);
    ///     </code>
    /// </example>
    public static WordSenseTable LoadEmbedded()
    {
        // Copy the resource into memory; it is about 1.6 MB
        using var stream = typeof(WordSenseTable).Assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new WordSenseTableFormatException("The embedded lexicon table is missing from the assembly.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Read(memory.ToArray());
    }

    /// <summary>
    ///     Parses a serialized table.
    /// </summary>
    /// <param name="data">The serialized table bytes.</param>
    /// <returns>The parsed table.</returns>
    /// <remarks>
    ///     To bound memory the reader rejects, whatever the checksum says: more than
    ///     100,000 context lemmas; more than 2,000,000 forms; more than 400,000 dictionary words or 4 MB of decoded
    ///     dictionary; fewer than 2 or more than 64 senses in a context lemma; an undefined part of speech; sense sets
    ///     that are inconsistent (see <see cref="LemmaEntry"/>); a dictionary that is not strictly ascending lower-case
    ///     <c>a-z</c> words in its canonical encoding; undefined morphology rules; and a form or context lemma that
    ///     shadows a dictionary word. Strings are limited to 65,535
    ///     UTF-8 bytes by their length prefix.
    /// </remarks>
    /// <exception cref="WordSenseTableFormatException">
    ///     Thrown when the data is truncated, corrupted, of an unsupported version, or otherwise malformed.
    /// </exception>
    public static WordSenseTable Read(ReadOnlySpan<byte> data)
    {
        try
        {
            return ReadCore(data);
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            // Constructor validation failures mean the content is inconsistent, which is a format error
            throw new WordSenseTableFormatException("The lexicon table content is inconsistent.", ex);
        }
    }

    /// <summary>
    ///     Serializes the table to its binary format.
    /// </summary>
    /// <returns>The serialized bytes, including the trailing digest.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a string is too long to encode.</exception>
    /// <exception cref="OverflowException">
    ///     Thrown when a count does not fit its field in the format, for example more than 65,535 senses in one
    ///     lemma. The writer does not apply the limits that <see cref="Read"/>
    ///     enforces, so a table that is written successfully can still be rejected when read back.
    /// </exception>
    public byte[] ToArray()
    {
        using var stream = new MemoryStream();
        Write(stream);
        return stream.ToArray();
    }

    /// <summary>
    ///     Serializes the table to a stream.
    /// </summary>
    /// <param name="destination">The stream receiving the bytes.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a string is too long to encode.</exception>
    /// <exception cref="OverflowException">
    ///     Thrown when a count does not fit its field in the format (see <see cref="ToArray"/>).
    /// </exception>
    public void Write(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        // Build the body in memory so the digest can be appended
        var buffer = new MemoryStream();
        buffer.Write(Magic);
        TableWriter.WriteU16(buffer, CurrentVersion);
        TableWriter.WriteU16(buffer, (int)Morphology);

        TableWriter.WriteU32(buffer, ContextLemmas.Count);
        foreach (var lemma in ContextLemmas)
        {
            TableWriter.WriteString(buffer, lemma.Lemma);
            TableWriter.WriteU16(buffer, lemma.Senses.Count);
            foreach (var sense in lemma.Senses)
            {
                buffer.WriteByte((byte)sense.Pos);
                TableWriter.WriteString(buffer, sense.Meaning ?? string.Empty);
                TableWriter.WriteString(buffer, sense.Respelling ?? string.Empty);
            }
        }

        // Forms are written in ordinal order so output is deterministic
        var forms = Forms.OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
        TableWriter.WriteU32(buffer, forms.Count);
        foreach (var form in forms)
        {
            TableWriter.WriteString(buffer, form.Key);
            TableWriter.WriteU32(buffer, _lemmaIndex[form.Value]);
        }

        // The dictionary section
        var compressed = _lexicon.Compress();
        TableWriter.WriteU32(buffer, _lexicon.Count);
        TableWriter.WriteU16(buffer, LexicalIndex.BlockSize);
        TableWriter.WriteU32(buffer, _lexicon.RawLength);
        TableWriter.WriteU32(buffer, compressed.Length);
        buffer.Write(compressed);

        // Append the integrity digest of everything written so far
        var body = buffer.ToArray();
        destination.Write(body);
        destination.Write(SHA256.HashData(body));
    }

    /// <summary>
    ///     Parses a serialized table, letting constructor validation errors escape to the caller.
    /// </summary>
    /// <param name="data">The serialized bytes.</param>
    /// <returns>The parsed table.</returns>
    private static WordSenseTable ReadCore(ReadOnlySpan<byte> data)
    {
        // Verify the digest before trusting any length field
        if (data.Length < Magic.Length + 4 + HashLength)
        {
            throw new WordSenseTableFormatException("The lexicon table is truncated.");
        }

        var body = data[..^HashLength];
        if (!SHA256.HashData(body).AsSpan().SequenceEqual(data[^HashLength..]))
        {
            throw new WordSenseTableFormatException("The lexicon table is corrupted: the checksum does not match.");
        }

        var reader = new TableReader(body);
        if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic))
        {
            throw new WordSenseTableFormatException("The lexicon table has an invalid signature.");
        }

        var version = reader.ReadU16();
        if (version != CurrentVersion)
        {
            throw new WordSenseTableFormatException($"Unsupported lexicon table version {version}.");
        }

        var flags = reader.ReadU16();
        if ((flags & ~(int)LemmaMorphologyRules.Default) != 0)
        {
            throw new WordSenseTableFormatException("The lexicon table enables undefined morphology rules.");
        }

        var lemmaCount = reader.ReadCount(MaxLemmas, 0);
        if ((long)lemmaCount * MinLemmaBytes > reader.Remaining)
        {
            throw new WordSenseTableFormatException("The lemma count exceeds the data of the lexicon table.");
        }

        var lemmas = new List<LemmaEntry>(lemmaCount);
        for (var i = 0; i < lemmaCount; i++)
        {
            var lemma = reader.ReadString();
            var senseCount = reader.ReadU16();
            if (senseCount < 2 || senseCount > MaxSenses)
            {
                throw new WordSenseTableFormatException("A lemma has an invalid sense count.");
            }

            var senses = new SenseDefinition[senseCount];
            for (var s = 0; s < senseCount; s++)
            {
                senses[s] = ReadSense(ref reader);
            }

            lemmas.Add(new LemmaEntry(lemma, senses, true));
        }

        // The constructor of the private overload checks order and uniqueness of the context lemmas
        for (var i = 1; i < lemmas.Count; i++)
        {
            if (string.CompareOrdinal(lemmas[i - 1].Lemma, lemmas[i].Lemma) >= 0)
            {
                throw new WordSenseTableFormatException("The context lemmas are not unique and in ordinal order.");
            }
        }

        var formCount = reader.ReadCount(MaxForms, 0);

        // Every form takes at least a string length and a lemma index, so the declared count must fit the bytes left;
        // this keeps a hostile count from reserving memory that the data cannot back
        if ((long)formCount * MinFormBytes > reader.Remaining)
        {
            throw new WordSenseTableFormatException("The form count exceeds the data of the lexicon table.");
        }

        var forms = new Dictionary<string, string>(formCount, StringComparer.Ordinal);
        for (var i = 0; i < formCount; i++)
        {
            var form = reader.ReadString();
            var lemmaIndex = reader.ReadU32();
            if (lemmaIndex >= lemmas.Count || !forms.TryAdd(form, lemmas[(int)lemmaIndex].Lemma))
            {
                throw new WordSenseTableFormatException("A form entry is invalid or duplicated.");
            }
        }

        var wordCount = reader.ReadCount(LexicalIndex.MaxWords, 0);
        if (reader.ReadU16() != LexicalIndex.BlockSize)
        {
            throw new WordSenseTableFormatException("The dictionary section has an unsupported block size.");
        }

        var rawLength = reader.ReadCount(LexicalIndex.MaxRawLength, 0);
        var compressedLength = reader.ReadCount(int.MaxValue, 0);
        var lexicon = LexicalIndex.Decompress(wordCount, rawLength, reader.ReadBytes(compressedLength));

        if (!reader.AtEnd)
        {
            throw new WordSenseTableFormatException("The lexicon table has unexpected trailing data.");
        }

        return new WordSenseTable(lemmas, lexicon, forms, (LemmaMorphologyRules)flags);
    }

    /// <summary>
    ///     Reads one sense definition: its part of speech, meaning and respelling.
    /// </summary>
    /// <param name="reader">The table reader.</param>
    /// <returns>The sense definition.</returns>
    private static SenseDefinition ReadSense(ref TableReader reader)
    {
        var pos = reader.ReadByte();
        if (!Enum.IsDefined((PartOfSpeech)pos))
        {
            throw new WordSenseTableFormatException("A sense has an undefined part of speech.");
        }

        var meaning = reader.ReadString();
        var respelling = reader.ReadString();
        return new SenseDefinition((PartOfSpeech)pos, meaning.Length == 0 ? null : meaning, respelling.Length == 0 ? null : respelling);
    }
}
