using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the reader of the fine-sense corpus and the rules that tie its rows to the sense catalog.
/// </summary>
public sealed class CorpusTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static SenseInfo Noun() => SenseFixtures.Senses("walk", PartOfSpeech.Noun, 1)[0];

    private static SenseInfo Verb() => SenseFixtures.Senses("walk", PartOfSpeech.Verb, 1)[0];

    private static string RowText(SenseInfo sense, string sentence) => $"{sense.Lemma}\t{sense.SenseId}\t{sense.PartOfSpeech}\t{sentence}\n";

    private string Write(string relative, string content) => _dir.Write(relative, content);

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "corpus", "lexicon", "lexicon.tsv")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root;
    }

    /// <summary>
    ///     A well-formed row is parsed with its sense, part of speech, words and target word.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Corpus_ReadSentences_ParsesRow()
    {
        // Arrange: a row of a real sense of the catalog
        var sense = Noun();
        var path = Write("a.tsv", RowText(sense, "She went for a long {walk} today."));

        // Act
        var row = Assert.Single(Corpus.ReadSentences(path));

        // Assert
        Assert.Equal("walk", row.Lemma);
        Assert.Equal(sense.SenseId, row.SenseId);
        Assert.Equal(PartOfSpeech.Noun, row.PartOfSpeech);
        Assert.Equal("a.tsv", row.Source);
        Assert.Equal(["She", "went", "for", "a", "long", "walk", "today", "."], row.Words);
        Assert.Equal("walk", row.Words[row.TargetWordIndex]);
    }

    /// <summary>
    ///     A target inside a hyphenated or possessive word splits that word at the braces.
    /// </summary>
    /// <param name="sentence">The sentence.</param>
    /// <param name="words">The expected words, joined by a space.</param>
    /// <param name="target">The expected target word.</param>
    [Theory]
    [InlineData("It was a big-{lunged} dog.", "It was a big- lunged dog .", "lunged")]
    [InlineData("The {regiment}'s flag fell.", "The regiment 's flag fell .", "regiment")]
    [InlineData("The y-{intercept} is two.", "The y- intercept is two .", "intercept")]
    public void WordSenseBuilder_Corpus_ReadSentences_SplitsCompoundTarget(string sentence, string words, string target)
    {
        var row = Assert.Single(Corpus.ReadSentences(Write("c.tsv", RowText(Noun(), sentence))));

        Assert.Equal(words, string.Join(' ', row.Words));
        Assert.Equal(target, row.Words[row.TargetWordIndex]);
    }

    /// <summary>
    ///     Malformed rows are rejected: wrong column count, an upper-case lemma, a part of speech that is not a name, an
    ///     unknown sense id and a sentence without exactly one non-empty target.
    /// </summary>
    /// <param name="template">The row with the sense id of a real sense in place of <c>{id}</c>.</param>
    [Theory]
    [InlineData("walk\t{id}\tNoun")]
    [InlineData("walk\t{id}\tNoun\tA {walk}.\textra")]
    [InlineData("Walk\t{id}\tNoun\tA {walk}.")]
    [InlineData("walk\t{id}\tSubstantive\tA {walk}.")]
    [InlineData("walk\t{id}\t1\tA {walk}.")]
    [InlineData("walk\t{id}\t999\tA {walk}.")]
    [InlineData("walk\twalk#no-such-sense\tNoun\tA {walk}.")]
    [InlineData("walk\t{id}\tNoun\tNo target here.")]
    [InlineData("walk\t{id}\tNoun\tTwo {walk} {walk}.")]
    [InlineData("walk\t{id}\tNoun\tEmpty {} pair.")]
    public void WordSenseBuilder_Corpus_ReadSentences_RejectsMalformedRow(string template)
    {
        var path = Write("bad.tsv", template.Replace("{id}", Noun().SenseId, StringComparison.Ordinal) + "\n");

        Assert.Throws<InvalidDataException>(() => Corpus.ReadSentences(path));
    }

    /// <summary>
    ///     A row whose part of speech is not the one of its sense is rejected, with the file, the line and both parts of speech.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Corpus_ReadSentences_PartOfSpeechDisagreeingWithSense_IsRejected()
    {
        // Arrange: the noun sense with the part of speech Verb on the second line
        var noun = Noun();
        var path = Write("pos.tsv", RowText(noun, "A {walk}.") + $"walk\t{noun.SenseId}\tVerb\tThey {{walk}} home.\n");

        // Act
        var error = Assert.Throws<InvalidDataException>(() => Corpus.ReadSentences(path));

        // Assert
        Assert.Contains("pos.tsv:2", error.Message, StringComparison.Ordinal);
        Assert.Contains("Verb", error.Message, StringComparison.Ordinal);
        Assert.Contains("Noun", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A row whose sense belongs to another lemma is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Corpus_ReadSentences_SenseOfAnotherLemma_IsRejected()
    {
        var other = SenseFixtures.Senses("run", PartOfSpeech.Noun, 1)[0];
        var path = Write("lemma.tsv", $"walk\t{other.SenseId}\tNoun\tA {{walk}}.\n");

        var error = Assert.Throws<InvalidDataException>(() => Corpus.ReadSentences(path));

        Assert.Contains("belongs to the lemma 'run'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The lenient reader returns the valid rows and one message per invalid row.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Corpus_ReadSentencesLenient_CollectsErrors()
    {
        var path = Write("l.tsv", RowText(Noun(), "A {walk}.") + RowText(Noun(), "No target.") + "\n" + RowText(Verb(), "To {walk}."));

        var (rows, errors) = Corpus.ReadSentencesLenient(path);

        Assert.Equal(2, rows.Count);
        Assert.Contains("l.tsv:2", Assert.Single(errors), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A missing directory yields no rows, the files are read in name order, and a sentence that appears in both
    ///     sets, however its case, spacing or punctuation differ, is detected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Corpus_DirectoryAndDisjointness()
    {
        // Arrange: two files written out of order
        Assert.Empty(Corpus.ReadDirectory(Path.Join(_dir.Root, "none")));
        Write("anchors/b.tsv", RowText(Noun(), "Second {walk}."));
        Write("anchors/a.tsv", RowText(Noun(), "First {walk}."));
        Write("holdout/h.tsv", RowText(Noun(), "first  {WALK}!") + RowText(Noun(), "Another {walk}."));

        // Act
        var anchors = Corpus.ReadDirectory(Path.Join(_dir.Root, "anchors"));
        var holdout = Corpus.ReadDirectory(Path.Join(_dir.Root, "holdout"));
        var clash = Assert.Throws<InvalidDataException>(() => Corpus.EnsureDisjoint(anchors, holdout));
        Corpus.EnsureDisjoint(anchors, holdout.Skip(1));

        // Assert
        Assert.Equal(["a.tsv", "b.tsv"], anchors.Select(r => r.Source));
        Assert.Contains("holdout/h.tsv", clash.Message, StringComparison.Ordinal);
        Assert.Contains("anchors/a.tsv", clash.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => Corpus.EnsureDisjoint([anchors[0], anchors[0]], []));
    }

    /// <summary>
    ///     The lemmas of a directory are the first columns of its rows, read without validation.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Corpus_ReadLemmas_ReadsFirstColumnWithoutValidation()
    {
        Write("lemmas/a.tsv", "walk\tnot-a-sense\tNoun\tx\n\nrun\n");
        Write("lemmas/b.tsv", "walk\ty\n zzyzx \tz\n");

        var lemmas = Corpus.ReadLemmas(Path.Join(_dir.Root, "lemmas"));

        Assert.Equal(["run", "walk", "zzyzx"], lemmas.Order(StringComparer.Ordinal));
        Assert.Empty(Corpus.ReadLemmas(Path.Join(_dir.Root, "missing")));
    }

    /// <summary>
    ///     The senses of a lemma are listed for every part of speech in enumeration order, each in catalog order.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_SensesOf_FollowsPartOfSpeechThenCatalogOrder()
    {
        var senses = SenseInventory.SensesOf("walk");

        Assert.Equal(
            Enum.GetValues<PartOfSpeech>().SelectMany(pos => WordLexicon.SensesOf("walk", pos)).Select(s => s.SenseId),
            senses.Select(s => s.SenseId));
        Assert.Contains(senses, s => s.PartOfSpeech == PartOfSpeech.Noun);
        Assert.Contains(senses, s => s.PartOfSpeech == PartOfSpeech.Verb);
        Assert.Empty(SenseInventory.SensesOf("foobar"));
    }

    /// <summary>
    ///     The rules flag a lemma that is not lower-case, an unknown sense, a sense of another lemma and a part of speech
    ///     that disagrees with the sense (errors), and a target word that the lexicon does not analyze as the lemma and
    ///     part of speech of the row (a warning, only for a row that has no error). An inflected form of the lemma is fine.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_ValidateRules_FlagsViolations()
    {
        // Arrange
        var noun = Noun();
        var run = SenseFixtures.Senses("run", PartOfSpeech.Noun, 1)[0];
        CorpusRow Make(string lemma, string senseId, PartOfSpeech pos, string target) =>
            new(lemma, senseId, pos, $"A {{{target}}} here.", "r.tsv", ["A", target, "here", "."], 1);
        CorpusRow[] rows =
        [
            Make("walk", noun.SenseId, PartOfSpeech.Noun, "walk"),
            Make("walk", noun.SenseId, PartOfSpeech.Noun, "walks"),
            Make("Walk", noun.SenseId, PartOfSpeech.Noun, "walk"),
            Make("walk", "walk#no-such-sense", PartOfSpeech.Noun, "walk"),
            Make("walk", run.SenseId, PartOfSpeech.Noun, "walk"),
            Make("walk", noun.SenseId, PartOfSpeech.Verb, "walk"),
            Make("walk", noun.SenseId, PartOfSpeech.Noun, "foobar"),
            Make("walk", noun.SenseId, PartOfSpeech.Noun, "foobar"),
        ];

        // Act
        var result = SenseInventory.ValidateRules("anchors", rows);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(4, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Contains("must be lower-case", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("not in the sense catalog", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("belongs to the lemma 'run'", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("part of speech of the row is Verb", StringComparison.Ordinal));
        Assert.All(result.Errors, e => Assert.StartsWith("anchors/r.tsv: ", e, StringComparison.Ordinal));

        // Only the target that the lexicon cannot resolve is a warning; the inflected forms resolve and are fine
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("2 row(s) mark 'foobar'", warning, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(result.ThrowIfInvalid);
    }

    /// <summary>
    ///     The committed anchors and holdout validate against the catalog of the word image: no row disagrees with its
    ///     sense and the two sets are disjoint. Rows whose target word is no form of their lemma are only warnings.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_Validate_CommittedCorpus_IsConsistent()
    {
        // Arrange
        var corpus = Path.Join(FindRepositoryRoot().FullName, "corpus");
        var anchors = Corpus.ReadDirectory(Path.Join(corpus, "anchors"));
        var holdout = Corpus.ReadDirectory(Path.Join(corpus, "holdout"));

        // Act
        var result = SenseInventory.Validate(anchors, holdout);

        // Assert
        Assert.NotEmpty(anchors);
        Assert.NotEmpty(holdout);
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors.Take(10)));
        Assert.True(result.Warnings.Count == 0, string.Join(Environment.NewLine, result.Warnings.Take(10)));
    }

    /// <summary>
    ///     A sentence in both sets makes the validation fail.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_SenseInventory_Validate_SentenceInBothSets_IsAnError()
    {
        var row = SenseFixtures.Row(Noun(), 1);

        var result = SenseInventory.Validate([row], [row with { Source = "h.tsv" }]);

        Assert.False(result.IsValid);
        Assert.Contains("Duplicate sentence", Assert.Single(result.Errors), StringComparison.Ordinal);
    }
}
