namespace DemaConsulting.WordSense.Basic.Tests;

/// <summary>
///     Tests for <see cref="Rules"/>.
/// </summary>
public class RulesTests
{
    private static readonly HashSet<PartOfSpeech> NounVerb = [PartOfSpeech.Noun, PartOfSpeech.Verb];
    private static readonly HashSet<PartOfSpeech> NounAdjective = [PartOfSpeech.Noun, PartOfSpeech.Adjective];
    private static readonly HashSet<PartOfSpeech> AdjectiveVerb = [PartOfSpeech.Adjective, PartOfSpeech.Verb];

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
    ///     The infinitive marker selects the verb.
    /// </summary>
    [Fact]
    public void Rules_Evaluate_AfterTo_SelectsVerb()
    {
        Assert.Equal(Rules.AfterTo, Rules.Evaluate("to", null, NounVerb)?.Rule);
    }

    /// <summary>
    ///     A subject pronoun before a word that can be a verb selects the verb.
    /// </summary>
    [Fact]
    public void Rules_Evaluate_AfterSubjectPronoun_SelectsVerb()
    {
        var match = Rules.Evaluate("they", null, NounVerb);

        Assert.Equal(new RuleMatch(Rules.AfterSubjectPronoun, PartOfSpeech.Verb, Rules.AfterSubjectPronounConfidence), match);
    }

    /// <summary>
    ///     A determiner before a word that can be a noun selects the noun.
    /// </summary>
    [Fact]
    public void Rules_Evaluate_AfterDeterminer_PrefersNoun()
    {
        var match = Rules.Evaluate("the", null, NounAdjective);

        Assert.Equal(new RuleMatch(Rules.AfterDeterminer, PartOfSpeech.Noun, Rules.AfterDeterminerConfidence), match);
    }

    /// <summary>
    ///     A determiner before a word that cannot be a noun selects the adjective.
    /// </summary>
    [Fact]
    public void Rules_Evaluate_AfterDeterminer_FallsBackToAdjective()
    {
        var match = Rules.Evaluate("the", null, AdjectiveVerb);

        Assert.Equal(new RuleMatch(Rules.AfterDeterminer, PartOfSpeech.Adjective, Rules.AfterDeterminerConfidence), match);
    }

    /// <summary>
    ///     A degree adverb before a word that can be an adjective selects the adjective.
    /// </summary>
    [Fact]
    public void Rules_Evaluate_AfterDegreeAdverb_SelectsAdjective()
    {
        var match = Rules.Evaluate("very", null, AdjectiveVerb);

        Assert.Equal(new RuleMatch(Rules.AfterDegreeAdverb, PartOfSpeech.Adjective, Rules.AfterDegreeAdverbConfidence), match);
    }

    /// <summary>
    ///     A preposition before a word that can be a noun selects the noun.
    /// </summary>
    [Fact]
    public void Rules_Evaluate_AfterPreposition_SelectsNoun()
    {
        var match = Rules.Evaluate("with", null, NounVerb);

        Assert.Equal(new RuleMatch(Rules.AfterPreposition, PartOfSpeech.Noun, Rules.AfterPrepositionConfidence), match);
    }

    /// <summary>
    ///     An auxiliary verb after a word that can be a noun selects the noun.
    /// </summary>
    [Fact]
    public void Rules_Evaluate_BeforeAuxiliary_SelectsNoun()
    {
        var match = Rules.Evaluate(null, "was", NounVerb);

        Assert.Equal(new RuleMatch(Rules.BeforeAuxiliary, PartOfSpeech.Noun, Rules.BeforeAuxiliaryConfidence), match);
    }

    /// <summary>
    ///     A word that can only be a noun after a word that can be an adjective selects the adjective.
    /// </summary>
    [Fact]
    public void Rules_Evaluate_BeforeNoun_SelectsAdjective()
    {
        var onlyNoun = WordLexicon.Words.First(pair => pair.Value.All(s => s.PartOfSpeech == PartOfSpeech.Noun) && pair.Key.All(char.IsAsciiLetterLower)).Key;

        var match = Rules.Evaluate(null, onlyNoun, NounAdjective);

        Assert.Equal(new RuleMatch(Rules.BeforeNoun, PartOfSpeech.Adjective, Rules.BeforeNounConfidence), match);
    }

    /// <summary>
    ///     The rule names list every rule in the order they are tried.
    /// </summary>
    [Fact]
    public void Rules_RuleNames_ListEveryRuleInOrder()
    {
        Assert.Equal(
            [
                Rules.AfterModal,
                Rules.AfterTo,
                Rules.AfterSubjectPronoun,
                Rules.AfterDeterminer,
                Rules.AfterDegreeAdverb,
                Rules.AfterPreposition,
                Rules.BeforeAuxiliary,
                Rules.BeforeNoun,
            ],
            Rules.RuleNames);
    }

    /// <summary>
    ///     When two rules could fire, the earlier one in the order decides.
    /// </summary>
    [Fact]
    public void Rules_Evaluate_FirstRuleWins()
    {
        // The word before is a modal and the word after is an auxiliary: the modal rule is tried first
        var match = Rules.Evaluate("will", "was", NounVerb);

        Assert.Equal(Rules.AfterModal, match?.Rule);
    }

    /// <summary>
    ///     Every rule has a fixed confidence that is neither 0 nor 1.
    /// </summary>
    [Fact]
    public void Rules_Confidences_AreFixedPerRuleAndBetweenZeroAndOne()
    {
        Assert.Equal(Rules.RuleNames.Order(StringComparer.Ordinal), Rules.Confidences.Keys.Order(StringComparer.Ordinal));
        Assert.All(Rules.Confidences.Values, c => Assert.InRange(c, 0.01, 0.99));
    }

    /// <summary>
    ///     A result carries exactly the confidence of the rule that fired.
    /// </summary>
    [Fact]
    public void Rules_Evaluate_ResultCarriesRuleConfidence()
    {
        var match = Rules.Evaluate("to", null, NounVerb);

        Assert.NotNull(match);
        Assert.Equal(Rules.Confidences[match.Value.Rule], match.Value.Confidence);
    }

    /// <summary>
    ///     A rule that needs a part of speech does not fire when the word cannot have it.
    /// </summary>
    [Fact]
    public void Rules_Evaluate_SelectedPartOfSpeechNotACandidate_DoesNotFire()
    {
        Assert.Null(Rules.Evaluate("will", null, NounAdjective));
    }

    /// <summary>
    ///     Without any neighbor context no rule fires.
    /// </summary>
    [Fact]
    public void Rules_Evaluate_NoRuleFires_ReturnsNull()
    {
        Assert.Null(Rules.Evaluate(null, null, NounVerb));
    }
}
