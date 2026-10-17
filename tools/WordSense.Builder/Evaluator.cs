using System.Diagnostics;
using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

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
internal sealed record SetResult(string Name, int Rows, int BestSense, int Pos, int HeteronymRows, int Meaning, int Answered, int AnsweredCorrect);

/// <summary>
///     The measurements of one model.
/// </summary>
/// <param name="Model">The model name.</param>
/// <param name="Sets">The result of each evaluation set.</param>
/// <param name="WeightsBytes">The size of the weights file, or 0 when it was not found.</param>
/// <param name="MillisecondsPerSentence">The mean time to classify one holdout sentence.</param>
/// <param name="WordsLoadMilliseconds">The time of the first use of the word table.</param>
/// <param name="WordsMemoryBytes">The managed memory the first use of the word table added.</param>
internal sealed record EvaluationResult(
    string Model,
    IReadOnlyList<SetResult> Sets,
    long WeightsBytes,
    double MillisecondsPerSentence,
    double WordsLoadMilliseconds,
    long WordsMemoryBytes)
{
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
        return text.ToString();
    }

    private static string Percent(int part, int whole) => whole == 0 ? "n/a" : (100.0 * part / whole).ToString("0.00", CultureInfo.InvariantCulture) + "%";
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

        var modelDirectory = info.ModelDirectory is null ? null : Path.Combine(repository, "model", info.ModelDirectory);
        var model = info.Create(modelDirectory);
        using var disposable = model as IDisposable;
        await model.EnsureModelAsync(null, cancellationToken).ConfigureAwait(false);
        var classifier = model.Create();

        foreach (var row in holdout.Take(WarmUpSentences))
        {
            await classifier.ClassifyAsync(row.Words, cancellationToken).ConfigureAwait(false);
        }

        var sets = new List<SetResult>();
        var clock = Stopwatch.StartNew();
        sets.Add(await ScoreAsync("holdout", holdout, classifier, gold, cancellationToken).ConfigureAwait(false));
        clock.Stop();
        var latency = holdout.Count == 0 ? 0 : clock.Elapsed.TotalMilliseconds / holdout.Count;
        progress.WriteLine($"{info.Name}: holdout done.");
        sets.Add(await ScoreAsync("real-text", real, classifier, gold, cancellationToken).ConfigureAwait(false));

        var weights = info.IsEncoder ? info.WeightsPath(repository) : string.Empty;
        return new EvaluationResult(info.Name, sets, File.Exists(weights) ? new FileInfo(weights).Length : 0, latency, wordsMs, wordsBytes);
    }

    private static async Task<SetResult> ScoreAsync(
        string name,
        IReadOnlyList<CorpusRow> rows,
        IWordSenseClassifier classifier,
        IReadOnlyDictionary<(string Lemma, string Label), InventoryRow> gold,
        CancellationToken cancellationToken)
    {
        int best = 0, pos = 0, heteronyms = 0, meaning = 0, answered = 0, answeredCorrect = 0;
        foreach (var row in rows)
        {
            var expected = gold[(row.Lemma, row.Sense)];
            var senses = await classifier.ClassifyAsync(row.Words, cancellationToken).ConfigureAwait(false);
            var actual = senses[row.TargetWordIndex];
            var details = actual.Details;
            var rightSense = details is not null && details.PartOfSpeech == expected.Pos && string.Equals(details.Meaning, expected.Meaning, StringComparison.Ordinal);
            best += rightSense ? 1 : 0;
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

        return new SetResult(name, rows.Count, best, pos, heteronyms, meaning, answered, answeredCorrect);
    }
}
