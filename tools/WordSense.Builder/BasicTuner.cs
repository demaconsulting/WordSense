using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using DemaConsulting.WordSense;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     Tries the hyper-parameter grid of <see cref="BasicHyperparameters"/> one parameter at a time and prints how each
///     variant scores. It reads only data that is safe to tune against: the development split of the representative part
///     of the open corpus and a deterministic sample of the fine-sense anchors split into a train and a dev part. It never
///     reads the test splits, the targeted challenge set, the holdout or the real-text sentences, and it writes no file.
///     The form-chooser sentences are left out of the tuned tables: they are the same sentences as the anchors, labeled a
///     second way, so they would leak the anchor development sentences into the tables.
/// </summary>
internal static class BasicTuner
{
    /// <summary>
    ///     Runs the tuning.
    /// </summary>
    /// <param name="corpus">The corpus directory (the open corpus with its tag counts, and the fine-sense anchors).</param>
    /// <param name="progress">Receives progress messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="treebankDev">The gold file of the development file of the treebank (<c>ud_dev.tsv</c>) that the tense weight is tuned with besides the open corpus development split, or null; a missing file is left out.</param>
    /// <returns>The rendered Markdown table.</returns>
    public static async Task<string> RunAsync(string corpus, TextWriter progress, CancellationToken cancellationToken, string? treebankDev = null)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var counts = TaggerCounts.Read(OpenCorpus.CountsDirectory(corpus));
        var dev = OpenCorpus.ReadSplit(corpus, OpenCorpus.Representative, "dev").Select(OpenCorpus.ToTagged).ToList();
        var lexicon = (Func<string, ImmutableArray<WordDetails>>)(key => WordLexicon.TryLookup(key, out var found) ? found : []);
        var (train, anchorDev) = SplitAnchors(Corpus.ReadDirectory(Path.Join(corpus, "anchors")));
        progress.WriteLine($"Tuning on {dev.Count} open-corpus dev sentences, {train.Count} anchor train and {anchorDev.Count} anchor dev sentences.");

        var text = new StringBuilder();
        text.Append("| Variant | Open dev ambiguous POS | Anchor dev analysis | Table size |\n");
        text.Append("| --- | ---: | ---: | ---: |\n");
        var seen = new HashSet<BasicHyperparameters>();
        var variants = new List<(string Label, BasicHyperparameters Parameters)> { ("default", BasicHyperparameters.Default) };
        seen.Add(BasicHyperparameters.Default);
        foreach (var (name, grid) in BasicHyperparameters.Grid)
        {
            foreach (var variant in grid.Where(seen.Add))
            {
                variants.Add(($"{name}: {Describe(variant)}", variant));
            }
        }

        foreach (var (label, hp) in variants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = BasicTrainer.Train(counts, lexicon, train, [], hp);
            var blob = BasicTablesWriter.Write(data, WordLexicon.Current.InventoryHash, CompressionLevel.Fastest);
            var tables = BasicTables.Load(blob, WordLexicon.Current);
            var (ambiguous, ambiguousCorrect) = ScoreTagger(tables, dev, lexicon);
            var (anchorRows, anchorCorrect) = await ScoreAnchorsAsync(tables, anchorDev, lexicon, cancellationToken).ConfigureAwait(false);
            text.Append("| ").Append(label)
                .Append(" | ").Append(Percent(ambiguousCorrect, ambiguous))
                .Append(" | ").Append(Percent(anchorCorrect, anchorRows))
                .Append(" | ").Append((blob.Length / 1024.0).ToString("N0", CultureInfo.InvariantCulture)).Append(" KiB |\n");
            progress.WriteLine($"{label} done.");
        }

        return text.ToString() + await TuneTenseAsync(corpus, counts, lexicon, dev, treebankDev, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Scores the weight of the tense model on the development sets: the model of the shipped configuration (all anchors
    ///     and form choosers, the verbs of the open corpus training split) with each weight of the grid.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="counts">The tag counts.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="dev">The development split of the representative open corpus.</param>
    /// <param name="treebankDev">The gold file of the development file of the treebank, or null; a file that does not exist is left out.</param>
    /// <param name="progress">Receives progress messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rendered Markdown table of the weights; only development sets are read, never a test set.</returns>
    private static async Task<string> TuneTenseAsync(
        string corpus,
        TaggerCounts counts,
        Func<string, ImmutableArray<WordDetails>> lexicon,
        List<TaggedSentence> dev,
        string? treebankDev,
        TextWriter progress,
        CancellationToken cancellationToken)
    {
        var sets = new List<(string Name, IReadOnlyList<TaggedSentence> Sentences)> { (TenseEvaluator.OpenDevName, dev) };
        if (treebankDev is not null && File.Exists(treebankDev) && TaggerEvaluator.ReadGold(treebankDev) is { Count: > 0 } gold && gold[0].Gold is not null)
        {
            sets.Add((TenseEvaluator.TreebankDevName, gold));
        }

        var anchors = Corpus.ReadDirectory(Path.Join(corpus, "anchors"));
        var choosers = ChooserCorpus.ReadDirectory(Path.Join(corpus, "choosers", "anchors"));
        var verbs = OpenCorpus.ReadSplit(corpus, OpenCorpus.Representative, "train").Select(OpenCorpus.ToTagged).ToList();
        var full = BasicTrainer.Train(counts, lexicon, anchors, choosers, BasicHyperparameters.Default, verbs);
        var text = new StringBuilder();
        text.Append("\n| Tense weight | Set | Same-spelling verbs | Accuracy | Read family |\n");
        text.Append("| ---: | --- | ---: | ---: | ---: |\n");
        foreach (var weight in BasicHyperparameters.TenseWeightGrid)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = full with { Hyperparameters = full.Hyperparameters with { TenseWeight = weight } };
            var tables = BasicTables.Load(BasicTablesWriter.Write(data, WordLexicon.Current.InventoryHash, CompressionLevel.Fastest), WordLexicon.Current);
            foreach (var (name, sentences) in sets)
            {
                var scores = await TenseEvaluator.ScoreBasicAsync(tables, sentences, name).ConfigureAwait(false);
                var all = scores.First(s => s.Group == TenseEvaluator.AllGroup);
                var read = scores.FirstOrDefault(s => s.Group == TenseEvaluator.ReadGroup);
                text.Append("| ").Append(weight.ToString("0.00", CultureInfo.InvariantCulture)).Append(" | ").Append(name)
                    .Append(" | ").Append(all.Tokens.ToString(CultureInfo.InvariantCulture))
                    .Append(" | ").Append(Percent(all.Correct, all.Tokens))
                    .Append(" | ").Append(read is null ? "n/a" : $"{Percent(read.Correct, read.Tokens)} of {read.Tokens.ToString(CultureInfo.InvariantCulture)}").Append(" |\n");
            }

            progress.WriteLine($"tense weight {weight.ToString("0.00", CultureInfo.InvariantCulture)} done.");
        }

        return text.ToString();
    }

    /// <summary>
    ///     Splits a deterministic sample of the anchors into a train and a dev part.
    /// </summary>
    /// <param name="anchors">All anchor sentences.</param>
    /// <returns>The train and the dev rows; a sentence is a dev row when a hash of its text modulo <see cref="BasicHyperparameters.AnchorDevModulus"/> is zero.</returns>
    internal static (List<CorpusRow> Train, List<CorpusRow> Dev) SplitAnchors(IReadOnlyList<CorpusRow> anchors)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        var order = Enumerable.Range(0, anchors.Count).ToArray();
        // A seeded generator is required here: the split must be reproducible, and nothing is security-sensitive
#pragma warning disable S2245
        var random = new Random(BasicHyperparameters.AnchorSeed);
#pragma warning restore S2245
        for (var i = order.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var train = new List<CorpusRow>();
        var dev = new List<CorpusRow>();
        foreach (var index in order.Take(BasicHyperparameters.AnchorSample).Order())
        {
            var row = anchors[index];
            (StableHash(row.Sentence) % BasicHyperparameters.AnchorDevModulus == 0 ? dev : train).Add(row);
        }

        return (train, dev);
    }

    /// <summary>
    ///     Gets a hash of a text that does not change between runs or platforms (FNV-1a over the UTF-16 code units).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A non-negative hash.</returns>
    internal static int StableHash(string text)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var ch in text)
            {
                h = (h ^ ch) * 16777619u;
            }

            return (int)(h & 0x7FFFFFFF);
        }
    }

    /// <summary>
    ///     Describes the hyper-parameters of a variant in one line.
    /// </summary>
    /// <param name="hp">The hyper-parameters.</param>
    /// <returns>The description.</returns>
    internal static string Describe(BasicHyperparameters hp) => string.Create(
        CultureInfo.InvariantCulture,
        $"gamma {hp.Gamma}, beta {hp.Beta}, closed {hp.ClosedMinCount}/{hp.ClosedShare}, hmm weight {hp.HmmWeight}, temperature {hp.Temperature}, cue alpha {hp.CueAlpha}");

    /// <summary>
    ///     Scores the tagger alone on sentences: every token that has several parts of speech in the lexicon, among which
    ///     the gold tag, counts, and it is right when the most probable tag of the tagger is the gold tag.
    /// </summary>
    /// <param name="tables">The statistics.</param>
    /// <param name="sentences">The gold sentences.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <returns>The number of scored tokens and the number of right ones.</returns>
    internal static (int Rows, int Correct) ScoreTagger(BasicTables tables, IReadOnlyList<TaggedSentence> sentences, Func<string, ImmutableArray<WordDetails>> lexicon)
    {
        var rows = 0;
        var correct = 0;
        foreach (var sentence in sentences)
        {
            var n = sentence.Words.Length;
            var tokens = new TaggerToken[n];
            for (var i = 0; i < n; i++)
            {
                tokens[i] = BasicClassifier.MakeToken(lexicon, sentence.Words[i]);
            }

            var posteriors = new double[n * PosTag.Count];
            PosTagger.Posteriors(tables, tokens, posteriors);
            for (var i = 0; i < n; i++)
            {
                var mask = tokens[i].DictionaryMask;
                var gold = sentence.Tags[i];
                if (tokens[i].ForcedTag >= 0 || System.Numerics.BitOperations.PopCount((uint)mask) < 2 || (mask & (1 << gold)) == 0)
                {
                    continue;
                }

                var best = 0;
                for (var t = 1; t < PosTag.Count; t++)
                {
                    if (posteriors[(i * PosTag.Count) + t] > posteriors[(i * PosTag.Count) + best])
                    {
                        best = t;
                    }
                }

                rows++;
                correct += best == gold ? 1 : 0;
            }
        }

        return (rows, correct);
    }

    private static async Task<(int Rows, int Correct)> ScoreAnchorsAsync(
        BasicTables tables,
        List<CorpusRow> rows,
        Func<string, ImmutableArray<WordDetails>> lexicon,
        CancellationToken cancellationToken)
    {
        var classifier = new BasicClassifier(tables, lexicon, (lemma, pos) => WordLexicon.Current.SenseCountOf(lemma, pos), WordLexicon.SensesOf);
        var correct = 0;
        foreach (var row in rows)
        {
            // The answer is right when it is an analysis of the lemma and the part of speech of the sense of the row
            var results = await classifier.ClassifyAsync(row.Words, cancellationToken).ConfigureAwait(false);
            correct += results[row.TargetWordIndex].Details is { } details && BasicTrainer.KeyOf(row).Matches(details) ? 1 : 0;
        }

        return (rows.Count, correct);
    }

    private static string Percent(int part, int whole) =>
        whole == 0 ? "n/a" : (100.0 * part / whole).ToString("0.00", CultureInfo.InvariantCulture) + "%";
}
