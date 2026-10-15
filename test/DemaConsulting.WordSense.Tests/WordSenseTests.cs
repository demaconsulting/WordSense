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
    ///     End to end: a classifier created from the model directory tells the senses of "record" apart.
    /// </summary>
    [Fact]
    public async Task WordSense_Classifier_EndToEnd_ClassifiesRecord()
    {
        using var classifier = await CreateClassifierAsync();

        var tokens = await classifier.ClassifyAsync(
            "The record was broken last year, so they will record the new album.",
            TestContext.Current.CancellationToken);

        var senses = tokens.Where(t => t.Sense is not null).Select(t => t.Sense!).ToList();
        Assert.Equal(["noun", "verb"], senses.Where(s => s.Lemma == "record").Select(s => s.Sense));
        Assert.Contains("record", classifier.KnownLemmas);
    }

    /// <summary>
    ///     End to end: the word-list API gives parallel senses, tags and details.
    /// </summary>
    [Fact]
    public async Task WordSense_Classifier_EndToEnd_WordListApi()
    {
        using var classifier = await CreateClassifierAsync();
        string[] words = ["The", "record", "was", "broken", "so", "they", "will", "record", "it"];
        var token = TestContext.Current.CancellationToken;

        var senses = await classifier.ClassifyAsync(words, token);
        var tags = await classifier.ClassifyTagAsync(words, ["noun", "verb"], token);
        var isVerb = await classifier.IsTagAsync(words, "verb", token);
        var details = await classifier.ClassifyDetailAsync(words, token);

        Assert.Equal(words.Length, senses.Length);
        Assert.Equal("noun", senses[1]!.Sense);
        Assert.Equal("verb", senses[7]!.Sense);
        Assert.Equal("noun", tags[1]);
        Assert.Equal("verb", tags[7]);
        Assert.False(isVerb[1]);
        Assert.True(isVerb[7]);
        Assert.Null(senses[0]);
        Assert.Same(senses[1], details[1]!.Value.Sense);
    }

    /// <summary>
    ///     Creating a classifier from a damaged model directory fails verification.
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
        foreach (var (words, target, _, sense) in await ReadHoldoutSampleAsync())
        {
            var result = (await classifier.ClassifyAsync(words, TestContext.Current.CancellationToken))[target];

            total++;
            if (result is not null)
            {
                answered++;
                answeredCorrect += result.Sense == sense ? 1 : 0;
            }
        }

        Assert.True(total > 400, "The holdout sample is unexpectedly small.");
        Assert.True((double)answeredCorrect / answered >= 0.96, $"Accuracy on answered sentences was {(double)answeredCorrect / answered:P2} ({answeredCorrect}/{answered}).");
        Assert.True((double)answered / total >= 0.85, $"Coverage was {(double)answered / total:P2}.");
    }

    /// <summary>
    ///     Group-level accuracy gate: a prediction counts as right when the predicted sense belongs to the group of the gold sense.
    /// </summary>
    [Fact]
    public async Task WordSense_Classifier_Holdout_MeetsGroupGate()
    {
        using var classifier = await CreateClassifierAsync();
        var total = 0;
        var answered = 0;
        var answeredCorrect = 0;
        foreach (var (words, target, lemma, sense) in await ReadHoldoutSampleAsync())
        {
            var gold = classifier.GetSenses(lemma).Single(s => s.Sense == sense);
            var result = (await classifier.ClassifyAsync(words, TestContext.Current.CancellationToken))[target];

            total++;
            if (result is not null)
            {
                answered++;
                answeredCorrect += result.Group == gold.Group ? 1 : 0;
            }
        }

        Assert.True(total > 400, "The holdout sample is unexpectedly small.");
        Assert.True((double)answeredCorrect / answered >= 0.96, $"Group accuracy on answered sentences was {(double)answeredCorrect / answered:P2} ({answeredCorrect}/{answered}).");
        Assert.True((double)answered / total >= 0.85, $"Group coverage was {(double)answered / total:P2}.");
    }

    /// <summary>
    ///     Part-of-speech honesty: every sense of the embedded table has exactly one part of speech and no stand-in tags.
    /// </summary>
    [Fact]
    public void WordSense_EmbeddedTable_EverySenseHasExactlyOnePartOfSpeech()
    {
        string[] partsOfSpeech = ["adj", "adv", "noun", "verb"];
        string[] qualifiers = ["comparative", "participle", "past", "present", "superlative"];
        var table = WordSenseTable.LoadEmbedded();

        foreach (var sense in table.Lemmas.SelectMany(l => l.Senses))
        {
            Assert.True(sense.Tags.Count(partsOfSpeech.Contains) == 1, $"{sense} must have exactly one part of speech but has [{string.Join(", ", sense.Tags)}].");
            Assert.True(sense.Tags.All(t => partsOfSpeech.Contains(t) || qualifiers.Contains(t)), $"{sense} has a tag that is not a part of speech or a tense/degree qualifier.");
        }
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
    ///     Tag-level accuracy gate: telling noun from verb must be at least as accurate as telling the senses apart.
    ///     Rows whose gold sense carries both or neither tag are skipped.
    /// </summary>
    [Fact]
    public async Task WordSense_Classifier_Holdout_MeetsNounVerbTagGate()
    {
        using var classifier = await CreateClassifierAsync();
        var total = 0;
        var answered = 0;
        var answeredCorrect = 0;
        foreach (var (words, target, lemma, sense) in await ReadHoldoutSampleAsync())
        {
            var gold = classifier.GetSenses(lemma).Single(s => s.Sense == sense);
            if (gold.HasTag("noun") == gold.HasTag("verb"))
            {
                continue;
            }

            var tag = (await classifier.ClassifyTagAsync(words, ["noun", "verb"], TestContext.Current.CancellationToken))[target];

            total++;
            if (tag is not null)
            {
                answered++;
                answeredCorrect += gold.HasTag(tag) ? 1 : 0;
            }
        }

        Assert.True(total > 350, "The noun/verb holdout sample is unexpectedly small.");
        Assert.True((double)answeredCorrect / answered >= 0.97, $"Tag accuracy on answered words was {(double)answeredCorrect / answered:P2} ({answeredCorrect}/{answered}).");
        Assert.True((double)answered / total >= 0.85, $"Tag coverage was {(double)answered / total:P2}.");
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
