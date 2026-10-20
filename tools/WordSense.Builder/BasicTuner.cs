using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Basic;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     Tries the hyper-parameter grid of <see cref="BasicHyperparameters"/> one parameter at a time and prints how each
///     variant scores. It reads only data that is safe to tune against: the development file of the treebank and a
///     deterministic sample of the anchor sentences split into a train and a dev part. It never reads the holdout or the
///     real-text sentences, and it writes no file.
/// </summary>
internal static class BasicTuner
{
    /// <summary>
    ///     Runs the tuning.
    /// </summary>
    /// <param name="corpus">The corpus directory (anchors, senses, the legacy class map and the vendored treebank counts).</param>
    /// <param name="udCache">The directory that holds the verified treebank development file.</param>
    /// <param name="progress">Receives progress messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rendered Markdown table.</returns>
    public static async Task<string> RunAsync(string corpus, string udCache, TextWriter progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var counts = UdCounts.Read(Path.Join(corpus, "ud"));
        const string devName = "en_ewt-ud-dev.conllu";
        var devPath = Path.Join(udCache, devName);
        if (!File.Exists(devPath))
        {
            throw new FileNotFoundException($"The treebank development file is missing; run 'ud-counts' or 'train-basic --refresh-ud true' first. Expected: {devPath}");
        }

        UdSource.Verify(devPath, UdSource.Files[devName]);
        var dev = UdCounts.ReadConllu(devPath).Take(BasicHyperparameters.UdDevSentences).ToList();
        var lexicon = (Func<string, ImmutableArray<WordDetails>>)(key => WordLexicon.TryLookup(key, out var found) ? found : []);
        var inventory = SenseInventory.ReadDirectory(Path.Join(corpus, "senses"));
        var labels = AnalysisLabels.Load(inventory, corpus);
        var (train, anchorDev) = SplitAnchors(Corpus.ReadDirectory(Path.Join(corpus, "anchors")));
        progress.WriteLine($"Tuning on {dev.Count} treebank dev sentences, {train.Count} anchor train and {anchorDev.Count} anchor dev sentences.");

        var text = new StringBuilder();
        text.Append("| Variant | UD dev ambiguous POS | Anchor dev analysis | Table size |\n");
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
            var data = BasicTrainer.Train(counts, lexicon, train, inventory, labels, hp);
            var blob = BasicTablesWriter.Write(data, WordLexicon.Current.InventoryHash, CompressionLevel.Fastest);
            var tables = BasicTables.Load(blob, WordLexicon.Current);
            var (ambiguous, ambiguousCorrect) = ScoreTreebank(tables, dev, lexicon);
            var (anchorRows, anchorCorrect) = await ScoreAnchorsAsync(tables, anchorDev, lexicon, labels, cancellationToken).ConfigureAwait(false);
            text.Append("| ").Append(label)
                .Append(" | ").Append(Percent(ambiguousCorrect, ambiguous))
                .Append(" | ").Append(Percent(anchorCorrect, anchorRows))
                .Append(" | ").Append((blob.Length / 1024.0).ToString("N0", CultureInfo.InvariantCulture)).Append(" KiB |\n");
            progress.WriteLine($"{label} done.");
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

    private static string Describe(BasicHyperparameters hp) => string.Create(
        CultureInfo.InvariantCulture,
        $"gamma {hp.Gamma}, beta {hp.Beta}, closed {hp.ClosedMinCount}/{hp.ClosedShare}, hmm weight {hp.HmmWeight}, temperature {hp.Temperature}, cue alpha {hp.CueAlpha}");

    private static (int Rows, int Correct) ScoreTreebank(BasicTables tables, List<UdSentence> sentences, Func<string, ImmutableArray<WordDetails>> lexicon)
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
        AnalysisLabels labels,
        CancellationToken cancellationToken)
    {
        var classifier = new BasicClassifier(tables, lexicon, (lemma, pos) => WordLexicon.Current.SenseCountOf(lemma, pos), WordLexicon.SensesOf);
        var correct = 0;
        foreach (var row in rows)
        {
            // The answer is right when it is one of the analyses that the label of the row stands for
            var results = await classifier.ClassifyAsync(row.Words, cancellationToken).ConfigureAwait(false);
            correct += labels.IsRight(row.Lemma, row.Sense, results[row.TargetWordIndex].Details) ? 1 : 0;
        }

        return (rows.Count, correct);
    }

    private static string Percent(int part, int whole) =>
        whole == 0 ? "n/a" : (100.0 * part / whole).ToString("0.00", CultureInfo.InvariantCulture) + "%";
}
