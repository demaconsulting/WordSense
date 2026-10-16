using System.Diagnostics;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests that run the real ONNX model and the embedded table. They are skipped when the Git LFS
///     content of the <c>model</c> directory is not available.
/// </summary>
public class WordSenseTests
{
    private static async Task<WordSenseClassifier> CreateClassifierAsync(int threads = 0)
    {
        var options = new WordSenseClassifierOptions { ModelDirectory = ModelAssets.RequireModelDirectory(), IntraOpThreads = threads };
        return await WordSenseClassifier.CreateAsync(options, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     The embedded table must have been trained with exactly the pinned model file, and the pinned
    ///     digests must match the committed files; a stale table fails here.
    /// </summary>
    [Fact]
    public void WordSense_EmbeddedTable_MatchesPinnedModel()
    {
        var modelDirectory = ModelAssets.RequireModelDirectory();
        var table = WordSenseTable.LoadEmbedded();

        Assert.Equal(WordSenseModelCatalog.OnnxSha256, table.ModelSha256);
        Assert.Equal(WordSenseModelCatalog.OnnxSha256, WordSenseModelCatalog.HashFile(Path.Combine(modelDirectory, "model_int8.onnx")));
        Assert.Equal(WordSenseModelCatalog.VocabularySha256, WordSenseModelCatalog.HashFile(Path.Combine(modelDirectory, "vocab.txt")));
        Assert.Equal(WordSenseModelCatalog.ModelId, table.ModelId);

        // The committed checksum list is what the documentation and the workflow verify
        var sums = File.ReadAllLines(Path.Combine(modelDirectory, "SHA256SUMS")).Select(l => l.TrimEnd('\r')).ToList();
        Assert.Contains(sums, l => l.StartsWith(WordSenseModelCatalog.OnnxSha256, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sums, l => l.StartsWith(WordSenseModelCatalog.VocabularySha256, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     With the embedded table, a sentence of common dictionary words is answered without any encoder call; the
    ///     encoder here is a counting fake, so no model files are needed.
    /// </summary>
    [Fact]
    public async Task WordSense_Dictionary_CommonWords_Answered()
    {
        var encoder = new CountingEncoder();
        string[] vocabulary = ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "hunter"];
        using var classifier = new WordSenseClassifier(encoder, WordSenseTable.LoadEmbedded(), new WordPieceTokenizer(vocabulary), null, false);

        var analyses = await classifier.ClassifyAsync(
            ["The", "hunter", "quickly", "climbed", "a", "tall", "mountain", "."],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, encoder.Calls);
        Assert.Equal(PartOfSpeech.Noun, analyses[1].Details!.PartOfSpeech);
        Assert.Equal(AnswerBasis.Dictionary, analyses[1].Basis);
        Assert.Equal(AnswerBasis.Dictionary, analyses[2].Basis);
        Assert.Equal(AnswerBasis.Dictionary, analyses[6].Basis);
        Assert.Equal(AnswerBasis.None, analyses[0].Basis);

        // A dictionary answer has margin 0, no runner-up and exactly its own sense as candidate
        foreach (var index in new[] { 1, 2, 6 })
        {
            Assert.False(analyses[index].Abstained);
            Assert.Equal(0.0, analyses[index].Margin);
            Assert.Equal(0.0, analyses[index].RunnerUpMargin);
            Assert.Null(analyses[index].RunnerUp);
            Assert.Null(analyses[index].Details!.Pronunciation);
            Assert.Same(analyses[index].Details, Assert.Single(analyses[index].Candidates));
        }

        // A dictionary word with several parts of speech has no answer and lists them all
        var table = WordSenseTable.LoadEmbedded();
        var several = table.Lemmas.First(l => l.Weights is null && l.Senses.Count > 1 && l.Lemma.Length > 3);
        var multi = (await classifier.ClassifyAsync([several.Lemma], TestContext.Current.CancellationToken))[0];
        Assert.Equal(AnswerBasis.Dictionary, multi.Basis);
        Assert.True(multi.Abstained);
        Assert.Null(multi.Details);
        Assert.Equal(several.Candidates, multi.Candidates);

        // With the dictionary switched off every one of these words is unknown
        using var off = new WordSenseClassifier(
            encoder, table, new WordPieceTokenizer(vocabulary), new WordSenseClassifierOptions { UseLexicon = false }, false);
        var none = await off.ClassifyAsync(
            ["The", "hunter", "quickly", "climbed", "a", "tall", "mountain", ".", several.Lemma],
            TestContext.Current.CancellationToken);
        Assert.All(none, a => Assert.Equal(AnswerBasis.None, a.Basis));
        Assert.All(none, a => Assert.Null(a.Details));
        Assert.Equal(0, encoder.Calls);
    }

    private sealed class CountingEncoder : IWordSenseEncoder
    {
        public int Calls { get; private set; }

        public string ModelSha256 => WordSenseModelCatalog.OnnxSha256;

        public int HiddenSize => 384;

        public ValueTask<float[]> EncodeAsync(int[] inputIds, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(new float[inputIds.Length * HiddenSize]);
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    ///     End to end: a classifier created from the model directory tells the senses of "record" apart.
    /// </summary>
    [Fact]
    public async Task WordSense_Classifier_EndToEnd_ClassifiesRecord()
    {
        using var classifier = await CreateClassifierAsync();

        var tokens = await classifier.ClassifyAsync(
            "The record was broken last year, so they will record the new album.",
            TestContext.Current.CancellationToken);

        var details = tokens.Where(t => t.Analysis.Details is not null).Select(t => t.Analysis.Details!).ToList();
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Verb], details.Where(d => d.Lemma == "record").Select(d => d.PartOfSpeech));
        Assert.Equal(["noun", "verb"], details.Where(d => d.Lemma == "record").Select(d => d.Pronunciation!.Meaning));
        Assert.Contains("record", classifier.ContextLemmas);
    }

    /// <summary>
    ///     End to end: the word-list API gives a full parallel result for every word.
    /// </summary>
    [Fact]
    public async Task WordSense_Classifier_EndToEnd_WordListApi()
    {
        using var classifier = await CreateClassifierAsync();
        string[] words = ["The", "record", "was", "broken", "so", "they", "will", "record", "it"];
        var token = TestContext.Current.CancellationToken;

        var analyses = await classifier.ClassifyAsync(words, token);

        Assert.Equal(words.Length, analyses.Length);
        Assert.Equal(words, analyses.Select(a => a.Word));
        Assert.Equal(PartOfSpeech.Noun, analyses[1].Details!.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Verb, analyses[7].Details!.PartOfSpeech);
        Assert.Equal("record", analyses[1].Details!.Lemma);
        Assert.Null(analyses[0].Details);
        Assert.False(analyses[0].Abstained);
        Assert.True(analyses[1].Margin > 0);
        Assert.NotNull(analyses[1].RunnerUp);
        Assert.Contains(analyses[1].Details, classifier.GetCandidates("record"));
        Assert.Equal(AnswerBasis.Context, analyses[1].Basis);
        Assert.Equal(AnswerBasis.Context, analyses[7].Basis);
        Assert.Same(classifier.GetCandidates("record"), analyses[1].Candidates);
    }

    /// <summary>
    ///     Creating a classifier from an empty model directory fails verification.
    /// </summary>
    [Fact]
    public async Task WordSense_Classifier_CreateAsync_RejectsUnverifiedDirectory()
    {
        var empty = Path.Combine(Path.GetTempPath(), "wordsense-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            await Assert.ThrowsAsync<FileNotFoundException>(
                () => WordSenseClassifier.CreateAsync(new WordSenseClassifierOptions { ModelDirectory = empty }, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(empty);
        }
    }

    /// <summary>
    ///     Model files whose content does not match the pinned digests are rejected with an invalid-data error.
    ///     This needs no real model.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSense_Classifier_CreateAsync_RejectsWrongContentDirectory()
    {
        var wrong = Path.Combine(Path.GetTempPath(), "wordsense-wrong-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(wrong);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(wrong, "model_int8.onnx"), "not the model", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(wrong, "vocab.txt"), "not the vocabulary", TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => WordSenseClassifier.CreateAsync(new WordSenseClassifierOptions { ModelDirectory = wrong }, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(wrong, true);
        }
    }

    /// <summary>
    ///     Invalid option values are reported before the model directory is touched.
    /// </summary>
    [Fact]
    public async Task WordSense_Classifier_CreateAsync_ValidatesOptionsBeforeModel()
    {
        var missing = Path.Combine(Path.GetTempPath(), "wordsense-missing-" + Guid.NewGuid().ToString("N"));
        var cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => WordSenseClassifier.CreateAsync(new WordSenseClassifierOptions { ModelDirectory = missing, MaxSequenceLength = 4 }, cancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => WordSenseClassifier.CreateAsync(new WordSenseClassifierOptions { ModelDirectory = missing, AbstainMarginScale = -1 }, cancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => WordSenseClassifier.CreateAsync(new WordSenseClassifierOptions { ModelDirectory = missing, AbstainMarginScale = double.NaN }, cancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => WordSenseClassifier.CreateAsync(new WordSenseClassifierOptions { ModelDirectory = missing, IntraOpThreads = -1 }, cancellationToken));
    }

    /// <summary>
    ///     Reads every fifth holdout row as the words of its sentence, the index of the target word, and the gold lemma and sense.
    /// </summary>
    /// <returns>The sampled rows.</returns>
    private static async Task<List<(string[] Words, int Target, string Lemma, string Sense)>> ReadHoldoutSampleAsync()
    {
        var root = ModelAssets.RequireRepositoryRoot();
        var result = new List<(string[], int, string, string)>();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "corpus", "holdout"), "*.tsv").Order(StringComparer.Ordinal))
        {
            var lines = await File.ReadAllLinesAsync(file, TestContext.Current.CancellationToken);
            var rows = lines.Select(l => l.TrimEnd('\r').Split('\t')).Where(c => c.Length == 3).Where((_, i) => i % 5 == 0);
            foreach (var columns in rows)
            {
                var words = new List<string>();
                var target = -1;
                foreach (var word in columns[2].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var open = word.IndexOf('{', StringComparison.Ordinal);
                    if (open < 0)
                    {
                        words.Add(word);
                        continue;
                    }

                    // A target inside a compound word is split into prefix, target and suffix
                    var close = word.IndexOf('}', StringComparison.Ordinal);
                    if (open > 0)
                    {
                        words.Add(word[..open]);
                    }

                    target = words.Count;
                    words.Add(word.Substring(open + 1, close - open - 1));
                    if (close < word.Length - 1)
                    {
                        words.Add(word[(close + 1)..]);
                    }
                }

                result.Add((words.ToArray(), target, columns[0], columns[1]));
            }
        }

        return result;
    }

    /// <summary>
    ///     Accuracy gate: the embedded table must classify the held-out sentences with high accuracy. Every
    ///     fifth holdout sentence of every corpus file is evaluated, which keeps the test quick while still
    ///     covering every source file.
    /// </summary>
    [Fact]
    public async Task WordSense_Classifier_Holdout_MeetsAccuracyGate()
    {
        using var classifier = await CreateClassifierAsync();
        var total = 0;
        var answered = 0;
        var answeredCorrect = 0;
        foreach (var (words, target, lemma, sense) in await ReadHoldoutSampleAsync())
        {
            var gold = Gold[(lemma, sense)];
            var result = (await classifier.ClassifyAsync(words, TestContext.Current.CancellationToken))[target].Details;

            total++;
            if (result is not null)
            {
                answered++;
                answeredCorrect += result.PartOfSpeech == gold.Pos && result.Pronunciation?.Meaning == gold.Meaning ? 1 : 0;
            }
        }

        Assert.True(total > 400, "The holdout sample is unexpectedly small.");
        Assert.True((double)answeredCorrect / answered >= 0.96, $"Accuracy on answered sentences was {(double)answeredCorrect / answered:P2} ({answeredCorrect}/{answered}).");
        Assert.True((double)answered / total >= 0.85, $"Coverage was {(double)answered / total:P2}.");
    }

    /// <summary>
    ///     Pronunciation accuracy gate: for heteronyms, the predicted meaning must be the meaning of the gold sense.
    /// </summary>
    [Fact]
    public async Task WordSense_Classifier_Holdout_MeetsPronunciationGate()
    {
        using var classifier = await CreateClassifierAsync();
        var total = 0;
        var answered = 0;
        var answeredCorrect = 0;
        foreach (var (words, target, lemma, sense) in await ReadHoldoutSampleAsync())
        {
            var gold = Gold[(lemma, sense)];
            if (gold.Meaning is null)
            {
                continue;
            }

            var result = (await classifier.ClassifyAsync(words, TestContext.Current.CancellationToken))[target].Details;

            total++;
            if (result is not null)
            {
                answered++;
                answeredCorrect += result.Pronunciation!.Meaning == gold.Meaning ? 1 : 0;
            }
        }

        Assert.True(total > 200, "The heteronym holdout sample is unexpectedly small.");
        Assert.True((double)answeredCorrect / answered >= 0.96, $"Pronunciation accuracy on answered sentences was {(double)answeredCorrect / answered:P2} ({answeredCorrect}/{answered}).");
        Assert.True((double)answered / total >= 0.85, $"Pronunciation coverage was {(double)answered / total:P2}.");
    }

    /// <summary>
    ///     Part-of-speech accuracy gate: the predicted part of speech must be that of the gold sense.
    /// </summary>
    [Fact]
    public async Task WordSense_Classifier_Holdout_MeetsPartOfSpeechGate()
    {
        using var classifier = await CreateClassifierAsync();
        var total = 0;
        var answered = 0;
        var answeredCorrect = 0;
        foreach (var (words, target, lemma, sense) in await ReadHoldoutSampleAsync())
        {
            var gold = Gold[(lemma, sense)];
            var result = (await classifier.ClassifyAsync(words, TestContext.Current.CancellationToken))[target].Details;

            total++;
            if (result is not null)
            {
                answered++;
                answeredCorrect += result.PartOfSpeech == gold.Pos ? 1 : 0;
            }
        }

        Assert.True(total > 400, "The holdout sample is unexpectedly small.");
        Assert.True((double)answeredCorrect / answered >= 0.97, $"Part-of-speech accuracy on answered words was {(double)answeredCorrect / answered:P2} ({answeredCorrect}/{answered}).");
        Assert.True((double)answered / total >= 0.85, $"Part-of-speech coverage was {(double)answered / total:P2}.");
    }

    /// <summary>
    ///     Honesty of the shipped table: every lemma has a part of speech from the four shipped classes, a lemma is a
    ///     heteronym exactly when its senses have at least two distinct meanings, and every candidate is exposed.
    /// </summary>
    [Fact]
    public void WordSense_EmbeddedTable_PartsOfSpeechAndHeteronymsAreConsistent()
    {
        PartOfSpeech[] shipped = [PartOfSpeech.Noun, PartOfSpeech.Verb, PartOfSpeech.Adjective, PartOfSpeech.Adverb];
        var table = WordSenseTable.LoadEmbedded();

        foreach (var lemma in table.ContextLemmas)
        {
            var meanings = lemma.Senses.Select(s => s.Meaning).Distinct().Count();
            Assert.All(lemma.Senses, s => Assert.Contains(s.Pos, shipped));
            Assert.Equal(lemma.IsHeteronym, meanings >= 2);
            Assert.All(lemma.Candidates, c => Assert.Equal(lemma.IsHeteronym, c.Pronunciation is not null));
        }
    }

    /// <summary>
    ///     The gold part of speech and meaning of every sense label of the corpus.
    /// </summary>
    private static readonly Lazy<Dictionary<(string, string), (PartOfSpeech, string?)>> GoldMap = new(ReadGold);

    private static Dictionary<(string Lemma, string Label), (PartOfSpeech Pos, string? Meaning)> Gold => GoldMap.Value;

    /// <summary>
    ///     Reads the sense inventory of the corpus.
    /// </summary>
    /// <returns>The inventory by lemma and label.</returns>
    private static Dictionary<(string, string), (PartOfSpeech, string?)> ReadGold()
    {
        var root = ModelAssets.RequireRepositoryRoot();
        var result = new Dictionary<(string, string), (PartOfSpeech, string?)>();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "corpus", "senses"), "*.tsv"))
        {
            foreach (var c in File.ReadAllLines(file).Skip(1).Select(l => l.TrimEnd('\r').Split('\t')).Where(c => c.Length >= 4))
            {
                result[(c[0], c[1])] = (Enum.Parse<PartOfSpeech>(c[2], true), c[3].Length == 0 ? null : c[3]);
            }
        }

        return result;
    }

    /// <summary>
    ///     Forms completeness: every braced target word of every anchor, holdout and real-text row must resolve
    ///     through the embedded forms table (with the possessive fallback of the classifier) to the lemma of its row.
    ///     The single documented exception is the plural <c>rows</c>, which is itself a trained lemma and therefore
    ///     cannot also map to the lemma <c>row</c>.
    /// </summary>
    [Fact]
    public async Task WordSense_EmbeddedTable_EveryCorpusTargetResolvesToItsLemma()
    {
        var root = ModelAssets.RequireRepositoryRoot();
        var table = WordSenseTable.LoadEmbedded();
        var failures = new SortedSet<string>(StringComparer.Ordinal);
        var checkedRows = 0;
        foreach (var directory in new[] { "anchors", "holdout", "realtext" })
        {
            foreach (var file in Directory.GetFiles(Path.Combine(root, "corpus", directory), "*.tsv"))
            {
                var lines = await File.ReadAllLinesAsync(file, TestContext.Current.CancellationToken);
                foreach (var columns in lines.Select(l => l.TrimEnd('\r').Split('\t')).Where(c => c.Length >= 3))
                {
                    var open = columns[2].IndexOf('{', StringComparison.Ordinal);
                    var close = columns[2].IndexOf('}', StringComparison.Ordinal);
                    if (open < 0 || close < open)
                    {
                        continue;
                    }

                    var target = columns[2].Substring(open + 1, close - open - 1).ToLowerInvariant().Replace('\u2019', '\'');
                    var lemma = columns[0];
                    checkedRows++;
                    if (lemma == "row" && target == "rows")
                    {
                        continue;
                    }

                    var resolved = table.Forms.TryGetValue(target, out var found) && found == lemma;
                    if (!resolved && target.EndsWith("'s", StringComparison.Ordinal))
                    {
                        resolved = table.Forms.TryGetValue(target[..^2], out found) && found == lemma;
                    }

                    if (!resolved)
                    {
                        failures.Add($"{target}->{lemma}");
                    }
                }
            }
        }

        Assert.True(checkedRows > 10000, "The corpus is unexpectedly small.");
        Assert.True(failures.Count == 0, $"Targets missing from corpus/forms/speech.tsv: {string.Join(", ", failures)}.");
    }

    /// <summary>
    ///     Measures median latency of one classification with one and four ONNX Runtime threads and reports it
    ///     as a diagnostic message. Run with <c>--filter-trait "Category=Benchmark"</c>.
    /// </summary>
    /// <param name="threads">The ONNX Runtime intra-op thread count.</param>
    /// <returns>A task that completes when the measurement is done.</returns>
    [Theory]
    [Trait("Category", "Benchmark")]
    [InlineData(1)]
    [InlineData(4)]
    public async Task WordSense_Benchmark_ClassifyLatency(int threads)
    {
        using var classifier = await CreateClassifierAsync(threads);
        const string text = "She will record the vote in the record book, then lead the team to the lead mine.";
        var token = TestContext.Current.CancellationToken;
        for (var i = 0; i < 20; i++)
        {
            await classifier.ClassifyAsync(text, token);
        }

        var samples = new List<double>();
        for (var i = 0; i < 100; i++)
        {
            var watch = Stopwatch.StartNew();
            await classifier.ClassifyAsync(text, token);
            samples.Add(watch.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        TestContext.Current.TestOutputHelper?.WriteLine($"classify latency, {threads} thread(s): median {samples[50]:0.0} ms, p95 {samples[94]:0.0} ms");
        Assert.True(samples[50] < 2000);
    }
}
