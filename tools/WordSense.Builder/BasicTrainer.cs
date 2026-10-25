using System.Collections.Immutable;
using DemaConsulting.WordSense;

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
///     The shared tense table of the basic model, ready to be written.
/// </summary>
/// <param name="Prior">The quantized log odds of past against non-past in the training verbs, in steps of 1/8 nat.</param>
/// <param name="Hashes">The ascending feature hashes.</param>
/// <param name="Weights">The quantized log likelihood ratios of past against non-past, parallel to <paramref name="Hashes"/>.</param>
/// <param name="Past">The number of past training verbs.</param>
/// <param name="NonPast">The number of non-past training verbs.</param>
/// <param name="Collisions">The number of features dropped because their hash collided with a more frequent feature.</param>
internal sealed record BasicTenseRow(sbyte Prior, ushort[] Hashes, sbyte[] Weights, int Past, int NonPast, int Collisions);

/// <summary>
///     Everything the table of the basic model holds, before it is serialized.
/// </summary>
/// <param name="Hyperparameters">The hyper-parameters that were used (the runtime ones travel in the table header).</param>
/// <param name="Unigrams">The unigram tag counts.</param>
/// <param name="Bigrams">The bigram tag counts.</param>
/// <param name="Trigrams">The trigram tag counts.</param>
/// <param name="ClauseBigrams">The bigram tag counts of the positions whose clause already has a verb.</param>
/// <param name="ClauseTrigrams">The trigram tag counts of the positions whose clause already has a verb.</param>
/// <param name="Words">The tabled words in ordinal order.</param>
/// <param name="Suffixes">The tabled suffixes in ordinal order.</param>
/// <param name="Cues">The cue tables in ordinal order of key (a lemma, or a form that a chooser row decides).</param>
/// <param name="Collisions">The number of cue features dropped because their hash collided with a more frequent feature of the same table.</param>
/// <param name="Skipped">The number of training sentences left out of the cue tables because their label stands for no single analysis.</param>
/// <param name="Tense">The shared tense table, or null when the model was trained without verbs.</param>
internal sealed record BasicModelData(
    BasicHyperparameters Hyperparameters,
    int[] Unigrams,
    int[] Bigrams,
    int[] Trigrams,
    int[] ClauseBigrams,
    int[] ClauseTrigrams,
    IReadOnlyList<BasicWordRow> Words,
    IReadOnlyList<BasicSuffixRow> Suffixes,
    IReadOnlyList<BasicCueRow> Cues,
    int Collisions,
    int Skipped,
    BasicTenseRow? Tense = null)
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
        $"{Words.Count} words ({ClosedWords} closed-class), {Suffixes.Count} suffixes, {Cues.Count} cue tables, {CueFeatures} cue features, {Collisions} hash collisions, {Skipped} cue sentences skipped (their label stands for no single analysis)"
        + (Tense is { } t ? $", a tense table of {t.Hashes.Length} features from {t.Past} past and {t.NonPast} non-past verbs ({t.Collisions} hash collisions)" : string.Empty);
}

/// <summary>
///     Trains the basic model: the tabled words, suffixes and tag n-grams from the open-corpus counts (usage statistics
///     of the representative part), and the cue tables from the fine-sense anchors, whose senses are rolled up to
///     analyses (lemma and part of speech), and from the form-chooser anchors, whose labels name the lemma or the
///     tense of a form. Every choice reads <see cref="BasicHyperparameters"/>; the result depends only on the inputs, so
///     equal inputs give an equal model.
/// </summary>
/// <remarks>
///     <para>
///     A lemma that the anchors give two or more analyses gets a cue table keyed by the lemma; a form that the chooser
///     anchors give two or more analyses (<c>left</c> as a form of <c>leave</c> or of the adjective <c>left</c>, <c>read</c> as
///     a past or a present verb) gets a table keyed by the form, which the classifier finds before the table of the
///     lemma. When a key has both, the table holds both kinds of sentences: a sense sentence goes to the column of its
///     lemma and part of speech, unless the chooser sentences split that slot by features, in which case it is left out.
///     </para>
///     <para>
///     The anchors are a balanced classifier corpus (a quota of sentences per sense), so the number of anchors of an
///     analysis says nothing about how often the analysis occurs: the cue tables carry no analysis prior at all (every
///     column has the same one), and the part-of-speech frequencies come from the tagger counts alone.
///     </para>
/// </remarks>
internal static class BasicTrainer
{
    private const int MaximumAnalyses = 255;

    /// <summary>
    ///     One training sentence of the cue model: the table it trains, the analysis it stands for and its words.
    /// </summary>
    /// <param name="Table">The key of the cue table: a lemma or a form.</param>
    /// <param name="Column">The analysis the sentence stands for.</param>
    /// <param name="Words">The words of the sentence.</param>
    /// <param name="Target">The index of the target word.</param>
    /// <param name="FromChooser">True when the sentence is a form-chooser row.</param>
    private sealed record CueSample(string Table, AnalysisKey Column, IReadOnlyList<string> Words, int Target, bool FromChooser);

    /// <summary>
    ///     Trains the model.
    /// </summary>
    /// <param name="counts">The tag counts of the representative training split.</param>
    /// <param name="lexicon">The lexicon of the core package: the analyses of a lower-case word, or the default array when unknown.</param>
    /// <param name="anchors">The training sentences of the cue model (fine-sense rows).</param>
    /// <param name="choosers">The training sentences of the form choosers (form, lemma or tense label).</param>
    /// <param name="hp">The hyper-parameters.</param>
    /// <param name="verbs">The sentences whose verbs train the shared tense table (the training split of the representative open corpus, with the features of its tokens), or null to train a model without a tense table.</param>
    /// <returns>The model data.</returns>
    public static BasicModelData Train(
        TaggerCounts counts,
        Func<string, ImmutableArray<WordDetails>> lexicon,
        IReadOnlyList<CorpusRow> anchors,
        IReadOnlyList<ChooserRow> choosers,
        BasicHyperparameters hp,
        IEnumerable<TaggedSentence>? verbs = null)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(lexicon);
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(choosers);
        ArgumentNullException.ThrowIfNull(hp);
        var closed = counts.Words.ToDictionary(p => p.Key, p => ClosedMaskOf(p.Value, hp), StringComparer.Ordinal);
        var words = SelectWords(counts, lexicon, closed, hp);
        var suffixes = SelectSuffixes(counts, lexicon, closed, hp);
        var (cues, collisions, skipped) = TrainCues(anchors, choosers, hp);
        var tense = verbs is null ? null : TrainTense(verbs, hp);
        return new BasicModelData(hp, counts.Unigrams, counts.Bigrams, counts.Trigrams, counts.ClauseBigrams, counts.ClauseTrigrams, words, suffixes, cues, collisions, skipped, tense);
    }

    /// <summary>
    ///     Gets the tense class of a verb token of a gold source: the class the shared tense table learns.
    /// </summary>
    /// <param name="gold">What the source says about the token (its features in the notation of the open corpus, <c>Tense=Past|VerbForm=Part</c>).</param>
    /// <returns>1 for a past verb (<c>Tense=Past</c>: the simple past and the participle), 0 for a verb in a form that can share its spelling with a past form (the infinitive, the imperative and the present tense of every person but the third singular), and -1 for every other form (the third person singular of the present, which is spelled apart from the past; the gerund and the present participle; a token without features).</returns>
    internal static int TenseClassOf(GoldToken gold)
    {
        var items = gold.Features.Split('|');
        if (items.Contains("Tense=Past", StringComparer.Ordinal))
        {
            return 1;
        }

        var present = items.Contains("Tense=Pres", StringComparer.Ordinal);
        if (items.Contains("VerbForm=Ger", StringComparer.Ordinal) || (present && items.Contains("VerbForm=Part", StringComparer.Ordinal)))
        {
            return -1;
        }

        // The third person singular is spelled apart from the past (puts, cuts), so it would only teach the model that a
        // singular subject means non-past, which is false for the forms that can be confused
        if (present && items.Contains("Person=3", StringComparer.Ordinal) && items.Contains("Number=Sing", StringComparer.Ordinal))
        {
            return -1;
        }

        return items.Contains("VerbForm=Inf", StringComparer.Ordinal) || items.Contains("Mood=Imp", StringComparer.Ordinal) || (present && items.Contains("VerbForm=Fin", StringComparer.Ordinal))
            ? 0
            : -1;
    }

    /// <summary>
    ///     Trains the shared tense table: a naive Bayes model of past against non-past over the context features of every
    ///     verb of the sentences, whatever the verb. It reads no word list and no lemma, only the context.
    /// </summary>
    /// <param name="sentences">The sentences with the features of their tokens (a sentence without them is skipped).</param>
    /// <param name="hp">The hyper-parameters.</param>
    /// <returns>The table, or null when the sentences have no verb of one of the classes or no feature occurs often enough.</returns>
    internal static BasicTenseRow? TrainTense(IEnumerable<TaggedSentence> sentences, BasicHyperparameters hp)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        ArgumentNullException.ThrowIfNull(hp);
        var features = new FeatureRef[CueModel.MaxTenseFeatures];
        var featureCounts = new Dictionary<FeatureRef, int[]>();
        var classes = new int[2];
        foreach (var sentence in sentences)
        {
            if (sentence.Gold is not { } gold)
            {
                continue;
            }

            for (var i = 0; i < sentence.Words.Length; i++)
            {
                var cls = sentence.Tags[i] == PosTag.Verb ? TenseClassOf(gold[i]) : -1;
                if (cls < 0)
                {
                    continue;
                }

                // The context is the sentence segment around the verb, as the classifier reads it
                var (start, length) = BasicClassifier.SegmentOf(sentence.Words, i);
                var lower = new string[length];
                for (var k = 0; k < length; k++)
                {
                    lower[k] = BasicClassifier.Lower(sentence.Words[start + k]);
                }

                classes[cls]++;
                var count = CueModel.ExtractTense(lower, i - start, features);
                for (var f = 0; f < count; f++)
                {
                    if (!featureCounts.TryGetValue(features[f], out var perClass))
                    {
                        perClass = new int[2];
                        featureCounts[features[f]] = perClass;
                    }

                    perClass[cls]++;
                }
            }
        }

        return classes[0] == 0 || classes[1] == 0 ? null : BuildTense(featureCounts, classes, hp);
    }

    private static BasicTenseRow? BuildTense(Dictionary<FeatureRef, int[]> featureCounts, int[] classes, BasicHyperparameters hp)
    {
        // Keep the features that occur often enough; of the features that share a 16-bit hash keep the most frequent
        var byHash = new SortedDictionary<ushort, int[]>();
        var dropped = 0;
        foreach (var (feature, counts) in featureCounts.OrderBy(p => p.Key.Kind).ThenBy(p => p.Key.Token, StringComparer.Ordinal))
        {
            if (counts.Sum() < hp.TenseMinFeatureCount)
            {
                continue;
            }

            var hash = feature.Hash;
            if (!byHash.TryGetValue(hash, out var existing))
            {
                byHash[hash] = counts;
                continue;
            }

            dropped++;
            if (counts.Sum() > existing.Sum())
            {
                byHash[hash] = counts;
            }
        }

        var vocabulary = byHash.Count;
        if (vocabulary == 0)
        {
            return null;
        }

        var totals = new double[2];
        foreach (var counts in byHash.Values)
        {
            totals[0] += counts[0];
            totals[1] += counts[1];
        }

        var hashes = new ushort[vocabulary];
        var weights = new sbyte[vocabulary];
        var index = 0;
        foreach (var (hash, counts) in byHash)
        {
            // The log likelihood ratio of past against non-past: the one number per feature that the runtime sums
            var past = Math.Log((counts[1] + hp.TenseAlpha) / (totals[1] + (hp.TenseAlpha * vocabulary)));
            var other = Math.Log((counts[0] + hp.TenseAlpha) / (totals[0] + (hp.TenseAlpha * vocabulary)));
            hashes[index] = hash;
            weights[index] = QuantizeSigned(past - other);
            index++;
        }

        // The natural prior of the training text: the share of past verbs among the verbs that can be confused
        return new BasicTenseRow(QuantizeSigned(Math.Log((double)classes[1] / classes[0])), hashes, weights, classes[1], classes[0], dropped);
    }

    private static sbyte QuantizeSigned(double logValue) =>
        (sbyte)Math.Clamp(Math.Round(logValue / CueModel.Quantum), sbyte.MinValue, sbyte.MaxValue);

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
        TaggerCounts tagged,
        Func<string, ImmutableArray<WordDetails>> lexicon,
        Dictionary<string, int> closed,
        BasicHyperparameters hp)
    {
        var chosen = new SortedDictionary<string, BasicWordRow>(StringComparer.Ordinal);
        foreach (var (word, counts) in tagged.Words)
        {
            if (closed[word] != 0)
            {
                chosen[word] = new BasicWordRow(word, closed[word], counts);
            }
        }

        var ambiguous = tagged.Words
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
        TaggerCounts tagged,
        Func<string, ImmutableArray<WordDetails>> lexicon,
        Dictionary<string, int> closed,
        BasicHyperparameters hp)
    {
        var table = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var (word, counts) in tagged.Words)
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
        IReadOnlyList<ChooserRow> choosers,
        BasicHyperparameters hp)
    {
        var cues = new List<BasicCueRow>();
        var collisions = 0;
        var skipped = 0;
        var features = new FeatureRef[CueModel.MaxFeatures];

        // Every training sentence belongs to one table (a lemma or a form) and stands for one analysis
        var samples = new List<CueSample>(anchors.Count + choosers.Count);
        samples.AddRange(anchors.Select(a => new CueSample(a.Lemma, KeyOf(a), a.Words, a.TargetWordIndex, false)));
        foreach (var row in choosers)
        {
            if (KeyOf(row) is { } key)
            {
                samples.Add(new CueSample(row.Form, key, row.Words, row.TargetWordIndex, true));
            }
            else
            {
                skipped++;
            }
        }

        foreach (var group in samples.GroupBy(s => s.Table, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            // The columns are the distinct analyses that the sentences of the table stand for
            var rows = Reconcile(group.ToList(), ref skipped);
            var columns = ColumnsOf(rows);
            if (columns.Length is < 2 or > MaximumAnalyses)
            {
                continue;
            }

            var columnOf = new Dictionary<AnalysisKey, int>();
            for (var c = 0; c < columns.Length; c++)
            {
                columnOf[columns[c]] = c;
            }

            var featureCounts = new Dictionary<FeatureRef, int[]>();
            foreach (var row in rows)
            {
                var column = columnOf[row.Column];
                var (start, length) = BasicClassifier.SegmentOf(row.Words, row.Target);
                var lower = new string[length];
                for (var i = 0; i < length; i++)
                {
                    lower[i] = BasicClassifier.Lower(row.Words[start + i]);
                }

                var count = CueModel.Extract(lower, row.Target - start, features);
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

            var (cue, dropped) = BuildCue(group.Key, columns, featureCounts, hp);
            cues.Add(cue);
            collisions += dropped;
        }

        return (cues, collisions, skipped);
    }

    /// <summary>
    ///     Makes the sense sentences and the chooser sentences of one table agree on the columns. The chooser labels are the
    ///     finer ones (they may tell the tenses of a verb apart), so a sense sentence goes to the one chooser column of its
    ///     lemma and part of speech; when the chooser columns split that slot it cannot say which one, and the sentence is
    ///     left out; when no chooser column has the slot it keeps its own column.
    /// </summary>
    /// <param name="rows">The sentences of one table.</param>
    /// <param name="skipped">Incremented for each sentence that is left out.</param>
    /// <returns>The sentences with their final columns.</returns>
    private static List<CueSample> Reconcile(List<CueSample> rows, ref int skipped)
    {
        var chooserColumns = rows.Where(r => r.FromChooser).Select(r => r.Column).Distinct().ToList();
        if (chooserColumns.Count == 0)
        {
            return rows;
        }

        var result = new List<CueSample>(rows.Count);
        foreach (var row in rows)
        {
            if (row.FromChooser)
            {
                result.Add(row);
                continue;
            }

            var slot = chooserColumns.Where(c => string.Equals(c.Lemma, row.Column.Lemma, StringComparison.Ordinal) && c.Pos == row.Column.Pos).ToList();
            switch (slot.Count)
            {
                case 0:
                    result.Add(row);
                    break;
                case 1:
                    result.Add(row with { Column = slot[0] });
                    break;
                default:
                    skipped++;
                    break;
            }
        }

        return result;
    }

    /// <summary>
    ///     Gets the analysis that a fine-sense row stands for: the lemma and the part of speech of its sense, with no
    ///     required features (the senses belong to a lemma and part of speech, not to its forms).
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The analysis key.</returns>
    internal static AnalysisKey KeyOf(CorpusRow row) => AnalysisKey.Resolve(new AnalysisKey(row.Lemma, row.PartOfSpeech, string.Empty));

    /// <summary>
    ///     Gets the analysis that a form-chooser row stands for: for a label <c>lemma/features</c> the analysis of the form
    ///     with exactly those features, for a label that is a lemma its lemma and part of speech with no required features.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The analysis key, or null when the label stands for the analyses of more than one lemma and part of speech, or for none.</returns>
    internal static AnalysisKey? KeyOf(ChooserRow row)
    {
        var analyses = ChooserCorpus.AnalysesOf(row.Form);
        if (row.Label.Contains('/', StringComparison.Ordinal))
        {
            return analyses.FirstOrDefault(a => string.Equals(a.Lemma + "/" + a.Features, row.Label, StringComparison.Ordinal)) is { } match
                ? new AnalysisKey(match.Lemma, match.PartOfSpeech, match.Features)
                : null;
        }

        var slots = analyses.Where(a => string.Equals(a.Lemma, row.Label, StringComparison.Ordinal)).Select(a => (a.Lemma, a.PartOfSpeech)).Distinct().ToList();
        return slots.Count == 1 ? new AnalysisKey(slots[0].Lemma, slots[0].PartOfSpeech, string.Empty) : null;
    }

    /// <summary>
    ///     Lists the distinct analyses that the sentences of a table stand for, in the column order of the table.
    /// </summary>
    /// <param name="rows">The sentences of the table.</param>
    /// <returns>The analyses in ordinal order of lemma, part of speech and features.</returns>
    private static AnalysisKey[] ColumnsOf(IEnumerable<CueSample> rows) =>
        [.. rows
            .Select(r => r.Column)
            .Distinct()
            .OrderBy(k => k.Lemma, StringComparer.Ordinal)
            .ThenBy(k => k.Pos)
            .ThenBy(k => k.Features, StringComparer.Ordinal)];

    private static (BasicCueRow Cue, int Dropped) BuildCue(
        string lemma,
        AnalysisKey[] columns,
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

        // The anchors are a balanced classifier corpus, so their counts carry no usage frequency: every column gets the
        // same prior, which cancels in the fusion
        var prior = Quantize(Math.Log(1.0 / columns.Length));
        for (var s = 0; s < columns.Length; s++)
        {
            analyses[s] = new CueAnalysis(columns[s].Lemma, columns[s].Pos, columns[s].Features, prior);
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
