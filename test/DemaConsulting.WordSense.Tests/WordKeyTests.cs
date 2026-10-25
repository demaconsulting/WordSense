using System.Collections.Immutable;

// cspell:ignore percoid

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests of the word keys: the key of a classification result at each level, the text form and its parser, and the
///     enumeration of every key of the lexicon.
/// </summary>
public class WordKeyTests
{
    private static readonly Lazy<HashSet<string>> AllKeys = new(() => [.. WordLexicon.Keys()]);

    private static readonly WordDetails Bass = new("bass", PartOfSpeech.Noun, "Number=Sing");

    /// <summary>
    ///     At the sense level the key has the label of the answered sense.
    /// </summary>
    [Fact]
    public void ClassifyResult_Key_SenseLevel_HasTheLabelOfTheAnsweredSense()
    {
        // Arrange: a sense answer whose sense is not the top-ranked one
        var result = Result("Bass", Bass, SenseLevel.Sense, "bass#percoid", null, Ranked("bass#sea-bass", 0.7, "bass#percoid", 0.3));

        // Act
        var key = result.Key;

        // Assert
        Assert.Equal("bass|bass/Noun/Number=Sing#percoid", key);
    }

    /// <summary>
    ///     A group id never enters the key, at the sense level or at the group level.
    /// </summary>
    [Fact]
    public void ClassifyResult_Key_GroupId_IsNeverUsed()
    {
        // Arrange
        var sense = new ClassifyResult("bass", true, [new(Bass, 1, [])], Bass, SenseLevel.Sense, 0.9, "bass#group:fish", "bass#percoid");
        var group = new ClassifyResult("bass", true, [new(Bass, 1, [])], Bass, SenseLevel.Group, 0.9, "bass#group:fish", null);

        // Act / Assert
        Assert.Equal("bass|bass/Noun/Number=Sing#percoid", sense.Key);
        Assert.Equal("bass|bass/Noun/Number=Sing", group.Key);
    }

    /// <summary>
    ///     At the group level and at the part-of-speech level the key has the label of the most probable ranked sense of
    ///     the selected analysis, wherever it stands in the list of the model.
    /// </summary>
    [Theory]
    [InlineData(SenseLevel.Group)]
    [InlineData(SenseLevel.PartOfSpeech)]
    public void ClassifyResult_Key_RankedSenses_FallBackToTheTopRankedSense(SenseLevel level)
    {
        // Arrange: the list is not sorted, so the probability decides
        var result = Result("bass", Bass, level, null, "bass#group:fish", Ranked("bass#basso", 0.2, "bass#sea-bass", 0.5, "bass#percoid", 0.3));

        // Act
        var key = result.Key;

        // Assert
        Assert.Equal("bass|bass/Noun/Number=Sing#sea-bass", key);
    }

    /// <summary>
    ///     Equal probabilities are decided by the order of the ranking: the first listed sense is the top-ranked one (the
    ///     models list equal probabilities in the inventory order of the lemma), whatever the ordinal order of the ids.
    /// </summary>
    [Fact]
    public void ClassifyResult_Key_TiedRankedSenses_TakeTheFirstListed()
    {
        // Arrange
        var result = Result("bass", Bass, SenseLevel.PartOfSpeech, null, null, Ranked("bass#sea-bass", 0.5, "bass#percoid", 0.5));

        // Act / Assert
        Assert.Equal("bass|bass/Noun/Number=Sing#sea-bass", result.Key);
    }

    /// <summary>
    ///     The ranking of the selected analysis is used, not that of another analysis of the word.
    /// </summary>
    [Fact]
    public void ClassifyResult_Key_RankingOfAnotherAnalysis_IsNotUsed()
    {
        // Arrange: the selected analysis is the verb, which has no ranking; the noun has one
        var verb = new WordDetails("bass", PartOfSpeech.Verb, "VerbForm=Inf");
        var analyses = ImmutableArray.Create(new AnalysisCandidate(verb, 0.6, []), new AnalysisCandidate(Bass, 0.4, [new("bass#percoid", 1)]));
        var result = new ClassifyResult("bass", true, analyses, verb, SenseLevel.PartOfSpeech, 0.6, null, null);

        // Act / Assert
        Assert.Equal("bass|bass/Verb/VerbForm=Inf", result.Key);
    }

    /// <summary>
    ///     A part-of-speech answer without a ranking has no label.
    /// </summary>
    [Fact]
    public void ClassifyResult_Key_PartOfSpeechLevelWithoutRanking_HasNoLabel()
    {
        // Arrange
        var result = Result("bass", Bass, SenseLevel.PartOfSpeech, null, null, []);

        // Act / Assert
        Assert.Equal("bass|bass/Noun/Number=Sing", result.Key);
    }

    /// <summary>
    ///     Empty features leave out the slash and the features.
    /// </summary>
    [Fact]
    public void ClassifyResult_Key_EmptyFeatures_LeaveOutTheFeatureSegment()
    {
        // Arrange
        var details = new WordDetails("the", PartOfSpeech.Determiner, string.Empty);
        var result = Result("The", details, SenseLevel.Sense, "the#determiner", null, []);

        // Act / Assert
        Assert.Equal("the|the/Determiner#determiner", result.Key);
    }

    /// <summary>
    ///     An unknown word, a default result and a known word without a selected analysis have no key.
    /// </summary>
    [Fact]
    public void ClassifyResult_Key_UnknownOrUnanswered_IsNull()
    {
        // Arrange
        var unanswered = new ClassifyResult("record", true, [], null, SenseLevel.None, 0, null, null);

        // Act / Assert
        Assert.Null(ClassifyResult.Unknown("zzqxjw").Key);
        Assert.Null(default(ClassifyResult).Key);
        Assert.Null(unanswered.Key);
    }

    /// <summary>
    ///     The spelling is the normalized word: lower-cased, with a typographic apostrophe straightened; the lemma keeps
    ///     its case.
    /// </summary>
    [Fact]
    public void ClassifyResult_Key_CapitalizedWordAndLemma_NormalizeOnlyTheSpelling()
    {
        // Arrange
        var polish = new WordDetails("Polish", PartOfSpeech.Noun, "Number=Sing");
        var contraction = new WordDetails("do", PartOfSpeech.Auxiliary, "Polarity=Neg");

        // Act / Assert
        Assert.Equal("polish|Polish/Noun/Number=Sing", Result("POLISH", polish, SenseLevel.PartOfSpeech, null, null, []).Key);
        Assert.Equal("don't|do/Auxiliary/Polarity=Neg", Result("Don\u2019t", contraction, SenseLevel.PartOfSpeech, null, null, []).Key);
    }

    /// <summary>
    ///     The Basic model gives a possessive and a contraction their normalized spellings and the possessive feature.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task ClassifyResult_Key_ContractionAndPossessive_AreKeyedBySpelling()
    {
        // Arrange
        using var model = new BasicWordSenseModel();

        // Act
        var results = await model.ClassifyAsync(["Don\u2019t", "the", "dog's", "bone", "."], TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith("don't|", results[0].Key, StringComparison.Ordinal);
        Assert.StartsWith("dog's|dog/Noun/Number=Sing+Poss=Yes", results[2].Key, StringComparison.Ordinal);
        Assert.Null(results[4].Key);
        Assert.All(results.Where(r => r.IsKnown), r => Assert.Contains(r.Key!, AllKeys.Value));
    }

    /// <summary>
    ///     A key is an immutable value: two keys with the same parts are equal and have the same hash code, and a changed
    ///     copy is another key.
    /// </summary>
    [Fact]
    public void WordKey_Value_SameParts_AreEqual()
    {
        // Arrange
        var key = new WordKey("bass", "bass", PartOfSpeech.Noun, "Number=Sing", "percoid");
        var same = new WordKey("bass", "bass", PartOfSpeech.Noun, "Number=Sing", "percoid");

        // Act
        var changed = key with { Label = "basso" };

        // Assert
        Assert.Equal(key, same);
        Assert.Equal(key.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(key, changed);
        Assert.Equal("percoid", key.Label);
    }

    /// <summary>
    ///     The text form puts the parts in order and leaves out the empty features and the missing label.
    /// </summary>
    [Fact]
    public void WordKey_ToString_Parts_AreWrittenInTheCanonicalOrder()
    {
        // Arrange / Act / Assert
        Assert.Equal("bass|bass/Noun/Number=Sing#percoid", new WordKey("bass", "bass", PartOfSpeech.Noun, "Number=Sing", "percoid").ToString());
        Assert.Equal("polish|Polish/Noun/Number=Sing", new WordKey("polish", "Polish", PartOfSpeech.Noun, "Number=Sing", null).ToString());
        Assert.Equal("the|the/Determiner#determiner", new WordKey("the", "the", PartOfSpeech.Determiner, string.Empty, "determiner").ToString());
        Assert.Equal("of|of/Preposition", new WordKey("of", "of", PartOfSpeech.Preposition, string.Empty, null).ToString());
    }

    /// <summary>
    ///     The separators in a part are escaped with a backslash and nothing else is.
    /// </summary>
    [Fact]
    public void WordKey_ToString_SeparatorsInParts_AreEscaped()
    {
        // Arrange
        var key = new WordKey("a/c", "A/C", PartOfSpeech.Noun, "Number=Sing", "x#y|z\\w");

        // Act / Assert
        Assert.Equal("a\\/c|A\\/C/Noun/Number=Sing#x\\#y\\|z\\\\w", key.ToString());
    }

    /// <summary>
    ///     Parsing the text of a key gives the key back, escapes included.
    /// </summary>
    /// <param name="text">The key text.</param>
    [Theory]
    [InlineData("bass|bass/Noun/Number=Sing#percoid")]
    [InlineData("polish|Polish/Noun/Number=Sing")]
    [InlineData("the|the/Determiner#determiner")]
    [InlineData("of|of/Preposition")]
    [InlineData("don't|do/Auxiliary/Polarity=Neg")]
    [InlineData("dog's|dog/Noun/Number=Sing+Poss=Yes")]
    [InlineData("a\\/c|A\\/C/Noun/Number=Sing")]
    [InlineData("x\\|y|x\\#y\\\\/Verb#a\\/b")]
    public void WordKey_TryParse_ValidKey_RoundTrips(string text)
    {
        // Arrange / Act
        var parsed = WordKey.TryParse(text, out var key);

        // Assert
        Assert.True(parsed);
        Assert.Equal(text, key!.ToString());
        Assert.True(WordKey.TryParse(key.ToString(), out var again));
        Assert.Equal(key, again);
    }

    /// <summary>
    ///     The parts of a parsed key are unescaped.
    /// </summary>
    [Fact]
    public void WordKey_TryParse_EscapedKey_GivesTheUnescapedParts()
    {
        // Arrange / Act
        var parsed = WordKey.TryParse("a\\/c|A\\/C/Noun/Number=Sing#x\\#y", out var key);

        // Assert
        Assert.True(parsed);
        Assert.Equal(new WordKey("a/c", "A/C", PartOfSpeech.Noun, "Number=Sing", "x#y"), key);
    }

    /// <summary>
    ///     Text that is not a key in canonical form is refused, including a spelling that is not its own lexicon key (upper
    ///     case, a typographic apostrophe or no letter).
    /// </summary>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bass")]
    [InlineData("bass|bass")]
    [InlineData("|bass/Noun")]
    [InlineData("bass|/Noun")]
    [InlineData("bass|bass/")]
    [InlineData("bass|bass/Noun/")]
    [InlineData("bass|bass/Noun#")]
    [InlineData("bass|bass/Noun/#x")]
    [InlineData("bass|bass/Nouns")]
    [InlineData("bass|bass/noun")]
    [InlineData("bass|bass/2")]
    [InlineData("bass|bass/Noun/Number=Sing/extra")]
    [InlineData("bass|bass/Noun#a#b")]
    [InlineData("a/c|A/C/Noun")]
    [InlineData("a|b|c/Noun")]
    [InlineData("bass|bass/Noun\\")]
    [InlineData("bass|ba\\ss/Noun")]
    [InlineData("POLISH|Polish/Noun")]
    [InlineData("don\u2019t|do/Auxiliary")]
    [InlineData("3|3/Noun")]
    public void WordKey_TryParse_NotAKey_IsRefused(string? text)
    {
        // Arrange / Act
        var parsed = WordKey.TryParse(text, out var key);

        // Assert
        Assert.False(parsed);
        Assert.Null(key);
    }

    /// <summary>
    ///     No key is listed twice: the number of keys of one pass equals the number of distinct keys.
    /// </summary>
    [Fact]
    public void WordLexicon_Keys_Enumeration_ListsNoKeyTwice()
    {
        // Arrange
        var listed = 0;

        // Act
        foreach (var _ in WordLexicon.Keys())
        {
            listed++;
        }

        // Assert
        Assert.True(listed > 1_000_000, $"{listed} keys");
        Assert.Equal(AllKeys.Value.Count, listed);
    }

    /// <summary>
    ///     Every key of the enumeration parses, formats back to itself and has a spelling in normalized form (lower case,
    ///     straight apostrophe).
    /// </summary>
    [Fact]
    public void WordLexicon_Keys_EveryKey_ParsesBackToItselfWithANormalizedSpelling()
    {
        // Arrange
        var problems = new List<string>();

        // Act
        foreach (var text in AllKeys.Value)
        {
            if (!WordKey.TryParse(text, out var key) || key.ToString() != text)
            {
                problems.Add($"no round trip {text}");
            }
            else if (key.Spelling != key.Spelling.ToLowerInvariant() || key.Spelling.Contains('\u2019', StringComparison.Ordinal))
            {
                problems.Add($"not normalized {text}");
            }
        }

        // Assert
        Assert.True(problems.Count == 0, $"{problems.Count} problems: {string.Join("; ", problems.Take(10))}");
    }

    /// <summary>
    ///     The label of every labeled key is a sense of the lemma and part of speech of the key in the catalog.
    /// </summary>
    [Fact]
    public void WordLexicon_Keys_LabeledKeys_NameASenseOfTheLemmaAndPartOfSpeech()
    {
        // Arrange
        var problems = new List<string>();
        var labeled = 0;

        // Act
        foreach (var text in AllKeys.Value)
        {
            if (WordKey.TryParse(text, out var key) && key.Label is not null)
            {
                labeled++;
                if (!WordLexicon.TryGetSense(key.Lemma + "#" + key.Label, out var sense) || sense.PartOfSpeech != key.PartOfSpeech)
                {
                    problems.Add($"no such sense {text}");
                }
            }
        }

        // Assert
        Assert.True(problems.Count == 0, $"{problems.Count} problems: {string.Join("; ", problems.Take(10))}");
        Assert.True(labeled > 0);
    }

    /// <summary>
    ///     The keys of words that the lexicon looks up (capitalized lemmas, tense forms, spellings of one analysis,
    ///     contractions, a lemma with a slash and possessives that are not stored) are all enumerated: the key without a
    ///     label and one key for each sense of every analysis.
    /// </summary>
    /// <param name="word">The word as written.</param>
    [Theory]
    [InlineData("bass")]
    [InlineData("Polish")]
    [InlineData("POLISH")]
    [InlineData("read")]
    [InlineData("put")]
    [InlineData("dreamt")]
    [InlineData("dreamed")]
    [InlineData("don't")]
    [InlineData("A/C")]
    [InlineData("dog's")]
    [InlineData("dogs'")]
    [InlineData("Jack's")]
    public void WordLexicon_Keys_LookedUpWord_HasAKeyForEachAnalysisAndSense(string word)
    {
        // Arrange
        var lookedUp = WordLexicon.Lookup(word);
        var spelling = word.ToLowerInvariant();

        // Act / Assert
        Assert.NotEmpty(lookedUp);
        foreach (var analysis in lookedUp)
        {
            Assert.Contains(new WordKey(spelling, analysis.Lemma, analysis.PartOfSpeech, analysis.Features, null).ToString(), AllKeys.Value);
            foreach (var sense in WordLexicon.SensesOf(analysis.Lemma, analysis.PartOfSpeech))
            {
                var label = sense.SenseId[(sense.SenseId.IndexOf('#', StringComparison.Ordinal) + 1)..];
                Assert.Contains(new WordKey(spelling, analysis.Lemma, analysis.PartOfSpeech, analysis.Features, label).ToString(), AllKeys.Value);
            }
        }
    }

    /// <summary>
    ///     Two forms of one analysis that are spelled differently have different keys.
    /// </summary>
    [Fact]
    public void WordLexicon_Keys_FormsOfOneAnalysis_AreKeyedBySpelling()
    {
        // Arrange
        var keys = AllKeys.Value;

        // Act
        var dreamt = keys.Where(k => k.StartsWith("dreamt|", StringComparison.Ordinal)).ToList();
        var dreamed = keys.Where(k => k.StartsWith("dreamed|", StringComparison.Ordinal)).ToList();

        // Assert
        Assert.NotEmpty(dreamt);
        Assert.NotEmpty(dreamed);
        Assert.All(dreamt, k => Assert.Contains("|dream/Verb/", k, StringComparison.Ordinal));
    }

    /// <summary>
    ///     A sample of the spellings of the lexicon, in the case as stored and in upper case, has every key of its lookup
    ///     enumerated: the enumeration is a superset of what any lookup can give.
    /// </summary>
    [Fact]
    public void WordLexicon_Keys_SampledSpellings_CoverEveryLookup()
    {
        // Arrange
        var keys = AllKeys.Value;
        var image = WordLexicon.Current;
        var missing = new List<string>();

        // Act
        for (var i = 0; i < image.SpellingCount; i += 7)
        {
            var spelling = image.Spelling(i);
            foreach (var word in new[] { spelling, spelling.ToUpperInvariant() }.Distinct())
            {
                var normalized = word.ToLowerInvariant().Replace('\u2019', '\'');
                foreach (var analysis in WordLexicon.Lookup(word))
                {
                    var key = new WordKey(normalized, analysis.Lemma, analysis.PartOfSpeech, analysis.Features, null).ToString();
                    if (!keys.Contains(key))
                    {
                        missing.Add(key);
                    }
                }
            }
        }

        // Assert
        Assert.True(missing.Count == 0, $"{missing.Count} keys are not enumerated: {string.Join("; ", missing.Take(10))}");
    }

    /// <summary>
    ///     The enumeration has deferred execution (calling it reads nothing, the sequence is read on demand) and a fixed
    ///     order: every enumeration gives the same keys in the same order.
    /// </summary>
    [Fact]
    public void WordLexicon_Keys_Enumeration_IsDeferredAndRepeatable()
    {
        // Arrange / Act
        var sequence = WordLexicon.Keys();
        var first = sequence.Take(5000).ToList();
        var second = WordLexicon.Keys().Take(5000).ToList();
        var again = sequence.Take(5000).ToList();

        // Assert
        Assert.Equal(5000, first.Count);
        Assert.Equal(first, second);
        Assert.Equal(first, again);
    }

    /// <summary>
    ///     The keys of the words of the reviewed suite and of a sample of corpus sentences that the Basic model gives are
    ///     all enumerated, and only an unknown word has no key.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task BasicKeys_SuiteAndCorpusSentences_AreAllEnumerated()
    {
        // Arrange
        using var model = new BasicWordSenseModel();

        // Act
        var problems = await KeyCompleteness.ProblemsAsync(model, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(problems.Count == 0, $"{problems.Count} key problem(s): {string.Join("; ", problems.Take(20))}");
    }

    private static ClassifyResult Result(string word, WordDetails details, SenseLevel level, string? senseId, string? groupId, ImmutableArray<SenseCandidate> senses) =>
        new(word, true, [new(details, 1, senses)], details, level, 0.9, groupId, senseId);

    private static ImmutableArray<SenseCandidate> Ranked(params object[] idsAndProbabilities)
    {
        var senses = ImmutableArray.CreateBuilder<SenseCandidate>();
        for (var i = 0; i < idsAndProbabilities.Length; i += 2)
        {
            senses.Add(new SenseCandidate((string)idsAndProbabilities[i], (double)idsAndProbabilities[i + 1]));
        }

        return senses.ToImmutable();
    }
}
