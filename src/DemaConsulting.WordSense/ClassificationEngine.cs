namespace DemaConsulting.WordSense;

/// <summary>
///     The shared classification flow behind every model: look words up, decide which need context, ask a feature
///     source for that context and pick the best sense.
/// </summary>
internal sealed class ClassificationEngine : IWordSenseClassifier
{
    private const string OpeningPunctuation = "\"'([{\u2018\u201C\u201A\u201E\u00AB\u2039";

    private const string SentenceEndPunctuation = ".!?:";

    private readonly IReadOnlyDictionary<string, WordDetails[]> _words;

    private readonly WeightedLemmaTable _table;

    private readonly IFeatureSource _source;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ClassificationEngine"/> class.
    /// </summary>
    /// <param name="words">The word table.</param>
    /// <param name="table">The trained lemmas.</param>
    /// <param name="source">The context feature source.</param>
    public ClassificationEngine(IReadOnlyDictionary<string, WordDetails[]> words, WeightedLemmaTable table, IFeatureSource source)
    {
        _words = words ?? throw new ArgumentNullException(nameof(words));
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <inheritdoc />
    public async Task<WordSense[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(words);
        var list = words.ToList();
        if (list.Any(w => w is null))
        {
            throw new ArgumentException("A word must not be null.", nameof(words));
        }

        var results = new WordSense[list.Count];
        var pending = new List<(int Index, WordDetails[] Candidates, WeightedLemma Lemma)>();
        for (var i = 0; i < list.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results[i] = Resolve(list, i, out var lemma, out var candidates);
            if (lemma is not null)
            {
                pending.Add((i, candidates, lemma));
            }
        }

        if (pending.Count == 0)
        {
            return results;
        }

        var features = await _source.GetFeaturesAsync(list, pending.Select(p => p.Index).ToList(), cancellationToken).ConfigureAwait(false);
        for (var p = 0; p < pending.Count; p++)
        {
            var (index, candidates, lemma) = pending[p];
            if (features[p] is { } feature && Pick(lemma, candidates, feature) is { } picked)
            {
                results[index] = new WordSense(list[index], picked.Details, picked.Confidence);
            }
        }

        return results;
    }

    /// <summary>
    ///     Looks one word up and decides whether it needs context.
    /// </summary>
    /// <param name="list">All words of the sentence.</param>
    /// <param name="index">The index of the word.</param>
    /// <param name="lemma">Receives the weights when the word needs context; otherwise null.</param>
    /// <param name="candidates">Receives the senses of the word when it needs context.</param>
    /// <returns>The final result, or the unresolved placeholder when <paramref name="lemma"/> is set.</returns>
    private WordSense Resolve(List<string> list, int index, out WeightedLemma? lemma, out WordDetails[] candidates)
    {
        lemma = null;
        candidates = [];
        var word = list[index];
        var unknown = new WordSense(word, null, 0);
        if (word.Length == 0)
        {
            return unknown;
        }

        var key = Words.Normalize(word);
        if (!_words.TryGetValue(key, out var senses))
        {
            // A possessive resolves to its base word, but only when the base word has trained weights
            if (key.Length > 2 && key.EndsWith("'s", StringComparison.Ordinal) &&
                _words.TryGetValue(key[..^2], out var baseSenses) && _table.Find(baseSenses[0].Lemma) is not null)
            {
                senses = baseSenses;
            }
            else
            {
                return unknown;
            }
        }

        var weighted = _table.Find(senses[0].Lemma);

        // Without trained weights a capitalized word is a name unless the position explains the capital
        if (weighted is null && char.IsUpper(word[0]) && !CapitalExplained(list, index))
        {
            return unknown;
        }

        if (senses.Length == 1)
        {
            return new WordSense(word, senses[0], 1);
        }

        if (weighted is null)
        {
            return unknown;
        }

        lemma = weighted;
        candidates = senses;
        return unknown;
    }

    /// <summary>
    ///     Checks whether the position of a capitalized word explains the capital letter.
    /// </summary>
    /// <param name="list">All words of the sentence.</param>
    /// <param name="index">The index of the word.</param>
    /// <returns>True for the first word, a word after sentence-ending or opening punctuation, and the word I.</returns>
    private static bool CapitalExplained(List<string> list, int index)
    {
        if (index == 0 || list[index] == "I")
        {
            return true;
        }

        var previous = list[index - 1];
        return previous.Length > 0 && previous.All(c => SentenceEndPunctuation.Contains(c) || OpeningPunctuation.Contains(c));
    }

    /// <summary>
    ///     Scores the senses of a lemma for a context feature and picks the best candidate.
    /// </summary>
    /// <param name="lemma">The trained lemma.</param>
    /// <param name="candidates">The senses the word can have.</param>
    /// <param name="feature">The context feature.</param>
    /// <returns>The best sense and its confidence, or null when the feature does not fit the weights.</returns>
    private static (WordDetails Details, double Confidence)? Pick(WeightedLemma lemma, WordDetails[] candidates, ContextFeature feature)
    {
        var weights = lemma.Weights;
        var scores = new float[weights.SenseCount];
        if (feature.Dense is { } dense && weights.FeatureIds is null && dense.Length == weights.Dimension)
        {
            weights.Scores(dense, scores);
        }
        else if (feature.Ids is { } ids && weights.FeatureIds is not null)
        {
            weights.ScoresSparse(ids, feature.Value, scores);
        }
        else
        {
            return null;
        }

        // Rank only the senses the word itself can have
        var allowed = lemma.Details.Select((d, i) => (Detail: d, Index: i)).Where(x => candidates.Contains(x.Detail)).ToList();
        if (allowed.Count == 0)
        {
            return null;
        }

        var (best, margin) = allowed.Count == lemma.Details.Length
            ? LemmaWeights.Rank(scores)
            : RankSubset(scores, allowed.Select(a => a.Index).ToList());
        var detail = lemma.Details[best];
        return (detail, LemmaWeights.Confidence(margin, weights.AbstainMargin));
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
