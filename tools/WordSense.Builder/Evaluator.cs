using System.Diagnostics;
using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Basic;
using DemaConsulting.WordSense.Onnx;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing
#pragma warning disable S1215 // the measurement of the word table memory needs explicit collections

namespace WordSense.Builder;

/// <summary>
///     Counts of one evaluation set.
/// </summary>
/// <param name="Name">The name of the set.</param>
/// <param name="Rows">Sentences evaluated.</param>
/// <param name="BestSense">Sentences whose best sense (part of speech and meaning) was right.</param>
/// <param name="Pos">Sentences whose part of speech was right.</param>
/// <param name="HeteronymRows">Sentences of heteronym lemmas.</param>
/// <param name="Meaning">Sentences of heteronym lemmas whose meaning (pronunciation) was right.</param>
/// <param name="Answered">Sentences with a confidence of at least 0.5.</param>
/// <param name="AnsweredCorrect">Answered sentences whose best sense was right.</param>
/// <param name="Outcomes">The confidence and correctness of every sentence, or null when not collected.</param>
internal sealed record SetResult(
    string Name,
    int Rows,
    int BestSense,
    int Pos,
    int HeteronymRows,
    int Meaning,
    int Answered,
    int AnsweredCorrect,
    IReadOnlyList<(double Confidence, bool Correct)>? Outcomes = null);

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
/// <param name="RamBytes">The private memory the model added to the process, in bytes.</param>
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
    private const string TableBegin = "<!-- model-table:begin -->";
    private const string TableEnd = "<!-- model-table:end -->";

    private const string TableHeader =
        "| Model | Holdout best sense | Real-text best sense | Part of speech (holdout / real) | Pronunciation (holdout / real) | Download | Weights / table | RAM | Latency per sentence | First use (model + word table) | Download needed |\n" +
        "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |\n";

    /// <summary>
    ///     Renders the row of this model for the model comparison table of the README.
    /// </summary>
    /// <returns>The table row, ending with a line feed.</returns>
    public string RenderTableRow()
    {
        var inv = CultureInfo.InvariantCulture;
        var holdout = Sets.First(s => s.Name == "holdout");
        var real = Sets.First(s => s.Name == "real-text");
        var weights = Basic is null ? WeightsBytes : Basic.TableBytes;
        var latency = Basic is null ? MillisecondsPerSentence : Basic.MicrosecondsPerSentence / 1000.0;
        var download = DownloadBytes == 0 ? "none" : (DownloadBytes / 1048576.0).ToString("N0", inv) + " MiB";
        return "| " + DisplayName(Model) +
               " | " + Percent(holdout.BestSense, holdout.Rows) +
               " | " + Percent(real.BestSense, real.Rows) +
               " | " + Percent(holdout.Pos, holdout.Rows) + " / " + Percent(real.Pos, real.Rows) +
               " | " + Percent(holdout.Meaning, holdout.HeteronymRows) + " / " + Percent(real.Meaning, real.HeteronymRows) +
               " | " + download +
               " | " + (weights / 1024.0).ToString("N0", inv) + " KiB" +
               " | " + (RamBytes / 1048576.0).ToString("N0", inv) + " MiB" +
               " | " + latency.ToString("N2", inv) + " ms" +
               " | " + FirstUseMilliseconds.ToString("N0", inv) + " ms + " + WordsLoadMilliseconds.ToString("N0", inv) + " ms" +
               " | " + (DownloadBytes == 0 ? "no" : "yes") + " |\n";
    }

    /// <summary>
    ///     Writes the model comparison table file, replacing the row of this model and keeping the rows of the others.
    /// </summary>
    /// <param name="path">The table file.</param>
    public void UpdateTableFile(string path)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        if (File.Exists(path))
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var cells = line.Split('|');
                if (line.StartsWith("| ", StringComparison.Ordinal) && cells.Length > 2 && !line.StartsWith("| Model ", StringComparison.Ordinal) && !line.StartsWith("| ---", StringComparison.Ordinal))
                {
                    rows[cells[1].Trim()] = line + "\n";
                }
            }
        }

        rows[DisplayName(Model)] = RenderTableRow();
        // The table lists the models from the simplest to the most accurate (not in catalog order)
        var order = new[] { "basic", "minilm", "bert-base-cased" }
            .Concat(ModelCatalog.All.Select(m => m.Name))
            .Distinct(StringComparer.Ordinal)
            .Select(DisplayName)
            .ToList();
        var text = new StringBuilder();
        text.Append("# Model comparison table\n\n");
        text.Append("This file is generated by `WordSense.Builder evaluate <model> --table docs/build_notes/model-table.md`.\n");
        text.Append("Do not edit it by hand: the table of the root README must equal the table below (a doc-test checks it).\n\n");
        text.Append("<!-- markdownlint-disable MD013 -->\n\n");
        text.Append(TableBegin).Append('\n').Append(TableHeader);
        foreach (var name in order.Where(rows.ContainsKey).Concat(rows.Keys.Where(k => !order.Contains(k))))
        {
            text.Append(rows[name]);
        }

        text.Append(TableEnd).Append("\n\n<!-- markdownlint-enable MD013 -->\n");
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    private static string DisplayName(string model) => model switch
    {
        "basic" => "Basic",
        "minilm" => "MiniLM",
        "bert-base-cased" => "BERT base cased",
        _ => model,
    };

    private static readonly double[] BucketEdges = [0.4, 0.6, 0.7, 0.8, 0.9];
    private static readonly double[] Thresholds = [0.6, 0.8, 0.9];

    /// <summary>
    ///     The best-sense accuracy (holdout, real-text) of the models the basic model is compared with; fixed reference
    ///     values (the spike of the statistical tagger and the two encoder models), so that every run prints the same table.
    /// </summary>
    private static readonly (string Name, string Holdout, string Real)[] References =
    [
        ("Statistical tagger spike (reference)", "91.4%", "81.9%"),
        ("MiniLM", "96.87%", "92.60%"),
        ("BertBaseCased", "99.05%", "97.39%"),
    ];

    /// <summary>
    ///     Renders the measurements as Markdown.
    /// </summary>
    /// <returns>The text, with LF line ends.</returns>
    public string Render()
    {
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.Append("### ").Append(Model).Append('\n').Append('\n');
        text.Append("| Set | Rows | Best sense | Part of speech | Pronunciation | Answered | Answered accuracy |\n");
        text.Append("| --- | ---: | ---: | ---: | ---: | ---: | ---: |\n");
        foreach (var s in Sets)
        {
            text.Append("| ").Append(s.Name).Append(" | ").Append(s.Rows.ToString(inv))
                .Append(" | ").Append(Percent(s.BestSense, s.Rows))
                .Append(" | ").Append(Percent(s.Pos, s.Rows))
                .Append(" | ").Append(Percent(s.Meaning, s.HeteronymRows))
                .Append(" | ").Append(Percent(s.Answered, s.Rows))
                .Append(" | ").Append(Percent(s.AnsweredCorrect, s.Answered)).Append(" |\n");
        }

        text.Append('\n');
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

    private static string Percent(int part, int whole) => whole == 0 ? "n/a" : (100.0 * part / whole).ToString("0.00", CultureInfo.InvariantCulture) + "%";

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

        text.Append("\n#### Comparison (best sense)\n\n| Model | Holdout | Real-text |\n| --- | ---: | ---: |\n");
        var holdout = Sets.FirstOrDefault(s => s.Name == "holdout");
        var real = Sets.FirstOrDefault(s => s.Name == "real-text");
        text.Append("| Basic (this run) | ").Append(holdout is null ? "n/a" : Percent(holdout.BestSense, holdout.Rows))
            .Append(" | ").Append(real is null ? "n/a" : Percent(real.BestSense, real.Rows)).Append(" |\n");
        foreach (var (name, holdoutValue, realValue) in References)
        {
            text.Append("| ").Append(name).Append(" | ").Append(holdoutValue).Append(" | ").Append(realValue).Append(" |\n");
        }
    }

    private void RenderCalibration(StringBuilder text, IFormatProvider inv)
    {
        text.Append("#### Confidence calibration (rows; best-sense accuracy)\n\n");
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
///     Measures a shipped model on the holdout and the real-text sentences.
/// </summary>
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
        var count = WordLexicon.Words.Count;
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
    /// <returns>The measurements.</returns>
    public static async Task<EvaluationResult> RunAsync(string repository, string corpus, ModelInfo info, TextWriter progress, CancellationToken cancellationToken)
    {
        var (wordsMs, wordsBytes) = MeasureWords();
        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses"));
        var gold = inventory.Rows.ToDictionary(r => (r.Lemma, r.Label));
        var holdout = Corpus.ReadDirectory(Path.Combine(corpus, "holdout"));
        var realPath = Path.Combine(corpus, "realtext", "eval-seed.tsv");
        var real = File.Exists(realPath) ? Corpus.ReadSentences(realPath) : [];

        // The baseline is taken after the corpus is read, so the RAM figure holds only the model
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long privateBefore;
        using (var process = Process.GetCurrentProcess())
        {
            privateBefore = process.PrivateMemorySize64;
        }

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
        var modelDirectory = info.ModelDirectory is null ? null : Path.Combine(repository, "model", info.ModelDirectory);
        var firstUse = Stopwatch.StartNew();
        using var model = info.Create(modelDirectory);
        await model.EnsureModelAsync(null, cancellationToken).ConfigureAwait(false);
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

        var ramBytes = Math.Max(0, privateAfter - privateBefore);
        // Only the pinned files are downloaded, not every file of the model folder (README, SHA256SUMS, unused tokenizer)
        var downloadBytes = model is OnnxWordSenseModel onnx ? onnx.Spec.Files.Sum(f => f.Length) : 0;

        var sets = new List<SetResult>();
        var clock = Stopwatch.StartNew();
        sets.Add(await ScoreAsync("holdout", holdout, classifier, gold, cancellationToken).ConfigureAwait(false));
        clock.Stop();
        var latency = holdout.Count == 0 ? 0 : clock.Elapsed.TotalMilliseconds / holdout.Count;
        progress.WriteLine($"{info.Name}: holdout done.");
        sets.Add(await ScoreAsync("real-text", real, classifier, gold, cancellationToken).ConfigureAwait(false));

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

    private static async Task<SetResult> ScoreAsync(
        string name,
        IReadOnlyList<CorpusRow> rows,
        IWordSenseModel classifier,
        IReadOnlyDictionary<(string Lemma, string Label), InventoryRow> gold,
        CancellationToken cancellationToken)
    {
        int best = 0, pos = 0, heteronyms = 0, meaning = 0, answered = 0, answeredCorrect = 0;
        var outcomes = new List<(double Confidence, bool Correct)>(rows.Count);
        foreach (var row in rows)
        {
            var expected = gold[(row.Lemma, row.Sense)];
            var senses = await classifier.ClassifyAsync(row.Words, cancellationToken).ConfigureAwait(false);
            var actual = senses[row.TargetWordIndex];
            var details = actual.Details;
            var rightSense = details is not null && details.PartOfSpeech == expected.Pos && string.Equals(details.Meaning, expected.Meaning, StringComparison.Ordinal);
            best += rightSense ? 1 : 0;
            outcomes.Add((actual.Confidence, rightSense));
            pos += details is not null && details.PartOfSpeech == expected.Pos ? 1 : 0;
            if (expected.Meaning is not null)
            {
                heteronyms++;
                meaning += details is not null && string.Equals(details.Meaning, expected.Meaning, StringComparison.Ordinal) ? 1 : 0;
            }

            if (actual.Confidence >= 0.5)
            {
                answered++;
                answeredCorrect += rightSense ? 1 : 0;
            }
        }

        return new SetResult(name, rows.Count, best, pos, heteronyms, meaning, answered, answeredCorrect, outcomes);
    }
}
