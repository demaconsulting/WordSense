using System.Globalization;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the per-token outcome files and the paired comparison of two versions.
/// </summary>
public sealed class OutcomeComparerTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static TokenOutcome Token(string set, int sentence, int token, bool correct, bool ambiguous = true, string document = "", string gold = "Noun", string answer = "Verb") =>
        new(set, sentence, document.Length == 0 ? "s" + sentence.ToString(CultureInfo.InvariantCulture) : document, token, "w" + token.ToString(CultureInfo.InvariantCulture), gold, correct ? gold : answer, correct, ambiguous);

    /// <summary>
    ///     The exact McNemar p-value is one without discordant tokens, the doubled binomial tail otherwise, and tiny for
    ///     many more repairs than regressions.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OutcomeComparer_McNemar_IsTheExactTwoSidedBinomialTest()
    {
        // Arrange: discordant-pair counts with none, balanced, one-sided, lopsided and very large splits
        // Act / Assert: each count pair gives the exact two-sided binomial p-value
        Assert.Equal(1.0, OutcomeComparer.McNemar(0, 0));
        Assert.Equal(1.0, OutcomeComparer.McNemar(10, 10), 12);
        Assert.Equal(0.0625, OutcomeComparer.McNemar(5, 0), 12);
        Assert.Equal(0.625, OutcomeComparer.McNemar(3, 1), 12);
        Assert.Equal(OutcomeComparer.McNemar(1, 3), OutcomeComparer.McNemar(3, 1));
        var large = OutcomeComparer.McNemar(647, 299);
        Assert.InRange(large, 1e-31, 1e-29);
    }

    /// <summary>
    ///     The gain, the repairs and the regressions are counted over the paired tokens; the interval contains the gain and
    ///     is the same for the same seed.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OutcomeComparer_Compare_CountsRepairsAndRegressionsWithAnInterval()
    {
        // Arrange: 40 sentences of two tokens: the new version repairs 12 tokens and regresses 2, in different sentences
        List<TokenOutcome> baseline = [];
        List<TokenOutcome> updated = [];
        for (var s = 0; s < 40; s++)
        {
            for (var t = 0; t < 2; t++)
            {
                var baseRight = !(s < 12 && t == 0);
                var newRight = !baseRight || !(s is 20 or 21 && t == 1);
                baseline.Add(Token("set", s, t, baseRight));
                updated.Add(Token("set", s, t, newRight));
            }
        }

        // Act
        var report = OutcomeComparer.Compare(baseline, updated, "sentence", 2000, 7);
        var again = OutcomeComparer.Compare(baseline, updated, "sentence", 2000, 7);

        // Assert
        var all = report.Comparisons[0];
        Assert.Equal(("set", "all tokens", 80, 40, 68, 78, 12, 2), (all.Set, all.Subset, all.Tokens, all.Clusters, all.BaseCorrect, all.NewCorrect, all.Repairs, all.Regressions));
        Assert.Equal(100.0 * 10 / 80, all.Gain, 10);
        Assert.True(all.Lower <= all.Gain && all.Gain <= all.Upper);
        Assert.True(all.Lower > 0, "Twelve repairs against two regressions in different sentences exclude no gain.");
        Assert.Equal(report.Comparisons, again.Comparisons);
        Assert.Equal(all.BaseCorrect * 100.0 / 80, all.BaseAccuracy, 10);
        Assert.Equal(all.NewCorrect * 100.0 / 80, all.NewAccuracy, 10);
    }

    /// <summary>
    ///     A version that is right on every token the base gets wrong has an interval that is exactly the gain, and the
    ///     ambiguous subset only counts ambiguous tokens.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OutcomeComparer_Compare_DegenerateGainAndAmbiguousSubset()
    {
        // Arrange: two wrong base tokens that the new version gets right, and one ambiguity-free token that is right in both
        List<TokenOutcome> baseline = [Token("set", 0, 0, false), Token("set", 1, 0, false), Token("set", 2, 0, true, ambiguous: false)];
        List<TokenOutcome> updated = [Token("set", 0, 0, true), Token("set", 1, 0, true), Token("set", 2, 0, true, ambiguous: false)];

        // Act: compare by sentence
        var report = OutcomeComparer.Compare(baseline, updated, "sentence", 500, 1);

        // Assert: the ambiguous subset has two tokens and a degenerate interval at the gain
        var all = report.Comparisons[0];
        var ambiguous = report.Comparisons[1];
        Assert.Equal(3, all.Tokens);
        Assert.Equal(2, ambiguous.Tokens);
        Assert.Equal(100.0, ambiguous.Gain);
        Assert.Equal((100.0, 100.0), (ambiguous.Lower, ambiguous.Upper));
        Assert.Equal(2 * 100.0 / 3, all.Gain, 10);
    }

    /// <summary>
    ///     Clustering by document merges the sentences of a document, and tokens that only one file has are left out and counted.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OutcomeComparer_Compare_ClustersByDocumentAndCountsUnpairedTokens()
    {
        // Arrange: two sentences in document d1, one token each in d2 and d3 for the base, and unpaired tokens on both sides
        List<TokenOutcome> baseline = [Token("set", 0, 0, false, document: "d1"), Token("set", 1, 0, true, document: "d1"), Token("set", 2, 0, true, document: "d2"), Token("set", 3, 0, true, document: "d3")];
        List<TokenOutcome> updated = [Token("set", 0, 0, true, document: "d1"), Token("set", 1, 0, true, document: "d1"), Token("set", 2, 0, true, document: "d2"), Token("set", 9, 0, true, document: "d9")];

        // Act: compare clustered by sentence and by document
        var bySentence = OutcomeComparer.Compare(baseline, updated, "sentence", 100, 1);
        var byDocument = OutcomeComparer.Compare(baseline, updated, "document", 100, 1);

        // Assert: document clustering merges sentences and the unpaired tokens are counted
        Assert.Equal(3, bySentence.Comparisons[0].Clusters);
        Assert.Equal(2, byDocument.Comparisons[0].Clusters);
        Assert.Equal(2, bySentence.Unpaired["set"]);
        Assert.Equal(3, byDocument.Comparisons[0].Tokens);
    }

    /// <summary>
    ///     An unknown cluster unit, no common token and a non-positive number of replicates are rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OutcomeComparer_Compare_InvalidInput_Throws()
    {
        // Arrange: one outcome set, and a second set with no token in common
        List<TokenOutcome> one = [Token("set", 0, 0, true)];
        List<TokenOutcome> other = [Token("other", 0, 0, true)];

        // Act / Assert: each invalid unit, pairing, replicate count or null input is rejected
        Assert.Throws<ArgumentException>(() => OutcomeComparer.Compare(one, one, "page", 10, 1));
        Assert.Throws<ArgumentException>(() => OutcomeComparer.Compare(one, other, "sentence", 10, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => OutcomeComparer.Compare(one, one, "sentence", 0, 1));
        Assert.Throws<ArgumentNullException>(() => OutcomeComparer.Compare(null!, one, "sentence", 10, 1));
        Assert.Throws<ArgumentNullException>(() => OutcomeComparer.Compare(one, null!, "sentence", 10, 1));
    }

    /// <summary>
    ///     The changes of the wrong cells are listed, a cell that grows by more than 0.3 points is named, and the report
    ///     states the interval method.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OutcomeComparer_Render_ListsTheConfusionChanges()
    {
        // Arrange: 100 tokens; the base confuses 10 as Noun to Adjective; the new version repairs four of them and turns five into Noun to Verb
        List<TokenOutcome> baseline = [.. Enumerable.Range(0, 100).Select(i => i < 10 ? Token("set", i, 0, false, gold: "Noun", answer: "Adjective") : Token("set", i, 0, true))];
        List<TokenOutcome> updated = [.. Enumerable.Range(0, 100).Select(i => i switch
        {
            < 5 => Token("set", i, 0, false, gold: "Noun", answer: "Verb"),
            < 9 => Token("set", i, 0, true),
            9 => Token("set", i, 0, false, gold: "Noun", answer: "Adjective"),
            _ => Token("set", i, 0, true),
        })];

        // Act
        var report = OutcomeComparer.Compare(baseline, updated, "sentence", 200, 3);
        var text = OutcomeComparer.Render(report, "main.tsv", "head.tsv");

        // Assert
        var verb = Assert.Single(report.Confusions, c => c.Answer == "Verb");
        Assert.Equal((0, 5, 5.0), (verb.BaseCount, verb.NewCount, verb.Points));
        Assert.Contains("| Noun | Adjective | 10 | 1 | -9.00 |", text, StringComparison.Ordinal);
        Assert.Contains("Noun to Verb (+5.00)", text, StringComparison.Ordinal);
        Assert.Contains("paired cluster bootstrap by sentence, 200 replicates, seed 3", text, StringComparison.Ordinal);
        Assert.Contains("Base: main.tsv; new: head.tsv", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     An outcome file reads back what was written, and a malformed file is refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OutcomeFile_RenderAndRead_RoundTrip()
    {
        // Arrange: two outcomes rendered to a file
        (string, TokenOutcome)[] outcomes = [("basic", Token("set", 3, 4, false, document: "doc")), ("minilm", Token("set b", 0, 1, true, ambiguous: false))];
        var path = _dir.Write("o.tsv", OutcomeFile.Render(outcomes));

        // Act: read the file back
        var read = OutcomeFile.Read(path);

        // Assert: the outcomes round-trip, malformed files are refused and the tag names are mapped
        Assert.Equal(outcomes, read);
        Assert.Throws<InvalidDataException>(() => OutcomeFile.Read(_dir.Write("bad-header.tsv", "model\tset\n")));
        Assert.Throws<InvalidDataException>(() => OutcomeFile.Read(_dir.Write("bad-line.tsv", OutcomeFile.Render([]) + "basic\tset\tx\td\t1\tw\tNoun\tVerb\t1\t1\n")));
        Assert.Throws<InvalidDataException>(() => OutcomeFile.Read(_dir.Write("bad-flag.tsv", OutcomeFile.Render([]) + "basic\tset\t1\td\t1\tw\tNoun\tVerb\t2\t1\n")));
        Assert.Throws<ArgumentNullException>(() => OutcomeFile.Render(null!));
        Assert.Equal("Preposition", OutcomeFile.NameOf(PosTag.InfinitiveTo));
        Assert.Equal("Auxiliary", OutcomeFile.NameOf(PosTag.Auxiliary));
    }
}
