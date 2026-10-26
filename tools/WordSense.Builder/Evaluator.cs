using System.Diagnostics;
using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.BertBaseCased;
using DemaConsulting.WordSense.MiniLM;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing
#pragma warning disable S1215 // the measurement of the word table memory needs explicit collections

namespace WordSense.Builder;

/// <summary>
///     The answers of one level: how many sentences were answered at the level and how many of those were right.
/// </summary>
/// <param name="Answered">Sentences answered at the level.</param>
/// <param name="Correct">Answered sentences whose answer at that level was right.</param>
internal readonly record struct LevelCount(int Answered, int Correct);

/// <summary>
///     Counts of one evaluation set, measured through the public result of a model.
/// </summary>
/// <remarks>
///     An answer is right at its own level: at the sense level when the answered sense is the sense of the row, at the
///     group level when the answered group is the group of the sense of the row (a sense that is in no group is never
///     right at the group level), at the part-of-speech level when the part of speech of the selected analysis is the
///     one of the row. A word that the lexicon does not know has the level None and is never right.
/// </remarks>
/// <param name="Name">The name of the set.</param>
/// <param name="Rows">Sentences evaluated.</param>
/// <param name="Sense">The answers at the sense level.</param>
/// <param name="Group">The answers at the group level.</param>
/// <param name="PartOfSpeech">The answers at the part-of-speech level.</param>
/// <param name="None">Sentences whose target word is not in the lexicon.</param>
/// <param name="FineTop1">Sentences whose most probable sense (the largest analysis probability times sense probability over every ranked sense) is the sense of the row; a sentence without ranked senses counts as wrong.</param>
/// <param name="Ranked">Sentences whose result ranks at least one sense.</param>
/// <param name="FineTop1OfRanked">Ranked sentences whose most probable sense is the sense of the row.</param>
/// <param name="PosCorrect">Sentences whose selected analysis has the part of speech of the row, whatever the level of the answer.</param>
/// <param name="AnalysisCorrect">Sentences whose selected analysis has the lemma and the part of speech of the row.</param>
/// <param name="Outcomes">The confidence and the correctness at the answered level of every sentence, or null when not collected.</param>
internal sealed record SetResult(
    string Name,
    int Rows,
    LevelCount Sense,
    LevelCount Group,
    LevelCount PartOfSpeech,
    int None,
    int FineTop1,
    int Ranked,
    int FineTop1OfRanked,
    int PosCorrect,
    int AnalysisCorrect,
    IReadOnlyList<(double Confidence, bool Correct)>? Outcomes = null)
{
    /// <summary>
    ///     Gets the number of sentences whose answer was right at its own level.
    /// </summary>
    public int CorrectAtLevel => Sense.Correct + Group.Correct + PartOfSpeech.Correct;
}

/// <summary>
///     The measurements that only the basic model reports.
/// </summary>
/// <param name="TableBytes">The size of the embedded statistical table.</param>
/// <param name="ColdLoadMilliseconds">The time to read the table the first time.</param>
/// <param name="MicrosecondsPerWord">The mean time to classify one word of the holdout sentences.</param>
/// <param name="MicrosecondsPerSentence">The mean time to classify one holdout sentence.</param>
internal sealed record BasicDetails(long TableBytes, double ColdLoadMilliseconds, double MicrosecondsPerWord, double MicrosecondsPerSentence);

/// <summary>
///     The measurements of one model.
/// </summary>
/// <param name="Model">The model name.</param>
/// <param name="Sets">The result of each evaluation set.</param>
/// <param name="WeightsBytes">The size of the weights file, or 0 when it was not found.</param>
/// <param name="MillisecondsPerSentence">The mean time to classify one holdout sentence.</param>
/// <param name="WordsLoadMilliseconds">The time of the first use of the word table.</param>
/// <param name="WordsMemoryBytes">The managed memory the first use of the word table added.</param>
/// <param name="Basic">The extra measurements of the basic model, or null for the other models.</param>
/// <param name="DownloadBytes">The size of the files the model downloads, or 0 for a model without a download.</param>
/// <param name="RamBytes">The private memory the model added to the process, in bytes (the process counter has a coarse resolution: a model of a few hundred KiB reads as 0, and the table then shows "&lt; 1 MiB").</param>
/// <param name="FirstUseMilliseconds">The time from creating the model to the first classification.</param>
internal sealed record EvaluationResult(
    string Model,
    IReadOnlyList<SetResult> Sets,
    long WeightsBytes,
    double MillisecondsPerSentence,
    double WordsLoadMilliseconds,
    long WordsMemoryBytes,
    BasicDetails? Basic = null,
    long DownloadBytes = 0,
    long RamBytes = 0,
    double FirstUseMilliseconds = 0)
{
    /// <summary>
    ///     Gets the name of the set of all held-out rows when the corpus names the origin of its rows.
    /// </summary>
    internal const string AllRowsSetName = "holdout, all rows (secondary)";

    /// <summary>
    ///     Gets the name of the headline set: the real held-out sentences of the test documents.
    /// </summary>
    internal const string RealTestSetName = "real text, test split (headline)";

    /// <summary>
    ///     Gets the name of the set of the test sentences whose document supplies no anchor of the lemma of the sentence.
    /// </summary>
    internal const string RealTestFreeSetName = "real text, test split, documents without anchors";

    /// <summary>
    ///     Gets the name of the calibration split, on which the thresholds were chosen.
    /// </summary>
    internal const string RealCalibrationSetName = "real text, calibration split (diagnostic)";

    /// <summary>
    ///     Gets the name of the written held-out sentences.
    /// </summary>
    internal const string WrittenSetName = "written holdout (secondary)";

    /// <summary>
    ///     Gets the cells of the model comparison table that this measurement fills: the sense share and precision on the
    ///     real-text test split, and the cost columns.
    /// </summary>
    /// <remarks>
    ///     The headline cell is measured on the real-text test split: the share of its sentences that the model answers at
    ///     the sense level and the precision of those answers. A set that the corpus does not have reads n/a.
    /// </remarks>
    /// <returns>The cells by column name.</returns>
    public Dictionary<string, string> TableCells()
    {
        var inv = CultureInfo.InvariantCulture;
        var real = Sets.FirstOrDefault(s => s.Name == RealTestSetName);
        var weights = Basic is null ? WeightsBytes : Basic.TableBytes;
        var latency = Basic is null ? MillisecondsPerSentence : Basic.MicrosecondsPerSentence / 1000.0;
        var download = DownloadBytes == 0 ? "none" : (DownloadBytes / 1048576.0).ToString("N0", inv) + " MiB";
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ModelTable.RealSenseColumn] = real is null ? "n/a" : ShareAtPrecision(real.Sense.Answered, real.Sense.Correct, real.Rows),
            [ModelTable.DownloadColumn] = download,
            [ModelTable.WeightsColumn] = (weights / 1024.0).ToString("N0", inv) + " KiB",
            [ModelTable.RamColumn] = RamBytes < 1048576 ? "< 1 MiB" : (RamBytes / 1048576.0).ToString("N0", inv) + " MiB",
            [ModelTable.LatencyColumn] = latency.ToString("N2", inv) + " ms",
            [ModelTable.FirstUseColumn] = FirstUseMilliseconds.ToString("N0", inv) + " ms + " + WordsLoadMilliseconds.ToString("N0", inv) + " ms",
            [ModelTable.NeededColumn] = DownloadBytes == 0 ? "no" : "yes",
        };
    }

    /// <summary>
    ///     Writes the cells of this measurement into the model comparison table file (see <see cref="ModelTable"/>).
    /// </summary>
    /// <param name="path">The table file.</param>
    public void UpdateTableFile(string path) => ModelTable.Update(path, Model, TableCells());

    /// <summary>
    ///     Formats the share of the rows answered at a level and the precision of those answers, for example <c>23.4% @ 96.6%</c>.
    /// </summary>
    /// <param name="answered">The rows answered at the level.</param>
    /// <param name="correct">The answered rows that are right.</param>
    /// <param name="rows">All rows.</param>
    /// <returns>The text.</returns>
    internal static string ShareAtPrecision(int answered, int correct, int rows) =>
        rows == 0 ? "n/a" : Percent(answered, rows) + " @ " + Percent(correct, answered);

    private static readonly double[] BucketEdges = [0.4, 0.6, 0.7, 0.8, 0.9];
    private static readonly double[] Thresholds = [0.6, 0.8, 0.9];

    /// <summary>
    ///     Renders the measurements as Markdown.
    /// </summary>
    /// <returns>The text, with LF line ends.</returns>
    public string Render()
    {
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.Append("### ").Append(Model).Append('\n').Append('\n');
        text.Append("Share of the sentences answered at each level:\n\n");
        text.Append("| Set | Rows | Sense | Group | Part of speech | None |\n");
        text.Append("| --- | ---: | ---: | ---: | ---: | ---: |\n");
        foreach (var s in Sets)
        {
            text.Append("| ").Append(s.Name).Append(" | ").Append(s.Rows.ToString(inv))
                .Append(" | ").Append(Percent(s.Sense.Answered, s.Rows))
                .Append(" | ").Append(Percent(s.Group.Answered, s.Rows))
                .Append(" | ").Append(Percent(s.PartOfSpeech.Answered, s.Rows))
                .Append(" | ").Append(Percent(s.None, s.Rows)).Append(" |\n");
        }

        text.Append("\nAccuracy (precision at a level is the share of the answers at that level that are right at it):\n\n");
        text.Append("| Set | Sense precision | Group precision | Part-of-speech precision | Correct at answered level | Fine top-1 | Fine top-1 (ranked rows) | Part of speech | Best analysis |\n");
        text.Append("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n");
        foreach (var s in Sets)
        {
            text.Append("| ").Append(s.Name)
                .Append(" | ").Append(Percent(s.Sense.Correct, s.Sense.Answered))
                .Append(" | ").Append(Percent(s.Group.Correct, s.Group.Answered))
                .Append(" | ").Append(Percent(s.PartOfSpeech.Correct, s.PartOfSpeech.Answered))
                .Append(" | ").Append(Percent(s.CorrectAtLevel, s.Rows))
                .Append(" | ").Append(Percent(s.FineTop1, s.Rows))
                .Append(" | ").Append(Percent(s.FineTop1OfRanked, s.Ranked))
                .Append(" | ").Append(Percent(s.PosCorrect, s.Rows))
                .Append(" | ").Append(Percent(s.AnalysisCorrect, s.Rows)).Append(" |\n");
        }

        text.Append('\n');
        if (Sets.Count > 1)
        {
            text.Append("The first set is the headline: real held-out sentences of the test documents, which no threshold or weight has seen. ")
                .Append("The calibration split chose the thresholds (diagnostic), the written sentences are easier than real text (secondary). ")
                .Append("No set is representative running text: the held-out sentences were selected or written for specific senses, so every ")
                .Append("figure is a classifier measure on a balanced corpus and not the accuracy on representative text.\n\n");
        }

        text.Append("- Weights size: ").Append((WeightsBytes / 1024.0).ToString("N0", inv)).Append(" KiB\n");
        text.Append("- Classification latency: ").Append(MillisecondsPerSentence.ToString("N2", inv)).Append(" ms per sentence\n");
        text.Append("- Word table first use: ").Append(WordsLoadMilliseconds.ToString("N0", inv)).Append(" ms, ")
            .Append((WordsMemoryBytes / 1048576.0).ToString("N1", inv)).Append(" MiB\n");
        if (Basic is not null)
        {
            RenderBasic(text, Basic, inv);
        }
        else
        {
            text.Append('\n');
            RenderCalibration(text, inv);
        }

        return text.ToString();
    }

    internal static string Percent(int part, int whole) => whole == 0 ? "n/a" : (100.0 * part / whole).ToString("0.00", CultureInfo.InvariantCulture) + "%";

    private static string Bucket(int index) => index switch
    {
        0 => "< 0.4",
        1 => "0.4 - 0.6",
        2 => "0.6 - 0.7",
        3 => "0.7 - 0.8",
        4 => "0.8 - 0.9",
        _ => ">= 0.9",
    };

    private static int BucketOf(double confidence)
    {
        var index = 0;
        while (index < BucketEdges.Length && confidence >= BucketEdges[index])
        {
            index++;
        }

        return index;
    }

    private void RenderBasic(StringBuilder text, BasicDetails details, IFormatProvider inv)
    {
        text.Append("- Table size: ").Append((details.TableBytes / 1024.0).ToString("N0", inv)).Append(" KiB\n");
        text.Append("- Table cold load: ").Append(details.ColdLoadMilliseconds.ToString("N1", inv)).Append(" ms\n");
        text.Append("- Classification time: ").Append(details.MicrosecondsPerWord.ToString("N2", inv)).Append(" us per word, ")
            .Append(details.MicrosecondsPerSentence.ToString("N1", inv)).Append(" us per sentence\n\n");

        RenderCalibration(text, inv);

        text.Append("\n#### Best analysis\n\n| Model | Holdout |\n| --- | ---: |\n");
        var holdout = Sets.LastOrDefault();
        text.Append("| Basic (this run) | ").Append(holdout is null ? "n/a" : Percent(holdout.AnalysisCorrect, holdout.Rows)).Append(" |\n");
    }

    private void RenderCalibration(StringBuilder text, IFormatProvider inv)
    {
        text.Append("#### Confidence calibration (rows; correct at the answered level)\n\n");
        text.Append("| Set | ").Append(string.Join(" | ", Enumerable.Range(0, BucketEdges.Length + 1).Select(Bucket))).Append(" |\n");
        text.Append("| --- |").Append(string.Concat(Enumerable.Repeat(" ---: |", BucketEdges.Length + 1))).Append('\n');
        foreach (var set in Sets.Where(s => s.Outcomes is not null))
        {
            var cells = new List<string>();
            for (var b = 0; b <= BucketEdges.Length; b++)
            {
                var rows = set.Outcomes!.Where(o => BucketOf(o.Confidence) == b).ToList();
                cells.Add(rows.Count.ToString(inv) + " / " + Percent(rows.Count(o => o.Correct), rows.Count));
            }

            text.Append("| ").Append(set.Name).Append(" | ").Append(string.Join(" | ", cells)).Append(" |\n");
        }

        text.Append("\n#### Confidence thresholds\n\n");
        text.Append("| Set | Threshold | Coverage | Precision |\n| --- | ---: | ---: | ---: |\n");
        foreach (var set in Sets.Where(s => s.Outcomes is not null))
        {
            foreach (var threshold in Thresholds)
            {
                var kept = set.Outcomes!.Where(o => o.Confidence >= threshold).ToList();
                text.Append("| ").Append(set.Name).Append(" | ").Append(threshold.ToString("0.0", inv))
                    .Append(" | ").Append(Percent(kept.Count, set.Rows))
                    .Append(" | ").Append(Percent(kept.Count(o => o.Correct), kept.Count)).Append(" |\n");
            }
        }
    }
}

/// <summary>
///     Measures a shipped model on the held-out sentences of the fine-sense corpus.
/// </summary>
/// <remarks>
///     Every sentence is classified through the public <c>ClassifyAsync</c> of the model and the result of its target
///     word is judged at the level it was answered (see <see cref="SetResult"/>). Only <c>corpus/holdout</c> is read: the
///     real-text sentences carry the labels of the retired classes and are no longer measured.
/// </remarks>
internal static class Evaluator
{
    private const int WarmUpSentences = 20;

    /// <summary>
    ///     Measures the first use of the embedded word table.
    /// </summary>
    /// <returns>The time in milliseconds and the managed memory it added, in bytes.</returns>
    public static (double Milliseconds, long Bytes) MeasureWords()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetTotalMemory(true);
        var clock = Stopwatch.StartNew();
        var count = WordLexicon.Count;
        clock.Stop();
        GC.Collect();
        var after = GC.GetTotalMemory(true);
        return count == 0 ? (0, 0) : (clock.Elapsed.TotalMilliseconds, after - before);
    }

    /// <summary>
    ///     Evaluates a model.
    /// </summary>
    /// <param name="repository">The repository root.</param>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="info">The model.</param>
    /// <param name="progress">Receives progress messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="outcomesFile">When given, receives one line for each scored sentence (set, index, sense id, answer with its level, confidence, correct at the level 1 or 0) for comparisons between versions.</param>
    /// <returns>The measurements.</returns>
    public static async Task<EvaluationResult> RunAsync(string repository, string corpus, ModelInfo info, TextWriter progress, CancellationToken cancellationToken, string? outcomesFile = null)
    {
        var (wordsMs, wordsBytes) = MeasureWords();
        var holdout = Corpus.ReadDirectory(Path.Join(corpus, "holdout"));

        // The baseline is taken after the corpus is read, so the RAM figure holds only the model
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long privateBefore;
        using (var process = Process.GetCurrentProcess())
        {
            privateBefore = process.PrivateMemorySize64;
        }

        // The process counter moves in coarse steps, so the managed heap is measured too and the larger figure is reported
        var managedBefore = GC.GetTotalMemory(true);
        double coldLoadMs = 0;
        long tableBytes = 0;
        if (!info.IsEncoder)
        {
            var load = Stopwatch.StartNew();
            _ = BasicTables.Default;
            load.Stop();
            coldLoadMs = load.Elapsed.TotalMilliseconds;
            using var resource = typeof(BasicWordSenseModel).Assembly.GetManifestResourceStream(BasicTables.ResourceName);
            tableBytes = resource?.Length ?? 0;
        }
        var modelDirectory = info.ModelDirectory is null ? null : Path.Join(repository, "model", info.ModelDirectory);
        var firstUse = Stopwatch.StartNew();
        using var model = info.Create(modelDirectory);
        await ModelCatalog.EnsureAsync(model, cancellationToken).ConfigureAwait(false);
        IWordSenseModel classifier = model;

        foreach (var row in holdout.Take(1))
        {
            await classifier.ClassifyAsync(row.Words, cancellationToken).ConfigureAwait(false);
        }

        firstUse.Stop();
        foreach (var row in holdout.Take(WarmUpSentences))
        {
            await classifier.ClassifyAsync(row.Words, cancellationToken).ConfigureAwait(false);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long privateAfter;
        using (var process = Process.GetCurrentProcess())
        {
            privateAfter = process.PrivateMemorySize64;
        }

        var ramBytes = Math.Max(0, Math.Max(privateAfter - privateBefore, GC.GetTotalMemory(true) - managedBefore));
        // Only the pinned files are downloaded, not every file of the model folder (README, SHA256SUMS, unused tokenizer)
        var downloadBytes = model switch
        {
            MiniLMWordSenseModel minilm => minilm.Encoder.Spec.Files.Sum(f => f.Length),
            BertBaseCasedWordSenseModel bert => bert.Encoder.Spec.Files.Sum(f => f.Length),
            _ => 0,
        };

        using var outcomesLog = outcomesFile is null ? null : new StreamWriter(outcomesFile, false, new System.Text.UTF8Encoding(false)) { NewLine = "\n" };
        var clock = Stopwatch.StartNew();
        var holdoutSplit = RealSplit.Classify(corpus, holdout, Corpus.ReadDirectory(Path.Join(corpus, "anchors")));
        var subsetSpecs = SubsetsOf(holdoutSplit);
        var overallName = subsetSpecs.Count == 0 ? "holdout" : EvaluationResult.AllRowsSetName;
        var sides = new Dictionary<CorpusRow, string>(ReferenceEqualityComparer.Instance as IEqualityComparer<CorpusRow>);
        foreach (var (rowsOfSide, side) in new[] { (holdoutSplit.Calibration, "calibration"), (holdoutSplit.Test, "test"), (holdoutSplit.Written, "written") })
        {
            foreach (var r in rowsOfSide)
            {
                sides[r] = side;
            }
        }

        var (holdoutResult, subsetResults) = await ScoreAsync(overallName, holdout, classifier, outcomesLog, subsetSpecs, r => sides.GetValueOrDefault(r, "other"), cancellationToken).ConfigureAwait(false);
        clock.Stop();
        var latency = holdout.Count == 0 ? 0 : clock.Elapsed.TotalMilliseconds / holdout.Count;
        progress.WriteLine($"{info.Name}: holdout done.");
        List<SetResult> sets = [.. subsetResults, holdoutResult];

        var weights = info.IsEncoder ? info.WeightsPath(repository) : string.Empty;
        BasicDetails? basic = null;
        if (!info.IsEncoder)
        {
            var wordCount = Math.Max(1, holdout.Sum(r => r.Words.Count));
            var perWordUs = holdout.Count == 0 ? 0 : clock.Elapsed.TotalMilliseconds * 1000 / wordCount;
            basic = new BasicDetails(tableBytes, coldLoadMs, perWordUs, latency * 1000);
        }

        return new EvaluationResult(info.Name, sets, File.Exists(weights) ? new FileInfo(weights).Length : 0, latency, wordsMs, wordsBytes, basic,
            downloadBytes, ramBytes, info.IsEncoder ? firstUse.Elapsed.TotalMilliseconds : coldLoadMs);
    }

    /// <summary>
    ///     The verdict on the result of one sentence.
    /// </summary>
    /// <param name="Level">The level at which the model answered.</param>
    /// <param name="CorrectAtLevel">Whether the answer is right at its own level.</param>
    /// <param name="Ranked">Whether the result ranks at least one sense.</param>
    /// <param name="FineTop1">Whether the most probable ranked sense is the sense of the row.</param>
    /// <param name="PosCorrect">Whether the selected analysis has the part of speech of the row.</param>
    /// <param name="AnalysisCorrect">Whether the selected analysis has the lemma and the part of speech of the row.</param>
    /// <param name="Confidence">The confidence of the answer.</param>
    /// <param name="BestSense">The most probable ranked sense (the fine top-1), or null when the result ranks none.</param>
    internal readonly record struct Verdict(SenseLevel Level, bool CorrectAtLevel, bool Ranked, bool FineTop1, bool PosCorrect, bool AnalysisCorrect, double Confidence, string? BestSense = null);

    /// <summary>
    ///     Judges the result of the target word of a row.
    /// </summary>
    /// <remarks>
    ///     The answer is right at its own level: the sense at the sense level, the group of the sense of the row at the
    ///     group level (a sense without a group is never right there), the part of speech of the row at the
    ///     part-of-speech level; an unknown word is never right. The most probable sense is the one with the largest
    ///     sum, over the analyses that list it, of the probability of the analysis times its own probability (the
    ///     first on a tie); a result that ranks no sense has none and counts as wrong.
    /// </remarks>
    /// <param name="row">The row.</param>
    /// <param name="result">The result of the target word of the row.</param>
    /// <returns>The verdict.</returns>
    internal static Verdict Judge(CorpusRow row, ClassifyResult result)
    {
        ArgumentNullException.ThrowIfNull(row);
        var details = result.Details;
        var posRight = details is not null && details.PartOfSpeech == row.PartOfSpeech;
        var analysisRight = posRight && string.Equals(details!.Lemma, row.Lemma, StringComparison.Ordinal);
        var correct = result.Level switch
        {
            SenseLevel.Sense => string.Equals(result.SenseId, row.SenseId, StringComparison.Ordinal),
            SenseLevel.Group => GroupOf(row.SenseId) is { } group && string.Equals(result.GroupId, group, StringComparison.Ordinal),
            SenseLevel.PartOfSpeech => posRight,
            _ => false,
        };

        // The most probable sense sums analysis probability x sense probability per sense id (see SuiteFile.BestSense)
        var best = SuiteFile.BestSense(result);
        return new Verdict(result.Level, correct, best is not null, string.Equals(best, row.SenseId, StringComparison.Ordinal), posRight, analysisRight, result.Confidence, best);
    }

    /// <summary>
    ///     Finds the group of a sense of the catalog.
    /// </summary>
    /// <param name="senseId">The sense id.</param>
    /// <returns>The group id, or null when the sense is in no group or unknown.</returns>
    private static string? GroupOf(string senseId) => WordLexicon.TryGetSense(senseId, out var sense) ? sense.GroupId : null;

    /// <summary>
    ///     Describes the answer of a result for the outcomes file: the level and what was answered at it.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The text.</returns>
    private static string Answer(ClassifyResult result) => result.Level switch
    {
        SenseLevel.Sense => "Sense:" + result.SenseId,
        SenseLevel.Group => "Group:" + result.GroupId,
        SenseLevel.PartOfSpeech => "PartOfSpeech:" + result.Details?.Lemma + "|" + result.Details?.PartOfSpeech,
        _ => "None",
    };

    /// <summary>
    ///     Names the subsets of the held-out rows from the real-text split of the corpus.
    /// </summary>
    /// <remarks>
    ///     The held-out sentences are not representative real text. The headline set is the real sentences of the test
    ///     documents; the same sentences restricted to documents that supply no anchor of the lemma of the sentence, the
    ///     calibration split (on which
    ///     the thresholds were chosen) and the written sentences are secondary or diagnostic and are named so.
    /// </remarks>
    /// <param name="split">The split of the held-out rows (see <see cref="RealSplit"/>).</param>
    /// <returns>The named predicates of the sets that have rows; empty when the corpus has no provenance.</returns>
    internal static List<(string Name, Func<CorpusRow, bool> Member)> SubsetsOf(HoldoutSplit split)
    {
        ArgumentNullException.ThrowIfNull(split);
        var subsets = new List<(string, Func<CorpusRow, bool>)>();
        foreach (var (name, rows) in new[]
                 {
                     (EvaluationResult.RealTestSetName, split.Test),
                     (EvaluationResult.RealTestFreeSetName, split.TestWithoutAnchorDocuments),
                     (EvaluationResult.RealCalibrationSetName, split.Calibration),
                     (EvaluationResult.WrittenSetName, split.Written),
                 })
        {
            if (rows.Count > 0)
            {
                var members = new HashSet<CorpusRow>(rows, ReferenceEqualityComparer.Instance);
                subsets.Add((name, r => members.Contains(r)));
            }
        }

        return subsets;
    }

    private static async Task<(SetResult Overall, List<SetResult> Subsets)> ScoreAsync(
        string name,
        List<CorpusRow> rows,
        IWordSenseModel classifier,
        TextWriter? outcomesLog,
        List<(string Name, Func<CorpusRow, bool> Member)> subsets,
        Func<CorpusRow, string>? sideOf,
        CancellationToken cancellationToken)
    {
        var tallies = new List<Tally> { new(name, true) };
        tallies.AddRange(subsets.Select(s => new Tally(s.Name, false)));
        var index = 0;
        foreach (var row in rows)
        {
            var results = await classifier.ClassifyAsync(row.Words, cancellationToken).ConfigureAwait(false);
            var actual = results[row.TargetWordIndex];
            var verdict = Judge(row, actual);
            tallies[0].Add(verdict);
            for (var s = 0; s < subsets.Count; s++)
            {
                if (subsets[s].Member(row))
                {
                    tallies[s + 1].Add(verdict);
                }
            }

            outcomesLog?.WriteLine(string.Join('\t', name, index, row.SenseId, Answer(actual), actual.Confidence.ToString("F6", CultureInfo.InvariantCulture), verdict.CorrectAtLevel ? "1" : "0", verdict.BestSense ?? string.Empty, sideOf?.Invoke(row) ?? string.Empty, row.Sentence));
            index++;
        }

        return (tallies[0].ToResult(), tallies.Skip(1).Select(t => t.ToResult()).ToList());
    }

    /// <summary>
    ///     Totals the verdicts of a set of rows into the counts of a set.
    /// </summary>
    /// <param name="name">The name of the set.</param>
    /// <param name="verdicts">The verdicts, one for each row.</param>
    /// <returns>The counts.</returns>
    internal static SetResult Summarize(string name, IEnumerable<Verdict> verdicts)
    {
        ArgumentNullException.ThrowIfNull(verdicts);
        var tally = new Tally(name, false);
        foreach (var verdict in verdicts)
        {
            tally.Add(verdict);
        }

        return tally.ToResult();
    }

    /// <summary>
    ///     The running counts of one evaluation set.
    /// </summary>
    private sealed class Tally(string name, bool keepOutcomes)
    {
        private readonly LevelCount[] _answered = new LevelCount[4];
        private readonly List<(double Confidence, bool Correct)> _outcomes = [];
        private int _rows, _ranked, _fineTop1, _fineTop1OfRanked, _pos, _analysis;

        public void Add(Verdict verdict)
        {
            var level = (int)verdict.Level;
            _answered[level] = new LevelCount(_answered[level].Answered + 1, _answered[level].Correct + (verdict.CorrectAtLevel ? 1 : 0));
            _rows++;
            _ranked += verdict.Ranked ? 1 : 0;
            _fineTop1 += verdict.FineTop1 ? 1 : 0;
            _fineTop1OfRanked += verdict.Ranked && verdict.FineTop1 ? 1 : 0;
            _pos += verdict.PosCorrect ? 1 : 0;
            _analysis += verdict.AnalysisCorrect ? 1 : 0;
            if (keepOutcomes)
            {
                _outcomes.Add((verdict.Confidence, verdict.CorrectAtLevel));
            }
        }

        public SetResult ToResult() => new(
            name,
            _rows,
            _answered[(int)SenseLevel.Sense],
            _answered[(int)SenseLevel.Group],
            _answered[(int)SenseLevel.PartOfSpeech],
            _answered[(int)SenseLevel.None].Answered,
            _fineTop1,
            _ranked,
            _fineTop1OfRanked,
            _pos,
            _analysis,
            keepOutcomes ? _outcomes : null);
    }
}
