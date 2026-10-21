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
    ///     lemma is trained, a word with several keys is left out, and an unknown key is counted as unmapped.
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
        Assert.Equal([("zzzzzz%1:03:00::", 1)], funnel.Unmapped);
        Assert.Empty(none);
        Assert.Equal((3, 2, 0), (noneFunnel.SingleKey, noneFunnel.Mapped, noneFunnel.Trained));
        var (excluded, excludedFunnel) = SenseEvaluator.Select(sentences, counts, new HashSet<string> { "record" }, 1, new HashSet<string> { Corpus.Normalize(string.Join(' ', sentences[0].Words)) });
        Assert.Empty(excluded);
        Assert.Equal(1, excludedFunnel.LeftOutSentences);
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
        var funnel = new SenseEvalFunnel(new SemCorCounts(10, 200, 20, 5, 175), 170, 150, 100, [("a%1:00:00::", 3)], 2);
        var empty = Evaluator.Summarize("semcor", []);

        // Act
        var text = new SenseEvalResult(funnel, [new SenseEvalModelResult("Basic", empty, 0, 0, 0)]).Render();

        // Assert
        Assert.Contains("| annotated words | 200 |", text, StringComparison.Ordinal);
        Assert.Contains("mapping coverage 88.24%", text, StringComparison.Ordinal);
        Assert.Contains("a%1:00:00:: (3)", text, StringComparison.Ordinal);
        Assert.Equal("n/a (top-1 n/a)", SenseEvalResult.TableCell(new SenseEvalModelResult("Basic", empty, 0, 0, 0)));
    }
}
