using System.Runtime.InteropServices;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.MiniLM;
using DemaConsulting.WordSense.Onnx.Tests;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the CPU canary (the <c>canary</c> command) and the CPU preflight of the weights and head commands.
/// </summary>
public sealed class CanaryTests : IDisposable
{
    private static readonly string[] Sentence = ["He", "went", "to", "the", "bank", "."];

    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static CanaryEntry Entry(string id, params string[] answers) =>
        new(id, [.. Enumerable.Range(0, answers.Length).Select(i => "w" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))], answers);

    /// <summary>
    ///     The sentence file may have more columns: the first is the id, the last the words.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Canary_ReadSentences_FirstColumnIdLastColumnWords()
    {
        // Arrange: the layout of the sentence list of the CPU variation study (id, kind, target, words) and a two-column line
        var path = _dir.Write("sentences.tsv", "# comment\nH-use-1\tsuite\t4\tNobody could find a use .\n\nplain\tOne two\n");

        // Act
        var sentences = Canary.ReadSentences(path);

        // Assert
        Assert.Equal(["H-use-1", "plain"], sentences.Select(s => s.Id));
        Assert.Equal(["Nobody", "could", "find", "a", "use", "."], sentences[0].Words);
        Assert.Equal(["One", "two"], sentences[1].Words);
    }

    /// <summary>
    ///     A line without words, a repeated id and an empty file are refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Canary_ReadSentences_Malformed_Throws()
    {
        // Arrange
        var noWords = _dir.Write("a.tsv", "id-only\n");
        var twice = _dir.Write("b.tsv", "x\ta b\nx\tc d\n");
        var empty = _dir.Write("c.tsv", "# nothing\n");

        // Act / Assert
        Assert.Contains("needs an id and words", Assert.Throws<InvalidDataException>(() => Canary.ReadSentences(noWords)).Message, StringComparison.Ordinal);
        Assert.Contains("used twice", Assert.Throws<InvalidDataException>(() => Canary.ReadSentences(twice)).Message, StringComparison.Ordinal);
        Assert.Contains("no sentence", Assert.Throws<InvalidDataException>(() => Canary.ReadSentences(empty)).Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A canary file reads back what was rendered: the header and the answer of every word.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Canary_RenderAndRead_RoundTrips()
    {
        // Arrange
        CanaryEntry[] entries =
        [
            new("s1", ["a", "bank", "."], ["Sense:a#determiner", "Group:bank:financial", "-"]),
            new("s2", ["go"], ["PartOfSpeech:go|go/Verb"]),
        ];
        var path = _dir.Write("canary.tsv", Canary.Render([new("model", "bert-base-cased"), new("encoder-sha256", "ab12")], entries));

        // Act
        var file = Canary.Read(path);

        // Assert: keys with a colon survive, and the text uses line feeds only
        Assert.Equal("bert-base-cased", file.Header["model"]);
        Assert.Equal("ab12", file.Header["encoder-sha256"]);
        Assert.Equal(entries.Select(e => e.Id), file.Entries.Select(e => e.Id));
        Assert.Equal(entries[0].Answers, file.Entries[0].Answers);
        Assert.Equal(entries[1].Words, file.Entries[1].Words);
        Assert.DoesNotContain('\r', File.ReadAllText(path));
    }

    /// <summary>
    ///     A line with another number of answers than words is refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Canary_Read_AnswerCountDiffersFromWordCount_Throws()
    {
        // Arrange
        var path = _dir.Write("canary.tsv", "s1\ta b\tSense:x\n");

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => Canary.Read(path));
    }

    /// <summary>
    ///     The comparison lists every word whose answer differs, with both answers.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Canary_Compare_ListsDifferingWords()
    {
        // Arrange
        CanaryEntry[] expected = [Entry("s1", "Sense:a", "PartOfSpeech:b"), Entry("s2", "Sense:c")];
        CanaryEntry[] actual = [Entry("s1", "Sense:a", "Sense:b2"), Entry("s2", "Sense:c")];

        // Act
        var flips = Canary.Compare(expected, actual);

        // Assert
        var flip = Assert.Single(flips);
        Assert.Equal(new CanaryFlip("s1", 1, "w1", "PartOfSpeech:b", "Sense:b2"), flip);
        Assert.Empty(Canary.Compare(expected, expected));
    }

    /// <summary>
    ///     Sentences that do not correspond are an error, not a difference.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Canary_Compare_OtherSentences_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<InvalidDataException>(() => Canary.Compare([Entry("s1", "x")], []));
        Assert.Throws<InvalidDataException>(() => Canary.Compare([Entry("s1", "x")], [Entry("s2", "x")]));
        Assert.Throws<InvalidDataException>(() => Canary.Compare([Entry("s1", "x")], [Entry("s1", "x", "y")]));
    }

    /// <summary>
    ///     x86-64 allows no difference, ARM allows the tolerance, and another architecture is not judged.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Canary_Judge_ByArchitecture()
    {
        // Arrange / Act / Assert
        Assert.True(Canary.Judge(0, Architecture.X64, 40).Passed);
        Assert.False(Canary.Judge(1, Architecture.X64, 40).Passed);
        Assert.Equal(0, Canary.Judge(1, Architecture.X64, 40).Limit);
        Assert.True(Canary.Judge(0, Architecture.X86, 40).Passed);
        Assert.False(Canary.Judge(1, Architecture.X86, 40).Passed);
        Assert.Equal(0, Canary.Judge(1, Architecture.X86, 40).Limit);
        Assert.True(Canary.Judge(40, Architecture.Arm64, 40).Passed);
        Assert.False(Canary.Judge(41, Architecture.Arm64, 40).Passed);
        Assert.Equal(40, Canary.Judge(41, Architecture.Arm64, 40).Limit);
        var other = Canary.Judge(500, Architecture.Wasm, 40);
        Assert.False(other.Applicable);
        Assert.True(other.Passed);
    }

    /// <summary>
    ///     The tolerance is the option, else the header, else the default; a header that is not a number of words is
    ///     refused, and so is a negative one, even when the option would override it.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Canary_ResolveArmTolerance_OptionHeaderDefault_AndMalformedHeader_Throws()
    {
        // Arrange
        var none = new Dictionary<string, string>();
        var header = new Dictionary<string, string> { ["arm-tolerance"] = "12" };

        // Act / Assert: option, header, default
        Assert.Equal(7, Canary.ResolveArmTolerance(header, 7));
        Assert.Equal(12, Canary.ResolveArmTolerance(header, null));
        Assert.Equal(Canary.DefaultArmTolerance, Canary.ResolveArmTolerance(none, null));
        Assert.Equal(0, Canary.ResolveArmTolerance(new Dictionary<string, string> { ["arm-tolerance"] = "0" }, null));

        // Option: a value that is not a non-negative number is refused, a negative one too
        Assert.Throws<ArgumentOutOfRangeException>(() => Canary.ResolveArmTolerance(header, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Canary.ResolveArmTolerance(none, int.MinValue));
        Assert.Equal(0, Canary.ResolveArmTolerance(header, 0));

        // Malformed headers do not fall back to the default
        foreach (var bad in new[] { "-1", "forty", string.Empty, "1.5", "99999999999" })
        {
            var malformed = new Dictionary<string, string> { ["arm-tolerance"] = bad };
            Assert.Contains("arm-tolerance", Assert.Throws<InvalidDataException>(() => Canary.ResolveArmTolerance(malformed, null)).Message, StringComparison.Ordinal);
            Assert.Throws<InvalidDataException>(() => Canary.ResolveArmTolerance(malformed, 5));
        }
    }

    /// <summary>
    ///     A check of a file whose tolerance header is not a number fails with the message instead of using 40.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_Canary_MalformedToleranceHeader_ReturnsError()
    {
        // Arrange: a file with a negative tolerance; the model is not opened before the header is read
        var file = _dir.Write("canary.tsv", "# model: minilm\n# arm-tolerance: -3\ns1\ta\tSense:x\n");

        // Act
        var result = await RunAsync("canary", "--check", file, "--repository", _dir.Root);

        // Assert
        Assert.Equal(1, result.Code);
        Assert.Contains("arm-tolerance", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The answer of a word is its level and key, and a word without a key has none.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_Canary_AnswerAsync_BasicModel_AnswersEveryWordWithLevelAndKey()
    {
        // Arrange: the statistical model needs no model file
        using var model = new BasicWordSenseModel();

        // Act
        var entries = await Canary.AnswerAsync(model, [new CanarySentence("s1", Sentence)], TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(entries);
        Assert.Equal(Sentence.Length, entry.Answers.Length);
        Assert.All(entry.Answers, a => Assert.True(a == "-" || a.Contains(':', StringComparison.Ordinal), a));
    }

    /// <summary>
    ///     The fingerprint is the hash of the bits of the vectors: a changed last bit, a missing vector and another
    ///     order give another fingerprint.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_CpuPreflight_Hash_DependsOnEveryBit()
    {
        // Arrange
        double[] a = [1.0, 2.0];
        var nextToOne = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(2.0) + 1);

        // Act
        var hash = CpuPreflight.Hash([a, null]);

        // Assert
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, CpuPreflight.Hash([new double[] { 1.0, 2.0 }, null]));
        Assert.NotEqual(hash, CpuPreflight.Hash([new double[] { 1.0, nextToOne }, null]));
        Assert.NotEqual(hash, CpuPreflight.Hash([a, new double[0]]));
        Assert.NotEqual(hash, CpuPreflight.Hash([null, a]));
    }

    /// <summary>
    ///     The preflight finds the line of the model and the encoder, and tells a match, a difference and no reference apart.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_CpuPreflight_Compare_FindsTheReferenceOfTheEncoder()
    {
        // Arrange: a comment, a line of another encoder of the model and the line of this one
        const string file = "# comment\nm/v1/x\t" + "aa\t11\nm/v1/x\t" + "bb\t22\n";

        // Act / Assert
        Assert.Equal(CpuFingerprintStatus.Match, CpuPreflight.Compare(file, "m/v1/x", "BB", "22"));
        Assert.Equal(CpuFingerprintStatus.Differs, CpuPreflight.Compare(file, "m/v1/x", "bb", "23"));
        Assert.Equal(CpuFingerprintStatus.NoReference, CpuPreflight.Compare(file, "m/v1/x", "cc", "22"));
        Assert.Equal(CpuFingerprintStatus.NoReference, CpuPreflight.Compare(file, "other", "bb", "22"));
        Assert.Equal(CpuFingerprintStatus.NoReference, CpuPreflight.Compare(null, "m/v1/x", "bb", "22"));
    }

    /// <summary>
    ///     The preflight warns when this CPU does not reproduce the reference fingerprint, says nothing alarming when it
    ///     does, and prints the line to record when the encoder has no reference.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_CpuPreflight_RunAsync_ReportsMatchDifferenceAndNoReference()
    {
        // Arrange: the real MiniLM encoder and a repository folder with the fingerprint file
        using var minilm = new MiniLMEncoderModel(null, 128);
        var spec = minilm.Spec;
        var directory = ModelFileCheck.Require(spec, Path.Join(DataFixture.RepositoryRoot(), "model", "all-MiniLM-L6-v2", "v1"));
        using var model = new MiniLMEncoderModel(directory, 128);
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);
        using var encoder = model.OpenEncoder();
        var fingerprint = await CpuPreflight.FingerprintAsync(encoder, TestContext.Current.CancellationToken);
        var sha = ModelTrainer.EncoderShaOf(spec);
        var tools = Path.Join(_dir.Root, "tools", "WordSense.Builder");
        Directory.CreateDirectory(tools);
        var none = new StringWriter();
        var match = new StringWriter();
        var differs = new StringWriter();

        // Act
        await CpuPreflight.RunAsync(encoder, spec, _dir.Root, none, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Join(tools, CpuPreflight.FileName), $"{spec.WeightsId}\t{sha}\t{fingerprint}\n", TestContext.Current.CancellationToken);
        await CpuPreflight.RunAsync(encoder, spec, _dir.Root, match, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Join(tools, CpuPreflight.FileName), $"{spec.WeightsId}\t{sha}\t{new string('0', 64)}\n", TestContext.Current.CancellationToken);
        await CpuPreflight.RunAsync(encoder, spec, _dir.Root, differs, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(fingerprint, await CpuPreflight.FingerprintAsync(encoder, TestContext.Current.CancellationToken));
        Assert.Contains($"{spec.WeightsId}\t{sha}\t{fingerprint}", none.ToString(), StringComparison.Ordinal);
        Assert.Contains("reproduces the reference", match.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("warning", match.ToString(), StringComparison.Ordinal);
        Assert.Contains("warning", differs.ToString(), StringComparison.Ordinal);
        Assert.Contains("not byte-reproducible", differs.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The command writes the answers of a model and a check on the same machine passes; a changed answer fails it
    ///     on x86-64 and a file of another encoder is refused.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_Canary_WriteThenCheck_PassesOnTheSameMachine()
    {
        // Arrange: the real MiniLM model folder of the repository, five short sentences
        using var minilm = new MiniLMEncoderModel(null, 128);
        ModelFileCheck.Require(minilm.Spec, Path.Join(DataFixture.RepositoryRoot(), "model", "all-MiniLM-L6-v2", "v1"));
        var sentences = _dir.Write("sentences.tsv", "a\tThe bank was open .\nb\tShe will record the record .\nc\tThey went to the river bank .\n");
        var file = Path.Join(_dir.Root, "canary.tsv");
        var repository = DataFixture.RepositoryRoot();

        // Act
        var write = await RunAsync("canary", "--write", file, "--sentences", sentences, "--model", "minilm", "--repository", repository);
        var check = await RunAsync("canary", "--check", file, "--repository", repository);
        var text = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);

        // The first answer of the first sentence is changed
        var lines = text.Split('\n');
        var first = Array.FindIndex(lines, l => l.Length > 0 && l[0] != '#');
        var columns = lines[first].Split('\t');
        columns[2] = "Sense:tampered";
        lines[first] = string.Join('\t', columns);
        await File.WriteAllTextAsync(file, string.Join('\n', lines), TestContext.Current.CancellationToken);
        var changed = await RunAsync("canary", "--check", file, "--repository", repository, "--arm-tolerance", "0");
        await File.WriteAllTextAsync(file, text.Replace(ModelTrainer.EncoderShaOf(minilm.Spec), new string('0', 64), StringComparison.Ordinal), TestContext.Current.CancellationToken);
        var otherEncoder = await RunAsync("canary", "--check", file, "--repository", repository);

        // Assert
        Assert.Equal(0, write.Code);
        Assert.Contains("wrote the answers of minilm", write.Output, StringComparison.Ordinal);
        Assert.StartsWith("# model: minilm\n# encoder-sha256: ", text, StringComparison.Ordinal);
        Assert.Equal(0, check.Code);
        Assert.Contains("0 of ", check.Output, StringComparison.Ordinal);
        Assert.Equal(1, changed.Code);
        Assert.Contains("expected Sense:tampered, got ", changed.Output, StringComparison.Ordinal);
        Assert.Equal(1, otherEncoder.Code);
        Assert.Contains("written for the encoder file", otherEncoder.Error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The command needs exactly one of --write and --check, and a number for the ARM tolerance.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_Canary_InvalidOptions_ReturnsUsageError()
    {
        // Arrange / Act
        var neither = await RunAsync("canary");
        var both = await RunAsync("canary", "--write", "a", "--check", "b");
        var number = await RunAsync("canary", "--check", "a", "--arm-tolerance", "many");
        var negative = await RunAsync("canary", "--check", "a", "--arm-tolerance", "-1");
        var negativeWrite = await RunAsync("canary", "--write", "a", "--sentences", "b", "--arm-tolerance", "-5");

        // Assert
        Assert.All([neither, both, number, negative, negativeWrite], r => Assert.Equal(1, r.Code));
        Assert.Contains("exactly one of --write FILE and --check FILE", neither.Error, StringComparison.Ordinal);
        Assert.Contains("exactly one of --write FILE and --check FILE", both.Error, StringComparison.Ordinal);
        Assert.Contains("--arm-tolerance", number.Error, StringComparison.Ordinal);
        Assert.Contains("--arm-tolerance", negative.Error, StringComparison.Ordinal);
        Assert.Contains("--arm-tolerance", negativeWrite.Error, StringComparison.Ordinal);
    }

    private static async Task<(int Code, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await Program.RunAsync(args, output, error, TestContext.Current.CancellationToken);
        return (code, output.ToString(), error.ToString());
    }
}
