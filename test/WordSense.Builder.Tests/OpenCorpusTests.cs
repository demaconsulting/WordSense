namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the reader of the open part-of-speech corpus.
/// </summary>
public sealed class OpenCorpusTests : IDisposable
{
    private const string Header = "# corpus = test\n# license = test\n# columns = id form lemma upos collapsed feats consensus\n\n";

    /// <summary>
    ///     The consensus labels the corpus reader may give a token, shared by the checks of the real corpus.
    /// </summary>
    private static readonly string[] ConsensusValues = ["unanimous", "disputed", "unchecked"];

    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static OpenSentence Sentence(string text, params (string Form, string Upos)[] tokens) =>
        new("s1", "d1", text, [.. tokens.Select(t => new OpenToken(t.Form, t.Upos, "unanimous"))]);

    /// <summary>
    ///     The reader skips the header comments and reads the sentence comments and the token lines; the targeted
    ///     comments are ignored.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OpenCorpus_ReadFile_ReadsSentencesAndTokens()
    {
        var path = _dir.Write(
            "t.tsv",
            Header +
            "# sent_id = a-s1\n# source_id = a\n# selection = genre=test\n# query = test\n# text = Check it.\n" +
            "1\tCheck\tcheck\tVERB\tV\tMood=Imp|VerbForm=Fin\tunanimous\n2\tit\tit\tPRON\tR\t_\tdisputed\n3\t.\t.\tPUNCT\tO\t_\tunchecked\n\n" +
            "# sent_id = a-s2\n# source_id = a\n# text = Go.\n1\tGo\tgo\tVERB\tV\t_\tunanimous\n2\t.\t.\tPUNCT\tO\t_\tunanimous\n");

        var sentences = OpenCorpus.ReadFile(path);

        Assert.Equal(["a-s1", "a-s2"], sentences.Select(s => s.SentenceId));
        Assert.Equal("a", sentences[0].SourceId);
        Assert.Equal("Check it.", sentences[0].Text);
        Assert.Equal(["Check", "it", "."], sentences[0].Tokens.Select(t => t.Form));
        Assert.Equal(["VERB", "PRON", "PUNCT"], sentences[0].Tokens.Select(t => t.Upos));
        Assert.Equal(["unanimous", "disputed", "unchecked"], sentences[0].Tokens.Select(t => t.Consensus));
    }

    /// <summary>
    ///     A file with only the header has no sentences, and a split is found by part and name.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OpenCorpus_ReadSplit_FindsTheFileOfAPart()
    {
        _dir.Write("corpus/open/representative/data/dev.tsv", Header);
        _dir.Write("corpus/open/targeted/data/test.tsv", Header + "# sent_id = x\n# source_id = y\n# text = Go.\n1\tGo\tgo\tVERB\tV\t_\tunanimous\n");

        Assert.Empty(OpenCorpus.ReadSplit(Path.Join(_dir.Root, "corpus"), OpenCorpus.Representative, "dev"));
        Assert.Single(OpenCorpus.ReadSplit(Path.Join(_dir.Root, "corpus"), OpenCorpus.Targeted, "test"));
        Assert.EndsWith(Path.Join("open", "counts"), OpenCorpus.CountsDirectory("corpus"), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A missing file and a malformed token line are reported.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OpenCorpus_ReadFile_MissingFileAndMalformedLine_Throw()
    {
        Assert.Throws<FileNotFoundException>(() => OpenCorpus.ReadFile(Path.Join(_dir.Root, "none.tsv")));
        var shortLine = _dir.Write("short.tsv", Header + "# sent_id = a\n# source_id = a\n# text = Go.\n1\tGo\tgo\tVERB\n");
        var outside = _dir.Write("outside.tsv", Header + "1\tGo\tgo\tVERB\tV\t_\tunanimous\n");

        Assert.Throws<InvalidDataException>(() => OpenCorpus.ReadFile(shortLine));
        Assert.Throws<InvalidDataException>(() => OpenCorpus.ReadFile(outside));
    }

    /// <summary>
    ///     A token line without a form, lemma or tag is refused with its file and line, and the lemma of a token is kept.
    /// </summary>
    /// <param name="token">The token line.</param>
    [Theory]
    [InlineData("1\t\tgo\tVERB\tV\t_\tunanimous")]
    [InlineData("1\tGo\t\tVERB\tV\t_\tunanimous")]
    [InlineData("1\tGo\tgo\t\tV\t_\tunanimous")]
    public void WordSenseBuilder_OpenCorpus_ReadFile_TokenWithEmptyField_IsRefusedWithTheLine(string token)
    {
        // Arrange
        var path = _dir.Write("empty.tsv", Header + "# sent_id = a\n# source_id = a\n# text = Go.\n" + token + "\n");

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => OpenCorpus.ReadFile(path));

        // Assert
        Assert.Contains("empty.tsv:8:", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A split contraction is joined again when the joined form is in the text, with the tag of its first part, and
    ///     a possessive 's stays a token of its own.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OpenCorpus_ToTagged_JoinsContractionsButNotPossessives()
    {
        var sentence = Sentence(
            "We don't know John's car, but it's red and I can't go.",
            ("We", "PRON"), ("do", "AUX"), ("n't", "PART"), ("know", "VERB"), ("John", "PROPN"), ("'s", "PART"), ("car", "NOUN"), (",", "PUNCT"),
            ("but", "CCONJ"), ("it", "PRON"), ("'s", "VERB"), ("red", "ADJ"), ("and", "CCONJ"), ("I", "PRON"), ("ca", "AUX"), ("n't", "PART"),
            ("go", "VERB"), (".", "PUNCT"));

        var tagged = OpenCorpus.ToTagged(sentence);

        Assert.Equal(["We", "don't", "know", "John", "'s", "car", ",", "but", "it's", "red", "and", "I", "can't", "go", "."], tagged.Words);
        Assert.Equal(tagged.Words.Length, tagged.Tags.Length);
        var possessive = Array.IndexOf(tagged.Words, "'s");
        Assert.Equal(DemaConsulting.WordSense.PosTag.Other, tagged.Tags[possessive]);
        Assert.Equal(DemaConsulting.WordSense.PosTag.Auxiliary, tagged.Tags[Array.IndexOf(tagged.Words, "don't")]);
    }

    /// <summary>
    ///     A clitic is not joined when the joined form is not in the text, or when it follows a token that does not end
    ///     in a letter.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OpenCorpus_ToTagged_KeepsClitics_WhenTheJoinedFormIsNotInTheText()
    {
        var sentence = Sentence("They said 'd.", ("They", "PRON"), ("said", "VERB"), ("'d", "AUX"), (".", "PUNCT"));
        var afterPunctuation = Sentence("Well , 's it.", ("Well", "INTJ"), (",", "PUNCT"), ("'s", "VERB"), ("it", "PRON"), (".", "PUNCT"));

        Assert.Equal(["They", "said", "'d", "."], OpenCorpus.ToTagged(sentence).Words);
        Assert.Equal(["Well", ",", "'s", "it", "."], OpenCorpus.ToTagged(afterPunctuation).Words);
    }

    /// <summary>
    ///     The committed corpus reads in every split, the splits of the representative part have no document in common,
    ///     and the simplified-technical-English challenge sentences are in the test split of the targeted part, which
    ///     shares no sentence with the representative part.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OpenCorpus_Committed_ReadsEverySplitAndKeepsTheStePartInTheChallengeSet()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "corpus", "lexicon", "lexicon.tsv")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var corpus = Path.Join(root.FullName, "corpus");
        var train = OpenCorpus.ReadSplit(corpus, OpenCorpus.Representative, "train");
        var dev = OpenCorpus.ReadSplit(corpus, OpenCorpus.Representative, "dev");
        var test = OpenCorpus.ReadSplit(corpus, OpenCorpus.Representative, "test");
        var challenge = OpenCorpus.ReadSplit(corpus, OpenCorpus.Targeted, "test");
        var targetedTrain = OpenCorpus.ReadSplit(corpus, OpenCorpus.Targeted, "train");

        Assert.InRange(train.Count, 10_000, int.MaxValue);
        Assert.InRange(dev.Count, 1_000, int.MaxValue);
        Assert.InRange(test.Count, 1_000, int.MaxValue);
        var documents = new[] { train, dev, test }.Select(s => s.Select(x => x.SourceId).ToHashSet(StringComparer.Ordinal)).ToList();
        Assert.Empty(documents[0].Intersect(documents[1]));
        Assert.Empty(documents[0].Intersect(documents[2]));
        Assert.Empty(documents[1].Intersect(documents[2]));
        Assert.Equal(32, challenge.Count(s => s.SourceId == "wordsense-ste-probe"));
        Assert.DoesNotContain(targetedTrain, s => s.SourceId == "wordsense-ste-probe");
        var representativeIds = train.Concat(dev).Concat(test).Select(s => s.SentenceId).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(challenge.Concat(targetedTrain), s => representativeIds.Contains(s.SentenceId));
        Assert.All(challenge.Concat(train.Take(100)), s => Assert.All(s.Tokens, t => Assert.Contains(t.Consensus, ConsensusValues)));
    }
}
