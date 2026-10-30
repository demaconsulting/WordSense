using System.Collections.Immutable;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the independent sense evaluation: the reader of SemCor, the mapping of its sense keys, the measurement of
///     a model and the download with its pinned checksum.
/// </summary>
public sealed class SenseEvaluatorTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly byte[] _body;

        public StubHandler(byte[] body)
        {
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_body) });
    }

    private static string TagFile(SenseInfo noun, SenseInfo other) =>
        "<contextfile concordance=brown>\n<context filename=br-t01 paras=yes>\n<p pnum=1>\n<s snum=1>\n" +
        "<wf cmd=ignore pos=DT>The</wf>\n" +
        $"<wf cmd=done pos=NN lemma={noun.Lemma} wnsn=1 lexsn={LexSn(noun)}>{noun.Lemma}</wf>\n" +
        "<wf cmd=ignore pos=VB>was</wf>\n" +
        $"<wf cmd=done pos=NN lemma={other.Lemma} wnsn=2 lexsn={LexSn(other)}>{other.Lemma}</wf>\n" +
        "<wf cmd=done pos=NNP lemma=group wnsn=1 lexsn=1:03:00:: pn=group>Fulton_County_Grand_Jury</wf>\n" +
        "<wf cmd=done pos=NN lemma=gone wnsn=0 lexsn=1:00:00::>gone</wf>\n" +
        "<wf cmd=done pos=NN lemma=nothing wnsn=1 lexsn=1:03:00::;1:03:01::>nothing</wf>\n" +
        "<wf cmd=done pos=NN lemma=zzzzzz wnsn=1 lexsn=1:03:00::>zzzzzz</wf>\n" +
        "<wf cmd=ignore pos=RB>n't</wf>\n<punc>.</punc>\n</s>\n<s snum=2>\n<wf cmd=ignore pos=DT>No</wf>\n<punc>.</punc>\n</s>\n</p>\n</context>\n</contextfile>\n";

    private static string LexSn(SenseInfo sense)
    {
        var key = sense.SenseKey!;
        return key[(key.IndexOf('%', StringComparison.Ordinal) + 1)..];
    }

    private static (SenseInfo Noun, SenseInfo Other) TwoSenses()
    {
        var senses = SenseFixtures.Senses("record", PartOfSpeech.Noun, 2);
        Assert.NotNull(senses[0].SenseKey);
        Assert.NotNull(senses[1].SenseKey);
        return (senses[0], senses[1]);
    }

    /// <summary>
    ///     Writes a one-sentence SemCor tag file whose words are the given senses, each annotated with its WordNet 3.0 key.
    /// </summary>
    /// <param name="senses">The senses of the words, in sentence order.</param>
    /// <returns>The text of the tag file.</returns>
    private static string Annotated(params SenseInfo[] senses) =>
        "<contextfile concordance=brown>\n<context filename=br-t02 paras=yes>\n<p pnum=1>\n<s snum=1>\n" +
        string.Concat(senses.Select(s => $"<wf cmd=done pos=NN lemma={s.Lemma} wnsn=1 lexsn={LexSn(s)}>{s.Lemma}</wf>\n")) +
        "<punc>.</punc>\n</s>\n</p>\n</context>\n</contextfile>\n";

    /// <summary>
    ///     Finds a keyed sense of a lemma that has exactly one sense in the catalog over all parts of speech.
    /// </summary>
    /// <returns>The only sense of the first candidate lemma that has one.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no candidate lemma has exactly one sense.</exception>
    private static SenseInfo MonosemousSense()
    {
        foreach (var lemma in new[] { "aardvark", "armadillo", "chimpanzee", "hippopotamus", "xylophone", "zucchini", "walrus", "wombat" })
        {
            var all = Enum.GetValues<PartOfSpeech>().SelectMany(pos => WordLexicon.SensesOf(lemma, pos)).ToList();
            if (all.Count == 1 && all[0].SenseKey is not null)
            {
                return all[0];
            }
        }

        throw new InvalidOperationException("None of the candidate lemmas has exactly one sense in the catalog.");
    }

    /// <summary>
    ///     The reader finds the single-word annotations, builds their WordNet 3.0 sense keys from the lemma and the lexical
    ///     sense number, leaves out multiword expressions and senses that the mapping dropped, and keeps punctuation as words.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseEval_Parse_ReadsSingleWordAnnotationsAndCountsTheRest()
    {
        // Arrange
        var (noun, other) = TwoSenses();

        // Act
        var (sentences, counts) = SemCor.Parse([("br-t01", TagFile(noun, other))]);

        // Assert: one sentence with annotations (the second has none)
        var sentence = Assert.Single(sentences);
        Assert.Equal("br-t01:1", sentence.Id);
        Assert.Equal(["The", "record", "was", "record", "Fulton", "County", "Grand", "Jury", "gone", "nothing", "zzzzzz", "n't", "."], sentence.Words);
        Assert.Equal([1, 3, 9, 10], sentence.Targets.Select(t => t.Index));
        Assert.Equal(noun.SenseKey, sentence.Targets[0].SenseKeys.Single());
        Assert.Equal(2, sentence.Targets[2].SenseKeys.Count);
        Assert.Equal(new SemCorCounts(1, 6, 1, 1, 4), counts);
    }

    /// <summary>
    ///     A WordNet 3.0 key becomes the sense of the catalog that has the key; the word is an evaluation word only when its
    ///     lemma is trained (or has two or more senses, see the all-words test), a word with several keys is left out, and an unknown key is counted as unmapped.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseEval_MapKeys_WordNet30KeyToOewnSense()
    {
        // Arrange
        var (noun, other) = TwoSenses();
        var (sentences, counts) = SemCor.Parse([("br-t01", TagFile(noun, other))]);

        // Act
        var (selected, funnel) = SenseEvaluator.Select(sentences, counts, new HashSet<string> { "record" });
        var (none, noneFunnel) = SenseEvaluator.Select(sentences, counts, new HashSet<string> { "other" });

        // Assert: both record words map to their senses; "nothing" has two keys; "zzzzzz" has no sense
        var rows = Assert.Single(selected).Rows;
        Assert.Equal([noun.SenseId, other.SenseId], rows.Select(r => r.SenseId));
        Assert.Equal([1, 3], rows.Select(r => r.TargetWordIndex));
        Assert.Equal([PartOfSpeech.Noun, PartOfSpeech.Noun], rows.Select(r => r.PartOfSpeech));
        Assert.Equal((3, 2, 2), (funnel.SingleKey, funnel.Mapped, funnel.Trained));
        Assert.Equal((1, 0), (funnel.MultiKey, funnel.SampledOut));
        Assert.Equal([("zzzzzz%1:03:00::", 1)], funnel.Unmapped);
        var untrainedSentence = Assert.Single(none);
        Assert.Empty(untrainedSentence.Rows);
        Assert.Equal(2, untrainedSentence.Others.Count);
        Assert.Equal((3, 2, 0), (noneFunnel.SingleKey, noneFunnel.Mapped, noneFunnel.Trained));
        var (excluded, excludedFunnel) = SenseEvaluator.Select(sentences, counts, new HashSet<string> { "record" }, 1, new HashSet<string> { Corpus.Normalize(string.Join(' ', sentences[0].Words)) });
        Assert.Empty(excluded);
        Assert.Equal(1, excludedFunnel.LeftOutSentences);

        // The word with two keys is counted directly: leaving its sentence out does not make a single-key word of it
        Assert.Equal(1, excludedFunnel.MultiKey);
        var (_, sampledFunnel) = SenseEvaluator.Select([sentences[0], sentences[0], sentences[0]], counts, new HashSet<string> { "record" }, 2);
        Assert.Equal(1, sampledFunnel.SampledOut);
    }

    /// <summary>
    ///     The measurement reports, for a model that answers one word at the sense level (right) and one at the part-of-speech
    ///     level, the share answered at each level, the precision, the fine top-1, the group top-1 and the most-frequent-sense baseline.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_SenseEval_Run_ReportsShareAndPrecisionPerLevelAndBaseline()
    {
        // Arrange: the first word is answered with its sense, the second (the second sense) with the part of speech and the first sense on top
        var (noun, other) = TwoSenses();
        var (sentences, counts) = SemCor.Parse([("br-t01", TagFile(noun, other))]);
        var (selected, funnel) = SenseEvaluator.Select(sentences, counts, new HashSet<string> { "record" });
        var details = new WordDetails("record", PartOfSpeech.Noun, string.Empty);
        ImmutableArray<AnalysisCandidate> Analyses(string first, string second) =>
            [new AnalysisCandidate(details, 1.0, [new SenseCandidate(first, 0.7), new SenseCandidate(second, 0.3)])];
        using var model = new FakeModel(words => [.. words.Select((w, i) => i switch
        {
            1 => new ClassifyResult(w, true, Analyses(noun.SenseId, other.SenseId), details, SenseLevel.Sense, 0.95, noun.GroupId, noun.SenseId),
            3 => new ClassifyResult(w, true, Analyses(noun.SenseId, other.SenseId), details, SenseLevel.PartOfSpeech, 0.8, null, null),
            _ => ClassifyResult.Unknown(w),
        })]);

        // Act
        var measured = await SenseEvaluator.MeasureAsync("Fake", model, selected, TextWriter.Null, TestContext.Current.CancellationToken);
        var text = new SenseEvalResult(funnel, [measured]).Render();

        // Assert
        Assert.Equal(2, measured.Set.Rows);
        Assert.Equal(new LevelCount(1, 1), measured.Set.Sense);
        Assert.Equal(new LevelCount(1, 1), measured.Set.PartOfSpeech);
        Assert.Equal(1, measured.Set.FineTop1);
        Assert.Equal(1, measured.MostFrequentSense);
        Assert.Contains("| Fake | 2 | 50.00% | 0.00% | 50.00% | 0.00% |", text, StringComparison.Ordinal);
        Assert.Contains("mapping coverage 66.67%", text, StringComparison.Ordinal);
        Assert.Contains("optimistic", text, StringComparison.Ordinal);
        Assert.Equal("50.00% @ 100.00% (top-1 50.00%)", SenseEvalResult.TableCell(measured));
    }

    /// <summary>
    ///     The selection keeps the mapped words of untrained lemmas with two or more senses as other rows, counts the words of an
    ///     untrained lemma with one sense in the funnel without keeping them, and keeps the trained-lemma rows as they were,
    ///     including the words of a trained lemma with one sense.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseEval_Select_AllWords_KeepsUntrainedPolysemousAndCountsTheRest()
    {
        // Arrange: a sentence with a sense of "record" (two or more senses) and the only sense of a one-sense lemma
        var (noun, _) = TwoSenses();
        var mono = MonosemousSense();
        var (sentences, counts) = SemCor.Parse([("br-t02", Annotated(noun, mono))]);

        // Act
        var (untrained, untrainedFunnel) = SenseEvaluator.Select(sentences, counts, new HashSet<string>());
        var (trained, trainedFunnel) = SenseEvaluator.Select(sentences, counts, new HashSet<string> { "record" });
        var (both, bothFunnel) = SenseEvaluator.Select(sentences, counts, new HashSet<string> { "record", mono.Lemma });

        // Assert: untrained, "record" is an other row and the one-sense lemma is only counted
        var sentence = Assert.Single(untrained);
        Assert.Empty(sentence.Rows);
        Assert.Equal([noun.SenseId], sentence.Others.Select(r => r.SenseId));
        Assert.Equal((2, 0, 1, 1), (untrainedFunnel.Mapped, untrainedFunnel.Trained, untrainedFunnel.UntrainedPolysemous, untrainedFunnel.Monosemous));

        // Assert: trained, "record" is a trained-lemma row as before and no other row is left
        var trainedSentence = Assert.Single(trained);
        Assert.Equal([noun.SenseId], trainedSentence.Rows.Select(r => r.SenseId));
        Assert.Empty(trainedSentence.Others);
        Assert.Equal((2, 1, 0, 1), (trainedFunnel.Mapped, trainedFunnel.Trained, trainedFunnel.UntrainedPolysemous, trainedFunnel.Monosemous));

        // Assert: a trained lemma with one sense is a trained-lemma row, not left out
        var bothSentence = Assert.Single(both);
        Assert.Equal([noun.SenseId, mono.SenseId], bothSentence.Rows.Select(r => r.SenseId));
        Assert.Empty(bothSentence.Others);
        Assert.Equal((2, 2, 0, 0), (bothFunnel.Mapped, bothFunnel.Trained, bothFunnel.UntrainedPolysemous, bothFunnel.Monosemous));
    }

    /// <summary>
    ///     A word of an untrained lemma that a model answers at the part-of-speech level counts as no sense answer, its fine top-1
    ///     is the most probable ranked sense, and the trained-lemma figures do not change.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_SenseEval_Run_UntrainedLemma_CountsNoSenseAnswerAndFineTop1FromRankedSenses()
    {
        // Arrange: "record" is trained and answered with its sense; the second word is a sense of "line" (untrained) answered at the
        // part-of-speech level with that sense on top
        var record = SenseFixtures.Senses("record", PartOfSpeech.Noun, 1)[0];
        var line = SenseFixtures.Senses("line", PartOfSpeech.Noun, 2)[1];
        var (sentences, counts) = SemCor.Parse([("br-t02", Annotated(record, line))]);
        var (selected, _) = SenseEvaluator.Select(sentences, counts, new HashSet<string> { "record" });
        var recordDetails = new WordDetails("record", PartOfSpeech.Noun, string.Empty);
        var lineDetails = new WordDetails("line", PartOfSpeech.Noun, string.Empty);
        using var model = new FakeModel(words => [.. words.Select((w, i) => i switch
        {
            0 => new ClassifyResult(w, true, [], recordDetails, SenseLevel.Sense, 0.95, record.GroupId, record.SenseId),
            1 => new ClassifyResult(w, true, [new AnalysisCandidate(lineDetails, 1.0, [new SenseCandidate(line.SenseId, 0.6)])], lineDetails, SenseLevel.PartOfSpeech, 0.8, null, null),
            _ => ClassifyResult.Unknown(w),
        })]);

        // Act
        var measured = await SenseEvaluator.MeasureAsync("Fake", model, selected, TextWriter.Null, TestContext.Current.CancellationToken);

        // Assert: the trained-lemma set is only the first word
        Assert.Equal(1, measured.Set.Rows);
        Assert.Equal(new LevelCount(1, 1), measured.Set.Sense);
        Assert.Equal(0, measured.Set.FineTop1);
        var all = measured.AllWords!;
        Assert.Equal(1, all.Untrained.Rows);
        Assert.Equal(new LevelCount(0, 0), all.Untrained.Sense);
        Assert.Equal(new LevelCount(1, 1), all.Untrained.PartOfSpeech);
        Assert.Equal(1, all.Untrained.FineTop1);
        Assert.Equal(2, all.All.Rows);
        Assert.Equal(new LevelCount(1, 1), all.All.Sense);
        Assert.Equal(1, all.All.FineTop1);
    }

    /// <summary>
    ///     The tag-count baseline chooses the sense with the highest tag count of the gold lemma and part of speech, the first sense
    ///     in the inventory order when counts tie or are missing, and counts the trained and the untrained words separately.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_SenseEval_Run_TagCountBaseline_UsesTheHighestCountAndInventoryOrderOnTies()
    {
        // Arrange: the gold word is the second sense of "record" in the inventory order
        var (first, second) = TwoSenses();
        var (sentences, counts) = SemCor.Parse([("br-t02", Annotated(second))]);
        var (selected, _) = SenseEvaluator.Select(sentences, counts, new HashSet<string>());
        using var model = new FakeModel(words => [.. words.Select(w => ClassifyResult.Unknown(w))]);
        var higher = new Dictionary<string, int> { [first.SenseKey!] = 1, [second.SenseKey!] = 5 };
        var tie = new Dictionary<string, int> { [first.SenseKey!] = 2, [second.SenseKey!] = 2 };

        // Act
        var byCount = await SenseEvaluator.MeasureAsync("Fake", model, selected, TextWriter.Null, TestContext.Current.CancellationToken, higher);
        var byTie = await SenseEvaluator.MeasureAsync("Fake", model, selected, TextWriter.Null, TestContext.Current.CancellationToken, tie);
        var byNone = await SenseEvaluator.MeasureAsync("Fake", model, selected, TextWriter.Null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((1, 0), (byCount.AllWords!.TagCountUntrained, byCount.AllWords.TagCountTrained));
        Assert.Equal(0, byTie.AllWords!.TagCountUntrained);
        Assert.Equal(0, byNone.AllWords!.TagCountUntrained);
        Assert.Equal(1, byCount.AllWords.All.Rows);
        Assert.Equal(0, byCount.AllWords.All.FineTop1);
    }

    /// <summary>
    ///     The report has the all-words section with the trained, untrained and all rows of each model, the share of the words
    ///     that are of a trained lemma, and the statements that the tag-count baseline is optimistic because the counts include SemCor
    ///     and that the usage prior of the models, made of the same counts, makes their untrained fine top-1 circular on SemCor.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_SenseEval_Render_AllWords_ShowsTrainedShareAndCircularityCaveat()
    {
        // Arrange: one trained and one untrained word, the model answers none of them
        var record = SenseFixtures.Senses("record", PartOfSpeech.Noun, 1)[0];
        var line = SenseFixtures.Senses("line", PartOfSpeech.Noun, 2)[1];
        var (sentences, counts) = SemCor.Parse([("br-t02", Annotated(record, line))]);
        var (selected, funnel) = SenseEvaluator.Select(sentences, counts, new HashSet<string> { "record" });
        using var model = new FakeModel(words => [.. words.Select(w => ClassifyResult.Unknown(w))]);
        var measured = await SenseEvaluator.MeasureAsync("Fake", model, selected, TextWriter.Null, TestContext.Current.CancellationToken, new Dictionary<string, int>());

        // Act
        var text = new SenseEvalResult(funnel, [measured]).Render();

        // Assert
        Assert.Contains("### All words", text, StringComparison.Ordinal);
        Assert.Contains("share of words of a trained lemma 50.00%", text, StringComparison.Ordinal);
        Assert.Contains("| Fake | trained | 1 |", text, StringComparison.Ordinal);
        Assert.Contains("| Fake | untrained | 1 |", text, StringComparison.Ordinal);
        Assert.Contains("| Fake | all | 2 |", text, StringComparison.Ordinal);
        Assert.Contains("optimistic reference", text, StringComparison.Ordinal);
        Assert.Contains("counted on SemCor", text, StringComparison.Ordinal);
        Assert.Contains("circular on SemCor too", text, StringComparison.Ordinal);
        Assert.Contains("evaluate-allwords", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A download whose SHA-256 differs from the pin is rejected and leaves no file; the pinned bytes are kept.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_SenseEval_Download_WrongHash_IsRejected()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("not the archive");
        using var client = new HttpClient(new StubHandler(bytes));
        var destination = Path.Join(_dir.Root, "cache", SemCor.ArchiveName);

        // Act and assert: the wrong pin is rejected and nothing is left behind
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => SemCor.DownloadAsync(client, new Uri("https://example.invalid/semcor.tar.gz"), destination, SemCor.ArchiveSha256, TestContext.Current.CancellationToken));
        Assert.Contains("not the pinned", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".part"));

        // The right pin is kept
        await SemCor.DownloadAsync(client, new Uri("https://example.invalid/semcor.tar.gz"), destination, Convert.ToHexString(SHA256.HashData(bytes)), TestContext.Current.CancellationToken);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     The archive is verified before it is read, and its tag files are found in a gzip-compressed tar.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseEval_ReadArchive_VerifiesTheHashAndReadsTheTagFiles()
    {
        // Arrange: a tar.gz with one tag file below semcor3.0/brown1/tagfiles
        var (noun, other) = TwoSenses();
        var path = Path.Join(_dir.Root, "semcor.tar.gz");
        using (var file = File.Create(path))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var tar = new System.Formats.Tar.TarWriter(gzip))
        {
            tar.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "semcor3.0/brown1/tagfiles/br-t01")
            {
                DataStream = new MemoryStream(Encoding.Latin1.GetBytes(TagFile(noun, other))),
            });
        }

        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        // Act
        var (sentences, counts) = SemCor.ReadArchive(path, hash);

        // Assert
        Assert.Single(sentences);
        Assert.Equal(4, counts.Used);
        Assert.Throws<InvalidDataException>(() => SemCor.ReadArchive(path, new string('0', 64)));
    }

    /// <summary>
    ///     Without the SemCor archive in the cache and without <c>--download</c> the command fails with a message that says how to get it.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_EvaluateSenses_WithoutCache_ReportsUsage()
    {
        // Arrange
        using var output = new StringWriter();
        using var error = new StringWriter();

        // Act
        var code = await Program.RunAsync(["evaluate-senses", "--cache", _dir.Root, "--model", "basic"], output, error, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, code);
        Assert.Contains("--download", error.ToString(), StringComparison.Ordinal);
        Assert.Contains(SemCor.ArchiveName, error.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, output.ToString());
    }

    /// <summary>
    ///     The funnel renders the counts and the table cell reads <c>n/a</c> for no words.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseEval_Render_FormatsTheFunnel()
    {
        // Arrange
        var funnel = new SenseEvalFunnel(new SemCorCounts(10, 200, 20, 5, 175), 170, 150, 100, [("a%1:00:00::", 3)], 2, 30, 20, 7, 3);
        var empty = Evaluator.Summarize("semcor", []);

        // Act
        var text = new SenseEvalResult(funnel, [new SenseEvalModelResult("Basic", empty, 0, 0, 0)]).Render();

        // Assert
        Assert.Contains("| annotated words | 200 |", text, StringComparison.Ordinal);
        Assert.Contains("| left out: more than one sense key | 7 |", text, StringComparison.Ordinal);
        Assert.Contains("not in the sample (the words they hold are not counted below) | 3 |", text, StringComparison.Ordinal);
        Assert.Contains("mapping coverage 88.24%", text, StringComparison.Ordinal);
        Assert.Contains("a%1:00:00:: (3)", text, StringComparison.Ordinal);
        Assert.Contains("(share of words of a trained lemma 76.92%) | 130 |", text, StringComparison.Ordinal);
        Assert.Contains("fewer than two senses | 20 |", text, StringComparison.Ordinal);
        Assert.DoesNotContain("### All words", text, StringComparison.Ordinal);
        Assert.Equal("n/a (top-1 n/a)", SenseEvalResult.TableCell(new SenseEvalModelResult("Basic", empty, 0, 0, 0)));
    }
}
