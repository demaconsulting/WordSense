namespace DemaConsulting.WordSense.Basic.Tests;

/// <summary>
///     Tests for <see cref="Rules"/>.
/// </summary>
public class RulesTests
{
    private static readonly HashSet<PartOfSpeech> NounVerb = [PartOfSpeech.Noun, PartOfSpeech.Verb];

    /// <summary>
    ///     A modal before a word that can be a verb selects the verb.
    /// </summary>
    [Fact]
    public void Rules_Evaluate_AfterModal_SelectsVerb()
    {
        var match = Rules.Evaluate("will", "the", NounVerb);

        Assert.Equal(new RuleMatch(Rules.AfterModal, PartOfSpeech.Verb, Rules.AfterModalConfidence), match);
    }

    /// <summary>
    ///     A determiner before a word that can be a noun selects the noun.
    /// </summary>
    [Fact]
    public void Rules_Evaluate_AfterDeterminer_SelectsNoun()
    {
        var match = Rules.Evaluate("the", null, NounVerb);

        Assert.Equal(new RuleMatch(Rules.AfterDeterminer, PartOfSpeech.Noun, Rules.AfterDeterminerConfidence), match);
    }

    /// <summary>
    ///     The infinitive marker selects the verb.
    /// </summary>
    [Fact]
    public void Rules_Evaluate_AfterTo_SelectsVerb()
    {
        Assert.Equal(Rules.AfterTo, Rules.Evaluate("to", null, NounVerb)?.Rule);
    }

    /// <summary>
    ///     Without any neighbor context no rule fires.
    /// </summary>
    [Fact]
    public void Rules_Evaluate_NoContext_ReturnsNull()
    {
        Assert.Null(Rules.Evaluate(null, null, NounVerb));
    }

    /// <summary>
    ///     A rule that needs a part of speech does not fire when the word cannot have it.
    /// </summary>
    [Fact]
    public void Rules_Evaluate_CandidateMissing_DoesNotFire()
    {
        Assert.Null(Rules.Evaluate("will", null, new HashSet<PartOfSpeech> { PartOfSpeech.Noun, PartOfSpeech.Adjective }));
    }

    /// <summary>
    ///     Every rule has a documented confidence in the open interval between 0 and 1.
    /// </summary>
    [Fact]
    public void Rules_Confidences_CoverEveryRule()
    {
        Assert.Equal(Rules.RuleNames.Order(StringComparer.Ordinal), Rules.Confidences.Keys.Order(StringComparer.Ordinal));
        Assert.All(Rules.Confidences.Values, c => Assert.InRange(c, 0.01, 0.99));
    }
}
