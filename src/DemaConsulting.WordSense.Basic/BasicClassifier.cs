using System.Buffers;

namespace DemaConsulting.WordSense.Basic;

/// <summary>
///     The classifier of the basic model: a statistical part-of-speech tagger and a context cue model choose among the
///     lexicon senses of each word.
/// </summary>
/// <remarks>
///     <para>
///     A word with no lexicon entry is <c>(word, null, 0)</c>; a word with one entry is <c>(word, entry, 1)</c>. For a
///     word with several entries, the text is split into sentence segments at the tokens ".", "!" and "?"; the tagger
///     computes the part-of-speech posterior of every word of the segment, the cue model scores the senses from the
///     neighboring words, and <see cref="SenseFusion"/> turns both into one probability per sense. The result is the most
///     probable sense and its probability, so the confidence of an ambiguous word is at least one over the number of
///     senses and the word never goes unanswered.
///     </para>
///     <para>
///     The classifier holds no mutable state, so one instance may be used from many threads at once, and the result
///     depends only on the input words. It is internal: <see cref="BasicWordSenseModel"/> delegates to it.
///     </para>
/// </remarks>
internal sealed class BasicClassifier
{
    private readonly BasicTables? _tables;
    private readonly IReadOnlyDictionary<string, WordDetails[]> _lexicon;

    /// <summary>
    ///     Initializes a new instance of the <see cref="BasicClassifier"/> class that uses the embedded table and the
    ///     lexicon of the core library.
    /// </summary>
    public BasicClassifier()
        : this(null, WordLexicon.Words)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="BasicClassifier"/> class with explicit statistics and lexicon.
    /// </summary>
    /// <param name="tables">The statistics, or null for the embedded table (loaded on first use).</param>
    /// <param name="lexicon">The lexicon, by lower-case word.</param>
    internal BasicClassifier(BasicTables? tables, IReadOnlyDictionary<string, WordDetails[]> lexicon)
    {
        _tables = tables;
        _lexicon = lexicon;
    }

    /// <summary>
    ///     Classifies every word of a tokenized sentence or text.
    /// </summary>
    /// <param name="words">The words in order, including punctuation tokens.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>An array parallel to <paramref name="words"/>.</returns>
    public Task<WordSense[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(words);
        var list = words.ToList();
        if (list.Any(w => w is null))
        {
            throw new ArgumentException("A word must not be null.", nameof(words));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var results = new WordSense[list.Count];
        if (list.Count == 0)
        {
            return Task.FromResult(results);
        }

        var tables = _tables ?? BasicTables.Default;
        var lower = new string[list.Count];
        for (var i = 0; i < lower.Length; i++)
        {
            lower[i] = Lower(list[i]);
        }

        var tokens = ArrayPool<TaggerToken>.Shared.Rent(list.Count);
        var posteriors = ArrayPool<double>.Shared.Rent(list.Count * PosTag.Count);
        var features = new FeatureRef[CueModel.MaxFeatures];
        try
        {
            var start = 0;
            while (start < list.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var end = start;
                while (end < list.Count - 1 && !IsTerminator(list[end]))
                {
                    end++;
                }

                var length = end - start + 1;
                ClassifySegment(tables, list, lower, start, length, tokens, posteriors, features, results);
                start = end + 1;
            }
        }
        finally
        {
            ArrayPool<TaggerToken>.Shared.Return(tokens, clearArray: true);
            ArrayPool<double>.Shared.Return(posteriors);
        }

        return Task.FromResult(results);
    }

    /// <summary>
    ///     Finds the sentence segment around a position.
    /// </summary>
    /// <param name="words">The words as written.</param>
    /// <param name="index">The position.</param>
    /// <returns>The index of the first word of the segment and its length; a segment ends after a ".", "!" or "?" token.</returns>
    internal static (int Start, int Length) SegmentOf(IReadOnlyList<string> words, int index)
    {
        ArgumentNullException.ThrowIfNull(words);
        var start = index;
        while (start > 0 && !IsTerminator(words[start - 1]))
        {
            start--;
        }

        var end = index;
        while (end < words.Count - 1 && !IsTerminator(words[end]))
        {
            end++;
        }

        return (start, end - start + 1);
    }

    /// <summary>
    ///     Lower-cases a word and straightens the typographic apostrophe; a token without a letter becomes empty.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <returns>The lexicon key, or an empty string for punctuation.</returns>
    internal static string Normalize(string word)
    {
        var key = Lower(word);
        return CueModel.HasLetter(key) ? key : string.Empty;
    }

    /// <summary>
    ///     Lower-cases a word and straightens the typographic apostrophe.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <returns>The lower-case token.</returns>
    internal static string Lower(string word) => word.ToLowerInvariant().Replace('\u2019', '\'');

    private static bool IsTerminator(string word) => word is "." or "!" or "?";

    /// <summary>
    ///     Creates the tagger token of a written word.
    /// </summary>
    /// <param name="lexicon">The lexicon, by lower-case word.</param>
    /// <param name="word">The word as written.</param>
    /// <returns>A fixed token for a word without a letter (a number or punctuation), otherwise a word token with the parts of speech the lexicon lists.</returns>
    internal static TaggerToken MakeToken(IReadOnlyDictionary<string, WordDetails[]> lexicon, string word)
    {
        var key = Normalize(word);
        if (key.Length == 0)
        {
            return TaggerToken.Fixed(word.Any(char.IsDigit) ? PosTag.Other : PosTag.Punctuation);
        }

        var mask = lexicon.TryGetValue(key, out var found) ? MaskOf(found) : 0;
        return TaggerToken.Word(key, mask, char.IsUpper(word[0]));
    }

    private static int MaskOf(WordDetails[] senses)
    {
        var mask = 0;
        foreach (var sense in senses)
        {
            mask |= 1 << PosTag.Of(sense.PartOfSpeech);
        }

        return mask;
    }

    private static WordSense Choose(BasicTables tables, string word, WordDetails[] senses, ReadOnlySpan<double> posterior, ReadOnlySpan<FeatureRef> features)
    {
        var count = senses.Length;
        Span<double> cues = count <= 32 ? stackalloc double[32] : new double[count];
        Span<double> probabilities = count <= 32 ? stackalloc double[32] : new double[count];
        cues = cues[..count];
        probabilities = probabilities[..count];
        if (tables.Cues.TryGetValue(senses[0].Lemma, out var cue))
        {
            CueModel.Score(cue, features, senses, cues);
        }
        else
        {
            cues.Clear();
        }

        SenseFusion.Fuse(posterior, senses, cues, tables.HmmWeight, tables.InvTemperature, probabilities);
        var best = SenseFusion.Winner(probabilities);
        return new WordSense(word, senses[best], probabilities[best]);
    }

    private void ClassifySegment(
        BasicTables tables,
        List<string> words,
        string[] lower,
        int start,
        int length,
        TaggerToken[] tokens,
        double[] posteriors,
        FeatureRef[] features,
        WordSense[] results)
    {
        for (var i = 0; i < length; i++)
        {
            tokens[i] = MakeToken(_lexicon, words[start + i]);
        }

        PosTagger.Posteriors(tables, tokens.AsSpan(0, length), posteriors.AsSpan(0, length * PosTag.Count));
        for (var i = 0; i < length; i++)
        {
            var word = words[start + i];
            var key = Normalize(word);
            if (key.Length == 0 || !_lexicon.TryGetValue(key, out var senses))
            {
                results[start + i] = new WordSense(word, null, 0);
            }
            else if (senses.Length == 1)
            {
                results[start + i] = new WordSense(word, senses[0], 1);
            }
            else
            {
                var featureCount = CueModel.Extract(lower.AsSpan(start, length), i, features);
                results[start + i] = Choose(tables, word, senses, posteriors.AsSpan(i * PosTag.Count, PosTag.Count), features.AsSpan(0, featureCount));
            }
        }
    }
}
