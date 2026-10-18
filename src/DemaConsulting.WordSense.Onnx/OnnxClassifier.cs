using System.Collections.Immutable;

namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The classifier of the encoder models: looks every word up in the lexicon, runs the encoder only when some word
///     has several senses that the model has weights for, and scores just that word's senses.
/// </summary>
/// <remarks>
///     <para>
///     The words are joined by single spaces into one text, the text is encoded once (long texts per target from a
///     window around the word), and the hidden states of each ambiguous word are mean-pooled over its word pieces,
///     centered on the global mean vector of the weights and scaled to unit length.
///     </para>
///     <para>
///     A word with no lexicon entry is <c>(word, null, 0)</c>; a word with one entry is <c>(word, entry, 1)</c>. A word
///     with several entries and intact weights is scored over its own senses only: <see cref="WordSense.Details"/> is
///     the best-scoring sense even when the confidence is low, and the confidence is
///     <c>clamp(0.5 + 0.5 * (m - a) / max(a, 0.5), 0, 1)</c>, where <c>m</c> is the margin between the best and the
///     second-best score and <c>a</c> the lemma's abstain margin. It grows monotonically with the margin and is
///     exactly 0.5 where the lemma's own abstain rule would switch. A word with several entries but missing or
///     mismatched weights is answered with its first entry and the confidence <c>1/n</c> for <c>n</c> entries; the
///     first entry is the most frequent sense of the dictionary. A lemma whose lexicon entry has more senses than the
///     weights were trained for is scored over the trained senses only. A trailing
///     <see cref="PartOfSpeech.Auxiliary"/> sense is not counted as an entry, because no weights are trained for it: the
///     encoder models never answer <see cref="PartOfSpeech.Auxiliary"/>, only the Basic model does.
///     </para>
/// </remarks>
internal sealed class OnnxClassifier
{
    private readonly OnnxEncoder _encoder;

    private readonly WeightedLemmaTable _table;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OnnxClassifier"/> class.
    /// </summary>
    /// <param name="encoder">The encoder; it is owned by the caller.</param>
    /// <param name="table">The matched weights.</param>
    public OnnxClassifier(OnnxEncoder encoder, WeightedLemmaTable table)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _table = table ?? throw new ArgumentNullException(nameof(table));
    }

    /// <summary>
    ///     Tells whether any word of a list has several senses, which is the cheap pre-check before the weights are consulted.
    /// </summary>
    /// <param name="words">The words.</param>
    /// <returns>True when some word has several senses.</returns>
    internal static bool HasMultiSenseWord(IReadOnlyList<string> words) =>
        words.Any(w => w.Length != 0 && WordLexicon.Words.TryGetValue(Normalize(w), out var senses) && EffectiveSenseCount(senses) > 1);

    /// <summary>
    ///     Tells whether any word of a list has several senses and intact weights, which is the only case that needs the encoder.
    /// </summary>
    /// <param name="words">The words.</param>
    /// <param name="table">The matched weights.</param>
    /// <returns>True when the model files, the encoder and the weights are needed to classify the list.</returns>
    internal static bool NeedsModel(IReadOnlyList<string> words, WeightedLemmaTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return words.Any(w => w.Length != 0
            && WordLexicon.Words.TryGetValue(Normalize(w), out var senses)
            && EffectiveSenseCount(senses) > 1
            && table.Find(senses[0].Lemma) is not null);
    }

    /// <summary>
    ///     Classifies a list that needs no model, using the lexicon alone: no model file, encoder or weights.
    /// </summary>
    /// <param name="words">The words; no word may have several senses together with intact weights.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The results, parallel to the words.</returns>
    internal static WordSense[] ResolveWithoutModel(IReadOnlyList<string> words, CancellationToken cancellationToken)
    {
        var results = new WordSense[words.Count];
        for (var i = 0; i < results.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results[i] = Resolve(null, words[i], out _, out _);
        }

        return results;
    }

    /// <summary>
    ///     Classifies every word of a tokenized sentence or text.
    /// </summary>
    /// <param name="words">The words in order, including punctuation tokens.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>An array parallel to <paramref name="words"/>.</returns>
    public async Task<WordSense[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(words);
        var list = words.ToList();
        if (list.Any(w => w is null))
        {
            throw new ArgumentException("A word must not be null.", nameof(words));
        }

        var results = new WordSense[list.Count];
        var pending = new List<(int Index, ImmutableArray<WordDetails> Candidates, WeightedLemma Lemma)>();
        for (var i = 0; i < list.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results[i] = Resolve(_table, list[i], out var lemma, out var candidates);
            if (lemma is not null)
            {
                pending.Add((i, candidates, lemma));
            }
        }

        if (pending.Count == 0)
        {
            return results;
        }

        var pooled = await _encoder.EncodeAsync(list, pending.Select(p => p.Index).ToList(), cancellationToken).ConfigureAwait(false);
        var mean = _table.Data.Mean;
        for (var p = 0; p < pending.Count; p++)
        {
            var (index, candidates, lemma) = pending[p];
            if (pooled[p] is { } vector && vector.Length == mean.Length && FeatureExtractor.Center(vector, mean) is { } feature)
            {
                var (details, confidence) = Pick(lemma, candidates, feature);
                results[index] = new WordSense(list[index], details, confidence);
            }
        }

        return results;
    }

    /// <summary>
    ///     Looks one word up and decides whether it needs context.
    /// </summary>
    /// <param name="table">The weights, or null when the caller knows that no word is ambiguous.</param>
    /// <param name="word">The word.</param>
    /// <param name="lemma">Receives the weights when the word needs context; otherwise null.</param>
    /// <param name="candidates">Receives the senses of the word when it needs context.</param>
    /// <returns>The final result, or the unresolved placeholder when <paramref name="lemma"/> is set.</returns>
    private static WordSense Resolve(WeightedLemmaTable? table, string word, out WeightedLemma? lemma, out ImmutableArray<WordDetails> candidates)
    {
        lemma = null;
        candidates = [];
        var unknown = new WordSense(word, null, 0);
        if (word.Length == 0 || !WordLexicon.Words.TryGetValue(Normalize(word), out var senses))
        {
            return unknown;
        }

        var count = EffectiveSenseCount(senses);
        if (count == 1)
        {
            return new WordSense(word, senses[0], 1);
        }

        // Several senses without intact weights for the lemma: the first sense (the most frequent one in the
        // dictionary order) with a confidence of one over the number of senses
        if (table?.Find(senses[0].Lemma) is not { } weighted)
        {
            return new WordSense(word, senses[0], 1.0 / count);
        }

        lemma = weighted;
        candidates = senses;
        return unknown;
    }

    /// <summary>
    ///     Counts the senses of a word that the encoder models can answer: a trailing <see cref="PartOfSpeech.Auxiliary"/>
    ///     sense is ignored because no weights are ever trained for it, so the encoder models never answer it.
    /// </summary>
    /// <param name="senses">The senses of the word.</param>
    /// <returns>The number of senses, without a trailing auxiliary sense that follows another sense.</returns>
    private static int EffectiveSenseCount(ImmutableArray<WordDetails> senses) =>
        senses.Length > 1 && senses[^1].PartOfSpeech == PartOfSpeech.Auxiliary ? senses.Length - 1 : senses.Length;

    /// <summary>
    ///     Lower-cases a word and straightens the typographic apostrophe.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <returns>The lexicon key.</returns>
    private static string Normalize(string word) => word.ToLowerInvariant().Replace('\u2019', '\'');

    /// <summary>
    ///     Scores the senses of a lemma for a feature and picks the best candidate.
    /// </summary>
    /// <param name="lemma">The weighted lemma.</param>
    /// <param name="candidates">The senses the word can have.</param>
    /// <param name="feature">The unit-length context feature.</param>
    /// <returns>The best sense and its confidence; no sense and confidence 0 when the candidates do not fit the weights.</returns>
    internal static (WordDetails? Details, double Confidence) Pick(WeightedLemma lemma, ImmutableArray<WordDetails> candidates, float[] feature)
    {
        var weights = lemma.Weights;
        if (feature.Length != weights.Dimension)
        {
            return (null, 0);
        }

        var scores = new float[weights.SenseCount];
        weights.Scores(feature, scores);

        // Rank only the senses the word itself can have and the weights were trained for; the weights score the first
        // senses of the lemma in lexicon order, and the lexicon may hold further senses after them
        var all = WordLexicon.Words[lemma.Lemma];
        var allowed = new List<int>(candidates.Length);
        for (var i = 0; i < Math.Min(all.Length, weights.SenseCount); i++)
        {
            if (candidates.IndexOf(all[i]) >= 0)
            {
                allowed.Add(i);
            }
        }

        if (allowed.Count == 0)
        {
            return (null, 0);
        }

        var (best, margin) = allowed.Count == weights.SenseCount ? LemmaWeights.Rank(scores) : RankSubset(scores, allowed);
        return (all[best], LemmaWeights.Confidence(margin, weights.AbstainMargin));
    }

    /// <summary>
    ///     Ranks a subset of the scores.
    /// </summary>
    /// <param name="scores">All scores.</param>
    /// <param name="indexes">The indexes in play.</param>
    /// <returns>The best original index and the margin to the second best; an infinite margin for a single index.</returns>
    private static (int Best, float Margin) RankSubset(float[] scores, List<int> indexes)
    {
        if (indexes.Count == 1)
        {
            return (indexes[0], float.PositiveInfinity);
        }

        var best = indexes[0];
        var second = float.NegativeInfinity;
        foreach (var i in indexes.Skip(1))
        {
            if (scores[i] > scores[best])
            {
                second = scores[best];
                best = i;
            }
            else if (scores[i] > second)
            {
                second = scores[i];
            }
        }

        if (float.IsNegativeInfinity(second))
        {
            second = scores[indexes[0]];
        }

        return (best, scores[best] - second);
    }
}
