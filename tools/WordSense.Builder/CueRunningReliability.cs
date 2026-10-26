using System.Collections.Immutable;
using System.IO.Compression;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     What the cues of one cue table did to the tokens of the training split that the table covers.
/// </summary>
/// <param name="Tokens">The number of scored tokens whose word has the table.</param>
/// <param name="Helped">The number of those tokens whose part of speech is right with the cues and wrong without them.</param>
/// <param name="Hurt">The number of those tokens whose part of speech is wrong with the cues and right without them.</param>
internal sealed record CueRunningStat(int Tokens, int Helped, int Hurt);

/// <summary>
///     Measures the net benefit of every cue table on running text and turns it into a reliability factor.
/// </summary>
/// <remarks>
///     <para>
///     The cue tables are trained from the anchors, a balanced corpus (a quota of sentences per sense), while a tagger has
///     to decide running text, where the analyses are far from balanced. A cross-validation over the anchors cannot see
///     that difference, so a table can predict its held-out anchors well and still outvote the tagger on running words.
///     This measurement uses the training split of the representative open corpus, which is running text with gold tags.
///     </para>
///     <para>
///     The split is cut into folds by a stable hash of the sentence. For each fold the tagger statistics (the n-grams, the
///     tabled words and the suffixes) are counted on the other folds, so the words of the fold are not in the counts that
///     score them (the emission would otherwise know the answer, and the cues could not add anything), and the fold is
///     tagged twice: with every cue table at full weight and with no cue table. A token that has a cue table is counted
///     as helped when only the run with the cues gets its part of speech right and as hurt when only the run without them
///     does. Nothing of the development or test splits is read.
///     </para>
///     <para>
///     The factor of a table is <c>min(1, (helped + pseudo) / (hurt + pseudo))</c>: a table that helps at least as often
///     as it hurts keeps its full weight, and the pseudo count keeps a table whose evidence is a few tokens near 1. It
///     is combined with the reliability that the anchors give (their product, or the smaller of the two).
///     </para>
/// </remarks>
internal static class CueRunningReliability
{
    /// <summary>
    ///     Measures the running-text benefit of every cue table that covers a token of the training split, and applies it.
    /// </summary>
    /// <param name="data">The trained model, with the reliabilities that the anchors give.</param>
    /// <param name="training">The training split of the representative open corpus with gold tags.</param>
    /// <param name="lexicon">The lexicon of the core package.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="runtime">How to make tables and classifiers (the shipped word image when null).</param>
    /// <returns>The model with the combined reliabilities; <paramref name="data"/> itself when the measurement is off.</returns>
    public static async Task<BasicModelData> ApplyAsync(
        BasicModelData data,
        IReadOnlyList<TaggedSentence> training,
        Func<string, ImmutableArray<WordDetails>> lexicon,
        CancellationToken cancellationToken,
        CueRunningRuntime? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Hyperparameters.RunningFolds < 2)
        {
            return data;
        }

        var stats = await MeasureAsync(data, training, lexicon, cancellationToken, runtime).ConfigureAwait(false);
        return Apply(data, stats);
    }

    /// <summary>
    ///     Measures what the cues of every cue table do on the training split, by cross-fitting.
    /// </summary>
    /// <param name="data">The trained model; its hyper-parameters give the number of folds.</param>
    /// <param name="training">The training split with gold tags.</param>
    /// <param name="lexicon">The lexicon of the core package.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="runtime">How to make tables and classifiers (the shipped word image when null).</param>
    /// <returns>The statistics by table key; a table that covers no token has none.</returns>
    public static async Task<IReadOnlyDictionary<string, CueRunningStat>> MeasureAsync(
        BasicModelData data,
        IReadOnlyList<TaggedSentence> training,
        Func<string, ImmutableArray<WordDetails>> lexicon,
        CancellationToken cancellationToken,
        CueRunningRuntime? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(training);
        ArgumentNullException.ThrowIfNull(lexicon);
        runtime ??= CueRunningRuntime.Shipped(lexicon);
        var folds = data.Hyperparameters.RunningFolds;
        var totals = new Dictionary<string, int[]>(StringComparer.Ordinal);
        if (folds < 2 || data.Cues.Count == 0)
        {
            return new Dictionary<string, CueRunningStat>();
        }

        var foldOf = new int[training.Count];
        for (var i = 0; i < training.Count; i++)
        {
            foldOf[i] = BasicTuner.StableHash(string.Join(' ', training[i].Words)) % folds;
        }

        var fullWeight = data.Cues.Select(c => c with { Reliability = 255 }).ToList();
        for (var fold = 0; fold < folds; fold++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var counts = TaggerCounts.Count(training.Where((_, i) => foldOf[i] != fold), lexicon);
            var tagger = BasicTrainer.WithCounts(data, counts, lexicon);
            var withCues = runtime.Load(tagger with { Cues = fullWeight });
            var without = runtime.Load(tagger with { Cues = [] });
            var onClassifier = runtime.Classifier(withCues);
            var offClassifier = runtime.Classifier(without);
            for (var s = 0; s < training.Count; s++)
            {
                if (foldOf[s] != fold)
                {
                    continue;
                }

                var sentence = training[s];
                var on = await onClassifier.ClassifyAsync(sentence.Words, cancellationToken).ConfigureAwait(false);
                var off = await offClassifier.ClassifyAsync(sentence.Words, cancellationToken).ConfigureAwait(false);
                for (var i = 0; i < sentence.Words.Length; i++)
                {
                    Tally(totals, withCues, lexicon, sentence, i, on[i], off[i]);
                }
            }
        }

        return totals.ToDictionary(p => p.Key, p => new CueRunningStat(p.Value[0], p.Value[1], p.Value[2]), StringComparer.Ordinal);
    }

    /// <summary>
    ///     Applies measured statistics to the cue tables: the final reliability of each table.
    /// </summary>
    /// <param name="data">The trained model, with the reliabilities that the anchors give.</param>
    /// <param name="stats">The statistics by table key.</param>
    /// <returns>The model whose cue rows carry the statistics and the combined reliability.</returns>
    public static BasicModelData Apply(BasicModelData data, IReadOnlyDictionary<string, CueRunningStat> stats)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(stats);
        var hp = data.Hyperparameters;
        var cues = new List<BasicCueRow>(data.Cues.Count);
        foreach (var cue in data.Cues)
        {
            if (!stats.TryGetValue(cue.Lemma, out var stat))
            {
                cues.Add(cue with { Reliability = cue.AnchorReliability });
                continue;
            }

            var factor = FactorOf(stat.Helped, stat.Hurt, hp.RunningPseudo);
            cues.Add(cue with
            {
                Reliability = Combine(cue.AnchorReliability, factor, hp.RunningMinimum),
                RunningTokens = stat.Tokens,
                RunningHelped = stat.Helped,
                RunningHurt = stat.Hurt,
            });
        }

        return data with { Cues = cues };
    }

    /// <summary>
    ///     Turns the tokens a table helped and hurt into its running-text factor.
    /// </summary>
    /// <param name="helped">The tokens the cues turned from wrong to right.</param>
    /// <param name="hurt">The tokens the cues turned from right to wrong.</param>
    /// <param name="pseudo">The pseudo count added to both.</param>
    /// <returns>1 when the table helped at least as often as it hurt, otherwise <c>(helped + pseudo) / (hurt + pseudo)</c>.</returns>
    internal static double FactorOf(int helped, int hurt, double pseudo) =>
        hurt <= helped ? 1.0 : (helped + pseudo) / (hurt + pseudo);

    /// <summary>
    ///     Combines the reliability that the anchors give with the running-text factor.
    /// </summary>
    /// <param name="anchor">The reliability from the anchors, 0 to 255.</param>
    /// <param name="factor">The running-text factor, 0 to 1.</param>
    /// <param name="minimum">True for the smaller of the two, false for their product.</param>
    /// <returns>The reliability, 0 to 255.</returns>
    internal static byte Combine(byte anchor, double factor, bool minimum)
    {
        var fromAnchors = anchor / 255.0;
        var combined = minimum ? Math.Min(fromAnchors, factor) : fromAnchors * factor;
        return (byte)Math.Round(Math.Clamp(combined, 0.0, 1.0) * 255);
    }

    private static void Tally(
        Dictionary<string, int[]> totals,
        BasicTables withCues,
        Func<string, ImmutableArray<WordDetails>> lexicon,
        TaggedSentence sentence,
        int index,
        ClassifyResult on,
        ClassifyResult off)
    {
        var gold = sentence.Tags[index];
        if (gold is PosTag.Punctuation or PosTag.Other || on.Details is not { } withDetails || off.Details is not { } withoutDetails)
        {
            return;
        }

        var key = BasicClassifier.Normalize(sentence.Words[index]);
        if (withCues.FindCue(key, lexicon(key)) is not { } cue)
        {
            return;
        }

        gold = gold == PosTag.InfinitiveTo ? PosTag.Preposition : gold;
        var rightWith = PosTag.Of(withDetails.PartOfSpeech) == gold;
        var rightWithout = PosTag.Of(withoutDetails.PartOfSpeech) == gold;
        if (!totals.TryGetValue(cue.Lemma, out var row))
        {
            row = new int[3];
            totals[cue.Lemma] = row;
        }

        row[0]++;
        row[1] += rightWith && !rightWithout ? 1 : 0;
        row[2] += rightWithout && !rightWith ? 1 : 0;
    }
}

/// <summary>
///     How the running-text measurement turns a model into tables and classifiers: the shipped word image, or a toy
///     inventory in the tests.
/// </summary>
/// <param name="Load">Serializes and reads back the tables of a model.</param>
/// <param name="Classifier">Makes the classifier of tables.</param>
internal sealed record CueRunningRuntime(Func<BasicModelData, BasicTables> Load, Func<BasicTables, BasicClassifier> Classifier)
{
    /// <summary>
    ///     The runtime of the shipped word image.
    /// </summary>
    /// <param name="lexicon">The lexicon of the core package.</param>
    /// <returns>The runtime.</returns>
    public static CueRunningRuntime Shipped(Func<string, ImmutableArray<WordDetails>> lexicon) => new(
        data => BasicTables.Load(BasicTablesWriter.Write(data, WordLexicon.Current.InventoryHash, CompressionLevel.Fastest), WordLexicon.Current),
        tables => new BasicClassifier(tables, lexicon, (lemma, pos) => WordLexicon.Current.SenseCountOf(lemma, pos), WordLexicon.SensesOf));
}
