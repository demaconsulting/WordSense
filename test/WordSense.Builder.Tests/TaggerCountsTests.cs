using System.Collections.Immutable;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the tag counts of the basic model.
/// </summary>
public sealed class TaggerCountsTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    /// <summary>
    ///     The analysis of <c>resolving</c>: a verb that is only a gerund.
    /// </summary>
    private static readonly WordDetails Resolving = new("resolve", PartOfSpeech.Verb, "VerbForm=Ger");

    /// <summary>
    ///     The analysis of <c>remove</c>: an infinitive, which is a finite verb of an imperative.
    /// </summary>
    private static readonly WordDetails Remove = new("remove", PartOfSpeech.Verb, "VerbForm=Inf");

    /// <summary>
    ///     Looks up a lower-case word in the toy lexicon of the count tests.
    /// </summary>
    /// <param name="key">The lower-case word.</param>
    /// <returns>The analyses of the word, or the default array when it is unknown.</returns>
    private static ImmutableArray<WordDetails> TestLexicon(string key) => key switch
    {
        "resolving" => [Resolving],
        "remove" => [Remove],
        _ => default,
    };

    private static List<TaggedSentence> Sentences() =>
    [
        new(["don't", "home"], [PosTag.Auxiliary, PosTag.Noun]),
        new(["To", "run", "."], [PosTag.InfinitiveTo, PosTag.Verb, PosTag.Punctuation]),
    ];

    /// <summary>
    ///     The universal tags map onto the tags of the tagger.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerCounts_MapTag_MapsUniversalTags()
    {
        Assert.Equal(PosTag.Noun, TaggerCounts.MapTag("PROPN", "Paris"));
        Assert.Equal(PosTag.Conjunction, TaggerCounts.MapTag("SCONJ", "if"));
        Assert.Equal(PosTag.InfinitiveTo, TaggerCounts.MapTag("PART", "To"));
        Assert.Equal(PosTag.Other, TaggerCounts.MapTag("PART", "not"));
        Assert.Equal(PosTag.Other, TaggerCounts.MapTag("X", "x"));
        Assert.Equal(PosTag.Other, TaggerCounts.MapTag("NUM", "3"));
        Assert.Equal(PosTag.Punctuation, TaggerCounts.MapTag("PUNCT", "."));
        Assert.Equal(PosTag.Auxiliary, TaggerCounts.MapTag("AUX", "will"));
    }

    /// <summary>
    ///     Only tokens made of letters and apostrophes get a word key.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerCounts_KeyOf_AcceptsLettersAndApostrophes()
    {
        Assert.Equal("don't", TaggerCounts.KeyOf("Don\u2019t"));
        Assert.Null(TaggerCounts.KeyOf("3rd"));
        Assert.Null(TaggerCounts.KeyOf("..."));
        Assert.Null(TaggerCounts.KeyOf("'"));
    }

    /// <summary>
    ///     The counts include the sentence boundaries and the word rows.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerCounts_Count_CountsNgramsAndWords()
    {
        // Arrange: the toy sentences and the toy lexicon

        // Act
        var counts = TaggerCounts.Count(Sentences(), TestLexicon);

        // Assert: boundaries, n-grams and the word rows
        Assert.Equal(2, counts.Unigrams[PosTag.Boundary]);
        Assert.Equal(1, counts.Unigrams[PosTag.Noun]);
        Assert.Equal(1, counts.Bigrams[(PosTag.Noun * PosTag.Extended) + PosTag.Boundary]);
        Assert.Equal(1, counts.Trigrams[(((PosTag.Boundary * PosTag.Extended) + PosTag.Boundary) * PosTag.Extended) + PosTag.Auxiliary]);
        Assert.Equal(1, counts.Words["to"][PosTag.InfinitiveTo]);
        Assert.False(counts.Words.ContainsKey("."));
    }

    /// <summary>
    ///     The count files read back as written and are written byte for byte the same each time.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerCounts_WriteAndRead_RoundTripsDeterministically()
    {
        // Arrange: the counts of the toy sentences and two output folders
        var counts = TaggerCounts.Count(Sentences(), TestLexicon);
        var first = Path.Join(_dir.Root, "out1");
        var second = Path.Join(_dir.Root, "out2");

        // Act: write twice and read the first folder back
        counts.Write(first);
        counts.Write(second);
        var back = TaggerCounts.Read(first);

        // Assert: the counts read back as written, and the files of both folders are byte for byte equal
        Assert.Equal(counts.Unigrams, back.Unigrams);
        Assert.Equal(counts.Bigrams, back.Bigrams);
        Assert.Equal(counts.Trigrams, back.Trigrams);
        Assert.Equal(counts.Words.Keys, back.Words.Keys);
        Assert.All(counts.Words, pair => Assert.Equal(pair.Value, back.Words[pair.Key]));
        foreach (var file in new[] { TaggerCounts.NgramsFile, TaggerCounts.WordsFile, TaggerCounts.ClauseNgramsFile })
        {
            Assert.Equal(File.ReadAllBytes(Path.Join(first, file)), File.ReadAllBytes(Path.Join(second, file)));
            Assert.DoesNotContain("\r", File.ReadAllText(Path.Join(first, file)), StringComparison.Ordinal);
        }

        Assert.Equal(counts.ClauseBigrams, back.ClauseBigrams);
        Assert.Equal(counts.ClauseTrigrams, back.ClauseTrigrams);
    }

    /// <summary>
    ///     The clause counts are the counts of the positions whose clause already has a verb: a verb starts the state, a
    ///     conjunction or punctuation ends it, and the other counts are the difference to the totals.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerCounts_Count_CountsClauseNgrams()
    {
        // Arrange: "they go the dog , the cat" with a verb in the first clause only
        var sentence = new TaggedSentence(
            ["they", "go", "the", "dog", ",", "the", "cat"],
            [PosTag.Pronoun, PosTag.Verb, PosTag.Determiner, PosTag.Noun, PosTag.Punctuation, PosTag.Determiner, PosTag.Noun]);

        // Act
        var counts = TaggerCounts.Count([sentence], TestLexicon);

        // Assert: after the verb the determiner and the noun are in a clause with a verb; the second clause and the start are not
        int Bigram(int a, int b) => (a * PosTag.Extended) + b;
        int Trigram(int a, int b, int c) => (((a * PosTag.Extended) + b) * PosTag.Extended) + c;
        Assert.Equal(1, counts.ClauseBigrams[Bigram(PosTag.Verb, PosTag.Determiner)]);
        Assert.Equal(1, counts.ClauseBigrams[Bigram(PosTag.Determiner, PosTag.Noun)]);
        Assert.Equal(1, counts.ClauseTrigrams[Trigram(PosTag.Pronoun, PosTag.Verb, PosTag.Determiner)]);
        Assert.Equal(1, counts.ClauseTrigrams[Trigram(PosTag.Verb, PosTag.Determiner, PosTag.Noun)]);
        Assert.Equal(1, counts.ClauseBigrams[Bigram(PosTag.Noun, PosTag.Punctuation)]);

        // The punctuation ends the clause: what follows it is counted without a verb
        Assert.Equal(0, counts.ClauseBigrams[Bigram(PosTag.Punctuation, PosTag.Determiner)]);
        Assert.Equal(1, counts.Bigrams[Bigram(PosTag.Punctuation, PosTag.Determiner)]);
        Assert.Equal(0, counts.ClauseBigrams[Bigram(PosTag.Pronoun, PosTag.Verb)]);
        Assert.Equal(0, counts.ClauseBigrams[Bigram(PosTag.Boundary, PosTag.Pronoun)]);
        Assert.Equal(2, counts.Bigrams[Bigram(PosTag.Determiner, PosTag.Noun)]);
        for (var i = 0; i < counts.Bigrams.Length; i++)
        {
            Assert.True(counts.ClauseBigrams[i] <= counts.Bigrams[i]);
        }
    }

    /// <summary>
    ///     A sentence-initial verb that the dictionary lists only as a gerund or a participle does not give its clause a
    ///     verb, while an infinitive or an unknown word at the start does.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerCounts_Count_NonFiniteStartDoesNotSetTheClauseState()
    {
        // Arrange: "Resolving this issue" against "Remove this issue", both tagged verb, determiner, noun
        TaggedSentence Of(string first) => new([first, "this", "issue"], [PosTag.Verb, PosTag.Determiner, PosTag.Noun]);

        // Act
        var gerund = TaggerCounts.Count([Of("Resolving")], TestLexicon);
        var imperative = TaggerCounts.Count([Of("Remove")], TestLexicon);

        // Assert: after the imperative the determiner is in a clause with a verb, after the gerund it is not
        var verbDeterminer = (PosTag.Verb * PosTag.Extended) + PosTag.Determiner;
        Assert.Equal(1, imperative.ClauseBigrams[verbDeterminer]);
        Assert.Equal(0, gerund.ClauseBigrams[verbDeterminer]);
        Assert.Equal(1, gerund.Bigrams[verbDeterminer]);
    }

    /// <summary>
    ///     A clause count file with a wrong header or a malformed line is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerCounts_Read_MalformedClauseFile_Throws()
    {
        // Arrange: written count files
        var counts = TaggerCounts.Count(Sentences(), TestLexicon);
        var directory = Path.Join(_dir.Root, "bad-clause");
        counts.Write(directory);
        var file = Path.Join(directory, TaggerCounts.ClauseNgramsFile);

        // Act / Assert: a wrong header
        File.WriteAllText(file, "wrong\theader\n");
        Assert.Throws<InvalidDataException>(() => TaggerCounts.Read(directory));

        // A unigram line, which a clause file does not have
        counts.Write(directory);
        File.AppendAllText(file, "1\tN\t1\n");
        Assert.Throws<InvalidDataException>(() => TaggerCounts.Read(directory));
    }

    /// <summary>
    ///     A clause count file with a bigram count above the total count of the bigram is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerCounts_Read_ClauseBigramAboveTotal_Throws()
    {
        // Arrange: written count files, then a clause bigram count that no total count reaches
        var counts = TaggerCounts.Count(Sentences(), TestLexicon);
        var directory = Path.Join(_dir.Root, "bad-clause-bigram");
        counts.Write(directory);
        File.AppendAllText(Path.Join(directory, TaggerCounts.ClauseNgramsFile), "2\tN N\t1000\n");

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => TaggerCounts.Read(directory));
    }

    /// <summary>
    ///     A clause count file with a trigram count above the total count of the trigram is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerCounts_Read_ClauseTrigramAboveTotal_Throws()
    {
        // Arrange: written count files, then a clause trigram count that no total count reaches
        var counts = TaggerCounts.Count(Sentences(), TestLexicon);
        var directory = Path.Join(_dir.Root, "bad-clause-trigram");
        counts.Write(directory);
        File.AppendAllText(Path.Join(directory, TaggerCounts.ClauseNgramsFile), "3\tN N N\t1000\n");

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => TaggerCounts.Read(directory));
    }

    /// <summary>
    ///     A missing clause count file is reported as a missing file.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerCounts_Read_MissingClauseFile_Throws()
    {
        // Arrange: written count files without the clause file
        var counts = TaggerCounts.Count(Sentences(), TestLexicon);
        var directory = Path.Join(_dir.Root, "no-clause");
        counts.Write(directory);
        File.Delete(Path.Join(directory, TaggerCounts.ClauseNgramsFile));

        // Act / Assert
        Assert.Throws<FileNotFoundException>(() => TaggerCounts.Read(directory));
    }

    /// <summary>
    ///     A count file with a wrong header or a malformed line is rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_TaggerCounts_Read_MalformedFiles_Throw()
    {
        var counts = TaggerCounts.Count(Sentences(), TestLexicon);
        var directory = Path.Join(_dir.Root, "bad");
        counts.Write(directory);
        File.WriteAllText(Path.Join(directory, TaggerCounts.WordsFile), "wrong\theader\n");
        Assert.Throws<InvalidDataException>(() => TaggerCounts.Read(directory));

        counts.Write(directory);
        File.AppendAllText(Path.Join(directory, TaggerCounts.WordsFile), "home\tN:1\n");
        Assert.Throws<InvalidDataException>(() => TaggerCounts.Read(directory));

        counts.Write(directory);
        File.AppendAllText(Path.Join(directory, TaggerCounts.NgramsFile), "4\tN N N N\t1\n");
        Assert.Throws<InvalidDataException>(() => TaggerCounts.Read(directory));
    }
}
