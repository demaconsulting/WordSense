namespace DemaConsulting.WordSense.BertBaseCased.Tests;

/// <summary>
///     End-to-end test of the BertBaseCased model. It runs the real model and is skipped when the model files are
///     absent (for example a checkout without Git LFS content).
/// </summary>
public class EndToEndTests
{
    /// <summary>
    ///     The word <c>record</c> is a verb after "will" and a noun after "the".
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BertBaseCased_Classifier_EndToEnd_ClassifiesRecord()
    {
        using var model = new BertBaseCasedWordSenseModel(ModelFolder.Require()) { IntraOpThreads = 2 };
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        var senses = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);

        Assert.Equal(6, senses.Length);
        Assert.Equal(PartOfSpeech.Verb, senses[2].Details?.PartOfSpeech);
        Assert.Equal(PartOfSpeech.Noun, senses[4].Details?.PartOfSpeech);
        Assert.All(senses, s => Assert.InRange(s.Confidence, 0.0, 1.0));
    }

    /// <summary>
    ///     A form that names a single sense answers it with full confidence: <c>bowed</c> is only the bend verb.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BertBaseCased_Classifier_EndToEnd_BowedIsOnlyTheBendVerb()
    {
        using var model = new BertBaseCasedWordSenseModel(ModelFolder.Require()) { IntraOpThreads = 2 };
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        var senses = await model.ClassifyAsync(["He", "bowed", "to", "the", "queen", "."], TestContext.Current.CancellationToken);

        Assert.Equal(Assert.Single(WordLexicon.Lookup("bowed")), senses[1].Details);
        Assert.Equal(1.0, senses[1].Confidence);
    }

    /// <summary>
    ///     <c>bows</c> keeps all three senses of <c>bow</c> and the context picks among them.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BertBaseCased_Classifier_EndToEnd_BowsIsResolvedAmongItsThreeSenses()
    {
        using var model = new BertBaseCasedWordSenseModel(ModelFolder.Require()) { IntraOpThreads = 2 };
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        var actors = await model.ClassifyAsync(["The", "actors", "took", "their", "bows", "."], TestContext.Current.CancellationToken);
        var queen = await model.ClassifyAsync(["He", "bows", "to", "the", "queen", "."], TestContext.Current.CancellationToken);
        var archer = await model.ClassifyAsync(["She", "drew", "back", "the", "bows", "."], TestContext.Current.CancellationToken);

        var senses = WordLexicon.Lookup("bows");
        Assert.Equal(3, senses.Length);
        Assert.Contains(actors[4].Details, senses);
        Assert.Contains(queen[1].Details, senses);
        Assert.Contains(archer[4].Details, senses);
        Assert.Equal(PartOfSpeech.Verb, queen[1].Details?.PartOfSpeech);
    }

    /// <summary>
    ///     <c>reads</c> is always the present tense, while <c>read</c> is told apart by its context.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BertBaseCased_Classifier_EndToEnd_ReadsIsPresentAndReadSplitsByContext()
    {
        using var model = new BertBaseCasedWordSenseModel(ModelFolder.Require()) { IntraOpThreads = 2 };
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        var reads = await model.ClassifyAsync(["She", "reads", "a", "book", "every", "night", "."], TestContext.Current.CancellationToken);
        var past = await model.ClassifyAsync(["Yesterday", "he", "read", "the", "report", "and", "sent", "comments", "."], TestContext.Current.CancellationToken);
        var present = await model.ClassifyAsync(["She", "loves", "to", "read", "before", "bed", "."], TestContext.Current.CancellationToken);

        var readsDetails = reads[1].Details!;
        Assert.Equal("read", readsDetails.Lemma);
        Assert.Equal(PartOfSpeech.Verb, readsDetails.PartOfSpeech);
        Assert.Equal("read#present", readsDetails.SenseId);
        Assert.Contains(past[2].Details!.SenseId, new[] { "read#past", "read#present" });
        Assert.Contains(present[3].Details!.SenseId, new[] { "read#past", "read#present" });
    }
}
