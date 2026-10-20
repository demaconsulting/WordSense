using System.Collections.Immutable;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Basic;

namespace WordSense.Builder;

/// <summary>
///     A tabled word of the basic model.
/// </summary>
/// <param name="Word">The lower-case key.</param>
/// <param name="ClosedMask">The tags the word may have regardless of the dictionary (0 for an open-class word).</param>
/// <param name="Counts">The training count of the word for each tag.</param>
internal sealed record BasicWordRow(string Word, int ClosedMask, int[] Counts);

/// <summary>
///     A tabled suffix of the basic model.
/// </summary>
/// <param name="Suffix">The suffix, one to three characters.</param>
/// <param name="Counts">The training counts of the open classes (noun, verb, adjective, adverb).</param>
internal sealed record BasicSuffixRow(string Suffix, int[] Counts);

/// <summary>
///     The cue table of one training lemma, ready to be written.
/// </summary>
/// <param name="Lemma">The training lemma that keys the table (the corpus lemma, which may be a form such as <c>left</c>).</param>
/// <param name="Analyses">The analyses in weight-column order: ordinal order of lemma, part of speech and features.</param>
/// <param name="Hashes">The ascending feature hashes.</param>
/// <param name="Weights">The quantized weights, feature-major.</param>
internal sealed record BasicCueRow(string Lemma, CueAnalysis[] Analyses, ushort[] Hashes, sbyte[] Weights);

/// <summary>
///     Everything the table of the basic model holds, before it is serialized.
/// </summary>
/// <param name="Hyperparameters">The hyper-parameters that were used (the runtime ones travel in the table header).</param>
/// <param name="Unigrams">The unigram tag counts.</param>
/// <param name="Bigrams">The bigram tag counts.</param>
/// <param name="Trigrams">The trigram tag counts.</param>
/// <param name="Words">The tabled words in ordinal order.</param>
/// <param name="Suffixes">The tabled suffixes in ordinal order.</param>
/// <param name="Cues">The cue tables in ordinal order of lemma.</param>
/// <param name="Collisions">The number of cue features dropped because their hash collided with a more frequent feature of the same lemma.</param>
/// <param name="SkippedAnchors">The number of anchor sentences left out of the cue tables because their label stands for several analyses.</param>
internal sealed record BasicModelData(
    BasicHyperparameters Hyperparameters,
    int[] Unigrams,
    int[] Bigrams,
    int[] Trigrams,
    IReadOnlyList<BasicWordRow> Words,
    IReadOnlyList<BasicSuffixRow> Suffixes,
    IReadOnlyList<BasicCueRow> Cues,
    int Collisions,
    int SkippedAnchors)
{
    /// <summary>
    ///     Gets the number of tabled words that are closed-class words.
    /// </summary>
    public int ClosedWords => Words.Count(w => w.ClosedMask != 0);

    /// <summary>
    ///     Gets the total number of cue features over all lemmas.
    /// </summary>
    public int CueFeatures => Cues.Sum(c => c.Hashes.Length);

    /// <summary>
    ///     Renders the sizes of the data as one line.
    /// </summary>
    /// <returns>The summary.</returns>
    public string Summary() =>
        $"{Words.Count} words ({ClosedWords} closed-class), {Suffixes.Count} suffixes, {Cues.Count} cue lemmas, {CueFeatures} cue features, {Collisions} hash collisions, {SkippedAnchors} anchors skipped (their label stands for several analyses)";
}

/// <summary>
///     Trains the basic model: the tabled words and suffixes from the Universal Dependencies counts, and the cue tables
///     from the anchor sentences, whose labels are rolled up to analyses. Every choice reads
///     <see cref="BasicHyperparameters"/>; the result depends only on the inputs, so equal inputs give an equal model.
/// </summary>
internal static class BasicTrainer
{
    private const int MaximumAnalyses = 255;

    /// <summary>
    ///     Trains the model.
    /// </summary>
    /// <param name="ud">The treebank counts.</param>
    /// <param name="lexicon">The lexicon of the core package: the analyses of a lower-case word, or the default array when unknown.</param>
    /// <param name="anchors">The training sentences of the cue model.</param>
    /// <param name="inventory">The sense inventory.</param>
    /// <param name="labels">The analyses that the corpus labels stand for.</param>
    /// <param name="hp">The hyper-parameters.</param>
    /// <returns>The model data.</returns>
    public static BasicModelData Train(
        UdCounts ud,
        Func<string, ImmutableArray<WordDetails>> lexicon,
        IReadOnlyList<CorpusRow> anchors,
        SenseInventory inventory,
        AnalysisLabels labels,
        BasicHyperparameters hp)
    {
        ArgumentNullException.ThrowIfNull(ud);
        ArgumentNullException.ThrowIfNull(lexicon);
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(hp);
        var closed = ud.Words.ToDictionary(p => p.Key, p => ClosedMaskOf(p.Value, hp), StringComparer.Ordinal);
        var words = SelectWords(ud, lexicon, closed, hp);
        var suffixes = SelectSuffixes(ud, lexicon, closed, hp);
        var (cues, collisions, skipped) = TrainCues(anchors, inventory, labels, hp);
        return new BasicModelData(hp, ud.Unigrams, ud.Bigrams, ud.Trigrams, words, suffixes, cues, collisions, skipped);
    }

    /// <summary>
    ///     Decides which tags of a word are closed-class tags.
    /// </summary>
    /// <param name="counts">The training counts of the word.</param>
    /// <param name="hp">The hyper-parameters.</param>
    /// <returns>The mask of the tags the word may have regardless of the dictionary, or 0.</returns>
    internal static int ClosedMaskOf(int[] counts, BasicHyperparameters hp)
    {
        var total = counts.Sum();
        if (total < hp.ClosedMinCount)
        {
            return 0;
        }

        var closedCount = PosTag.ClosedClasses.Sum(t => counts[t]);
        var mask = 0;
        if ((double)closedCount / total >= hp.ClosedShare)
        {
            for (var t = 0; t < PosTag.Count; t++)
            {
                mask |= counts[t] > 0 ? 1 << t : 0;
            }

            return mask;
        }

        foreach (var t in PosTag.ClosedClasses)
        {
            mask |= (double)counts[t] / total >= hp.ClosedAddShare ? 1 << t : 0;
        }

        return mask;
    }

    private static List<BasicWordRow> SelectWords(
        UdCounts ud,
        Func<string, ImmutableArray<WordDetails>> lexicon,
        Dictionary<string, int> closed,
        BasicHyperparameters hp)
    {
        var chosen = new SortedDictionary<string, BasicWordRow>(StringComparer.Ordinal);
        foreach (var (word, counts) in ud.Words)
        {
            if (closed[word] != 0)
            {
                chosen[word] = new BasicWordRow(word, closed[word], counts);
            }
        }

        var ambiguous = ud.Words
            .Where(p => closed[p.Key] == 0 && lexicon(p.Key) is { IsDefaultOrEmpty: false } senses && senses.Select(s => s.PartOfSpeech).Distinct().Count() >= 2)
            .OrderByDescending(p => p.Value.Sum())
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Take(hp.TopAmbiguousWords);
        foreach (var (word, counts) in ambiguous)
        {
            chosen[word] = new BasicWordRow(word, 0, counts);
        }

        return [.. chosen.Values];
    }

    private static List<BasicSuffixRow> SelectSuffixes(
        UdCounts ud,
        Func<string, ImmutableArray<WordDetails>> lexicon,
        Dictionary<string, int> closed,
        BasicHyperparameters hp)
    {
        var table = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var (word, counts) in ud.Words)
        {
            if (closed[word] != 0 || !lexicon(word).IsDefaultOrEmpty)
            {
                continue;
            }

            for (var length = 1; length <= Math.Min(3, word.Length); length++)
            {
                var suffix = word[^length..];
                if (!table.TryGetValue(suffix, out var row))
                {
                    row = new int[PosTag.OpenClasses.Length];
                    table[suffix] = row;
                }

                for (var j = 0; j < PosTag.OpenClasses.Length; j++)
                {
                    row[j] += counts[PosTag.OpenClasses[j]];
                }
            }
        }

        return [.. table.Where(p => p.Value.Sum() >= hp.SuffixMinTotal).Select(p => new BasicSuffixRow(p.Key, p.Value))];
    }

    private static (List<BasicCueRow> Cues, int Collisions, int Skipped) TrainCues(
        IReadOnlyList<CorpusRow> anchors,
        SenseInventory inventory,
        AnalysisLabels labels,
        BasicHyperparameters hp)
    {
        var cues = new List<BasicCueRow>();
        var collisions = 0;
        var skipped = 0;
        var features = new FeatureRef[CueModel.MaxFeatures];
        foreach (var group in anchors.GroupBy(a => a.Lemma, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            // The columns are the distinct analyses that the labels of the lemma stand for; a label that stands for
            // several analyses (a lemma_split class with several entries) names none of them
            var columns = ColumnsOf(group.Key, inventory, labels);
            if (columns.Length is < 2 or > MaximumAnalyses)
            {
                continue;
            }

            var columnOf = new Dictionary<AnalysisKey, int>();
            for (var c = 0; c < columns.Length; c++)
            {
                columnOf[columns[c]] = c;
            }

            var analysisCounts = new int[columns.Length];
            var featureCounts = new Dictionary<FeatureRef, int[]>();
            foreach (var row in group)
            {
                var stands = labels.Of(row.Lemma, row.Sense);
                if (stands.Count == 0)
                {
                    throw new InvalidDataException($"The anchor label '{row.Lemma}#{row.Sense}' has no analysis.");
                }

                if (stands.Count > 1)
                {
                    skipped++;
                    continue;
                }

                var column = columnOf[stands[0]];
                analysisCounts[column]++;
                var (start, length) = BasicClassifier.SegmentOf(row.Words, row.TargetWordIndex);
                var lower = new string[length];
                for (var i = 0; i < length; i++)
                {
                    lower[i] = BasicClassifier.Lower(row.Words[start + i]);
                }

                var count = CueModel.Extract(lower, row.TargetWordIndex - start, features);
                for (var f = 0; f < count; f++)
                {
                    if (!featureCounts.TryGetValue(features[f], out var perAnalysis))
                    {
                        perAnalysis = new int[columns.Length];
                        featureCounts[features[f]] = perAnalysis;
                    }

                    perAnalysis[column]++;
                }
            }

            var (cue, dropped) = BuildCue(group.Key, columns, analysisCounts, featureCounts, hp);
            cues.Add(cue);
            collisions += dropped;
        }

        return (cues, collisions, skipped);
    }

    /// <summary>
    ///     Lists the distinct analyses that the labels of a lemma stand for, in the column order of its cue table.
    /// </summary>
    /// <param name="lemma">The corpus lemma.</param>
    /// <param name="inventory">The sense inventory that lists the labels of the lemma.</param>
    /// <param name="labels">The analyses that the labels stand for.</param>
    /// <returns>The analyses in ordinal order of lemma, part of speech and features.</returns>
    private static AnalysisKey[] ColumnsOf(string lemma, SenseInventory inventory, AnalysisLabels labels) =>
        [.. inventory.RowsOf(lemma)
            .Select(r => labels.Of(r.Lemma, r.Label))
            .Where(stands => stands.Count == 1)
            .Select(stands => stands[0])
            .Distinct()
            .OrderBy(k => k.Lemma, StringComparer.Ordinal)
            .ThenBy(k => k.Pos)
            .ThenBy(k => k.Features, StringComparer.Ordinal)];

    private static (BasicCueRow Cue, int Dropped) BuildCue(
        string lemma,
        AnalysisKey[] columns,
        int[] analysisCounts,
        Dictionary<FeatureRef, int[]> featureCounts,
        BasicHyperparameters hp)
    {
        // Keep the features that occur often enough; of the features that share a 16-bit hash keep the most frequent
        var byHash = new SortedDictionary<ushort, (FeatureRef Feature, int[] Counts)>();
        var dropped = 0;
        foreach (var (feature, counts) in featureCounts.OrderBy(p => p.Key.Kind).ThenBy(p => p.Key.Token, StringComparer.Ordinal))
        {
            if (counts.Sum() < hp.CueMinFeatureCount)
            {
                continue;
            }

            var hash = feature.Hash;
            if (!byHash.TryGetValue(hash, out var existing))
            {
                byHash[hash] = (feature, counts);
                continue;
            }

            dropped++;
            if (counts.Sum() > existing.Counts.Sum())
            {
                byHash[hash] = (feature, counts);
            }
        }

        var analysisTotal = analysisCounts.Sum();
        var vocabulary = byHash.Count;
        var analyses = new CueAnalysis[columns.Length];
        var featureTotals = new int[columns.Length];
        foreach (var (_, counts) in byHash.Values)
        {
            for (var s = 0; s < columns.Length; s++)
            {
                featureTotals[s] += counts[s];
            }
        }

        for (var s = 0; s < columns.Length; s++)
        {
            var prior = Math.Log((analysisCounts[s] + 1.0) / (analysisTotal + columns.Length));
            analyses[s] = new CueAnalysis(columns[s].Lemma, columns[s].Pos, columns[s].Features, Quantize(prior));
        }

        var weights = new sbyte[vocabulary * columns.Length];
        var hashes = new ushort[vocabulary];
        var index = 0;
        foreach (var (hash, (_, counts)) in byHash)
        {
            hashes[index] = hash;
            for (var s = 0; s < columns.Length; s++)
            {
                var w = Math.Log((counts[s] + hp.CueAlpha) / (featureTotals[s] + (hp.CueAlpha * vocabulary)));
                weights[(index * columns.Length) + s] = Quantize(w);
            }

            index++;
        }

        return (new BasicCueRow(lemma, analyses, hashes, weights), dropped);
    }

    private static sbyte Quantize(double logValue) =>
        (sbyte)Math.Clamp(Math.Round(logValue / CueModel.Quantum), sbyte.MinValue, 0);
}
