using System.Collections.Immutable;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests of the ranking of the senses of the Basic classifier by the usage prior: an analysis whose lemma and part
///     of speech have two or more senses lists them ranked by usage, and the answer (level, confidence, sense and group)
///     is the one the classifier gave before.
/// </summary>
public class BasicClassifierUsagePriorTests
{
    private static readonly string[] Sentence = ["The", "dog", "chased", "a", "ball", "across", "the", "yard", "and", "the", "bank", "of", "the", "river", "was", "fine", "."];

    private static ImmutableArray<WordDetails> Lookup(string key) => WordLexicon.TryLookup(key, out var found) ? found : [];

    private static int Count(string lemma, PartOfSpeech pos) => WordLexicon.Current.SenseCountOf(lemma, pos);

    private static BasicClassifier WithoutPrior() => new(null, Lookup, Count, WordLexicon.SensesOf);

    /// <summary>
    ///     Every analysis whose lemma and part of speech have two or more senses lists them in the order of the usage
    ///     prior.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_PolysemousWord_ListsTheSensesRankedByUsage()
    {
        // Act
        var results = await new BasicClassifier().ClassifyAsync(Sentence, TestContext.Current.CancellationToken);
        var polysemous = results.SelectMany(r => r.Analyses).Where(a => Count(a.Details.Lemma, a.Details.PartOfSpeech) >= 2).ToList();

        // Assert
        Assert.NotEmpty(polysemous);
        Assert.All(polysemous, a => Assert.Equal(UsagePrior.RankOf(a.Details.Lemma, a.Details.PartOfSpeech), a.Senses));
        Assert.All(polysemous, a => Assert.True(a.Senses.Length >= 2));
    }

    /// <summary>
    ///     The answer for a polysemous word stays at the part-of-speech level with no sense and no group, although its
    ///     selected analysis lists ranked senses.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_PolysemousWord_AnswersAtThePartOfSpeechLevelWithoutSenseOrGroup()
    {
        // Act
        var dog = (await new BasicClassifier().ClassifyAsync(Sentence, TestContext.Current.CancellationToken))[1];

        // Assert
        Assert.True(dog.Analyses.First(a => a.Details.Equals(dog.Details)).Senses.Length >= 2);
        Assert.Equal(SenseLevel.PartOfSpeech, dog.Level);
        Assert.Null(dog.SenseId);
        Assert.Null(dog.GroupId);
    }

    /// <summary>
    ///     The ranking never changes the probability of any analysis: the analyses and their probabilities are those of a
    ///     classifier without the prior.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_UsagePrior_NeverChangesTheAnalysisProbabilities()
    {
        // Act
        var ranked = await new BasicClassifier().ClassifyAsync(Sentence, TestContext.Current.CancellationToken);
        var plain = await WithoutPrior().ClassifyAsync(Sentence, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(plain.Length, ranked.Length);
        for (var i = 0; i < ranked.Length; i++)
        {
            Assert.Equal(plain[i].Analyses.Select(a => (a.Details, a.Probability)), ranked[i].Analyses.Select(a => (a.Details, a.Probability)));
        }

        Assert.Contains(ranked, r => r.Analyses.Any(a => a.Senses.Length >= 2));
    }

    /// <summary>
    ///     The ranking never changes the selected analysis: the details of every result are those of a classifier without
    ///     the prior.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_UsagePrior_NeverChangesTheSelectedAnalysis()
    {
        // Act
        var ranked = await new BasicClassifier().ClassifyAsync(Sentence, TestContext.Current.CancellationToken);
        var plain = await WithoutPrior().ClassifyAsync(Sentence, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(plain.Select(r => r.Details), ranked.Select(r => r.Details));
        Assert.Contains(ranked, r => r.Analyses.Any(a => a.Senses.Length >= 2));
    }

    /// <summary>
    ///     The ranking never changes the answer: the level, the confidence, the sense id and the group id are those of a
    ///     classifier without the prior.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_UsagePrior_NeverChangesTheAnswer()
    {
        // Act
        var ranked = await new BasicClassifier().ClassifyAsync(Sentence, TestContext.Current.CancellationToken);
        var plain = await WithoutPrior().ClassifyAsync(Sentence, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(plain.Select(r => (r.Level, r.Confidence, r.SenseId, r.GroupId)), ranked.Select(r => (r.Level, r.Confidence, r.SenseId, r.GroupId)));
        Assert.Contains(ranked, r => r.Analyses.Any(a => a.Senses.Length >= 2));
    }

    /// <summary>
    ///     The ranking function is asked only for a lemma and part of speech with two or more senses.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_PriorIsAskedOnlyForSlotsWithTwoOrMoreSenses()
    {
        // Arrange: a ranking function that records its arguments
        var asked = new List<(string Lemma, PartOfSpeech Pos)>();
        var classifier = new BasicClassifier(null, Lookup, Count, WordLexicon.SensesOf, (lemma, pos) =>
        {
            asked.Add((lemma, pos));
            return UsagePrior.RankOf(lemma, pos);
        });

        // Act
        await classifier.ClassifyAsync(Sentence, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(asked);
        Assert.All(asked, a => Assert.True(Count(a.Lemma, a.Pos) >= 2));
    }

    /// <summary>
    ///     A ranking function that returns fewer than two senses for a polysemous slot gives no ranking: the analysis lists
    ///     no senses, is not marked as ranked by usage, and the word is still answered at the part-of-speech level, so a
    ///     usage result can never become a one-sense answer.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_RankingOfFewerThanTwoSenses_IsNoRanking()
    {
        // Arrange: a ranking function that returns only the first sense of every slot
        var classifier = new BasicClassifier(null, Lookup, Count, WordLexicon.SensesOf, (lemma, pos) =>
            [.. UsagePrior.RankOf(lemma, pos).Take(1)]);

        // Act
        var dog = (await classifier.ClassifyAsync(Sentence, TestContext.Current.CancellationToken))[1];

        // Assert
        var polysemous = dog.Analyses.Where(a => Count(a.Details.Lemma, a.Details.PartOfSpeech) >= 2).ToList();
        Assert.NotEmpty(polysemous);
        Assert.All(polysemous, a => Assert.True(a.Senses.IsEmpty && !a.SensesRankedByUsage));
        Assert.Equal(SenseLevel.PartOfSpeech, dog.Level);
        Assert.Null(dog.SenseId);
    }

    /// <summary>
    ///     An analysis whose lemma and part of speech have no sense lists no senses.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_AnalysisWithoutSenses_ListsNoSenses()
    {
        // Act
        var results = await new BasicClassifier().ClassifyAsync(Sentence, TestContext.Current.CancellationToken);
        var senseless = results.SelectMany(r => r.Analyses).Where(a => Count(a.Details.Lemma, a.Details.PartOfSpeech) == 0).ToList();

        // Assert
        Assert.NotEmpty(senseless);
        Assert.All(senseless, a => Assert.True(a.Senses.IsEmpty));
    }

    /// <summary>
    ///     Without a ranking delegate (the explicit constructor of the tests) an analysis with several senses lists none,
    ///     as the classifier did before the prior.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_WithoutAPrior_ListsNoSensesForAPolysemousAnalysis()
    {
        // Act
        var results = await WithoutPrior().ClassifyAsync(Sentence, TestContext.Current.CancellationToken);

        // Assert
        Assert.All(results.SelectMany(r => r.Analyses).Where(a => Count(a.Details.Lemma, a.Details.PartOfSpeech) >= 2), a => Assert.True(a.Senses.IsEmpty));
    }

    /// <summary>
    ///     The key of the result of a polysemous word names the label of the top-ranked sense of the selected analysis
    ///     (rule b of the key).
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_PolysemousWord_KeyCarriesTheLabelOfTheTopRankedSense()
    {
        // Act
        var dog = (await new BasicClassifier().ClassifyAsync(Sentence, TestContext.Current.CancellationToken))[1];
        var top = dog.Analyses.First(a => a.Details.Equals(dog.Details)).Senses[0].SenseId;

        // Assert
        Assert.EndsWith("#" + top[(top.IndexOf('#', StringComparison.Ordinal) + 1)..], dog.Key, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The key of the result of a polysemous word is a member of the keys of the lexicon.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_PolysemousWord_KeyIsOneOfTheEnumeratedKeys()
    {
        // Act
        var dog = (await new BasicClassifier().ClassifyAsync(Sentence, TestContext.Current.CancellationToken))[1];

        // Assert
        Assert.Contains(dog.Key!, WordLexicon.Keys().Where(k => k.StartsWith("dog|", StringComparison.Ordinal)));
    }

    /// <summary>
    ///     The ranking is the same for every occurrence of a word and for every instance (it depends on the lemma and
    ///     part of speech only), also when the word has a single analysis and its result is shared.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_RankingDoesNotDependOnContext()
    {
        // Act
        var first = await new BasicClassifier().ClassifyAsync(["The", "ball", "rolled", "."], TestContext.Current.CancellationToken);
        var second = await new BasicClassifier().ClassifyAsync(["She", "kicked", "a", "ball", "at", "the", "party", "."], TestContext.Current.CancellationToken);
        var a = first[1].Analyses.First(x => x.Details.Equals(first[1].Details)).Senses;
        var b = second[3].Analyses.First(x => x.Details.Equals(second[3].Details)).Senses;

        // Assert
        Assert.Equal(a, b);
        Assert.True(a.Length >= 2);
    }

    /// <summary>
    ///     Every analysis whose senses are ranked by the usage prior says so, so that a consumer can tell the ranking from a
    ///     score of the context; an analysis with one sense, or none, does not.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_UsagePrior_MarksTheSensesAsRankedByUsage()
    {
        // Act
        var results = await new BasicClassifier().ClassifyAsync(Sentence, TestContext.Current.CancellationToken);
        var analyses = results.SelectMany(r => r.Analyses).ToList();

        // Assert
        var polysemous = analyses.Where(a => Count(a.Details.Lemma, a.Details.PartOfSpeech) >= 2).ToList();
        Assert.NotEmpty(polysemous);
        Assert.All(polysemous, a => Assert.True(a.SensesRankedByUsage));
        var other = analyses.Where(a => Count(a.Details.Lemma, a.Details.PartOfSpeech) < 2).ToList();
        Assert.NotEmpty(other);
        Assert.All(other, a => Assert.False(a.SensesRankedByUsage));
        Assert.Contains(other, a => a.Senses.Length == 1);
    }

    /// <summary>
    ///     Without a ranking delegate no senses are listed and nothing is marked as ranked by usage.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicClassifier_ClassifyAsync_WithoutAPrior_MarksNothingAsRankedByUsage()
    {
        // Act
        var results = await WithoutPrior().ClassifyAsync(Sentence, TestContext.Current.CancellationToken);

        // Assert
        Assert.All(results.SelectMany(r => r.Analyses), a => Assert.False(a.SensesRankedByUsage));
    }
}
