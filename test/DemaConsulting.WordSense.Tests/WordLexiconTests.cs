// cspell:ignore betaked
namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="WordLexicon"/>.
/// </summary>
public class WordLexiconTests
{
    private static readonly string[] KnownWords = ["record", "the", "bows", "drove"];

    /// <summary>
    ///     A well-known word with several parts of speech lists them under one key.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_KnownWord_ReturnsItsAnalyses()
    {
        Assert.True(WordLexicon.TryLookup("record", out var senses));

        var parts = senses.Select(s => s.PartOfSpeech).ToHashSet();
        Assert.Contains(PartOfSpeech.Noun, parts);
        Assert.Contains(PartOfSpeech.Verb, parts);
        Assert.All(senses, s => Assert.Equal("record", s.Lemma));
    }

    /// <summary>
    ///     The inflected forms resolve to the lemma of the base form and carry grammatical features.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_InflectedForm_ReturnsEntryLemmaAndFeatures()
    {
        var senses = WordLexicon.Lookup("records");

        Assert.Contains(senses, s => s.Lemma == "record" && s.PartOfSpeech == PartOfSpeech.Noun && s.Features == "Number=Plur");
        Assert.Contains(senses, s => s.Lemma == "record" && s.PartOfSpeech == PartOfSpeech.Verb && s.Features == "Person=3+Tense=Pres");
    }

    /// <summary>
    ///     Regular and irregular inflections of ordinary words resolve to their lemma.
    /// </summary>
    [Theory]
    [InlineData("looked", "look", PartOfSpeech.Verb)]
    [InlineData("seemed", "seem", PartOfSpeech.Verb)]
    [InlineData("trees", "tree", PartOfSpeech.Noun)]
    [InlineData("knows", "know", PartOfSpeech.Verb)]
    [InlineData("having", "have", PartOfSpeech.Verb)]
    public void WordLexicon_Lookup_InflectedFormOfAnyDictionaryWord_ResolvesToLemma(string form, string lemma, PartOfSpeech pos)
    {
        Assert.True(WordLexicon.TryLookup(form, out var senses));

        Assert.Contains(senses, s => s.Lemma == lemma && s.PartOfSpeech == pos);
    }

    /// <summary>
    ///     The word tokens that real books use and that an earlier rebuild of the forms lost (regular inflections that the
    ///     written forms or the variants of the authored forms had shadowed, and inflections of the hand-written additions)
    ///     resolve to a sensible analysis.
    /// </summary>
    [Theory]
    [InlineData("alighted", "alight", PartOfSpeech.Verb)]
    [InlineData("America", "america", PartOfSpeech.Noun)]
    [InlineData("betakes", "betake", PartOfSpeech.Verb)]
    [InlineData("brothers", "brother", PartOfSpeech.Noun)]
    [InlineData("Brothers", "brother", PartOfSpeech.Noun)]
    [InlineData("canallers", "canaller", PartOfSpeech.Noun)]
    [InlineData("complices", "complice", PartOfSpeech.Noun)]
    [InlineData("dismasted", "dismast", PartOfSpeech.Verb)]
    [InlineData("ditchers", "ditcher", PartOfSpeech.Noun)]
    [InlineData("England", "england", PartOfSpeech.Noun)]
    [InlineData("flingers", "flinger", PartOfSpeech.Noun)]
    [InlineData("France", "france", PartOfSpeech.Noun)]
    [InlineData("gnawings", "gnawing", PartOfSpeech.Noun)]
    [InlineData("hundreds", "hundred", PartOfSpeech.Noun)]
    [InlineData("Hundreds", "hundred", PartOfSpeech.Noun)]
    [InlineData("Hursts", "hurst", PartOfSpeech.Noun)]
    [InlineData("India", "india", PartOfSpeech.Noun)]
    [InlineData("Ireland", "ireland", PartOfSpeech.Noun)]
    [InlineData("Irishmen", "irishman", PartOfSpeech.Noun)]
    [InlineData("millions", "million", PartOfSpeech.Noun)]
    [InlineData("oaks", "oak", PartOfSpeech.Noun)]
    [InlineData("others", "other", PartOfSpeech.Noun)]
    [InlineData("Others", "other", PartOfSpeech.Noun)]
    [InlineData("Pandects", "pandect", PartOfSpeech.Noun)]
    [InlineData("peas", "pea", PartOfSpeech.Noun)]
    [InlineData("peetweets", "peetweet", PartOfSpeech.Noun)]
    [InlineData("raffs", "raff", PartOfSpeech.Noun)]
    [InlineData("Scotland", "scotland", PartOfSpeech.Noun)]
    [InlineData("spilled", "spill", PartOfSpeech.Verb)]
    [InlineData("squirrels", "squirrel", PartOfSpeech.Noun)]
    [InlineData("thousands", "thousand", PartOfSpeech.Noun)]
    [InlineData("tossed", "toss", PartOfSpeech.Verb)]
    [InlineData("unplastered", "unplaster", PartOfSpeech.Verb)]
    [InlineData("waked", "wake", PartOfSpeech.Verb)]
    [InlineData("whaleman", "whaleman", PartOfSpeech.Noun)]
    [InlineData("whalemen", "whaleman", PartOfSpeech.Noun)]
    public void WordLexicon_Lookup_FormsLostByEarlierRebuild_ResolveToTheirLemma(string form, string lemma, PartOfSpeech pos)
    {
        var senses = WordLexicon.Lookup(form);

        Assert.Contains(senses, s => string.Equals(s.Lemma, lemma, StringComparison.OrdinalIgnoreCase) && s.PartOfSpeech == pos);
    }

    /// <summary>
    ///     A regular form that is only an alternative to a listed irregular form stays valid (brothers next to brethren,
    ///     tossed next to tost) while the wrong regular forms of the real irregular words stay absent.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_RegularFormNextToListedAlternative_StaysRegularFormOfIrregularWordsStayAbsent()
    {
        Assert.NotEmpty(WordLexicon.Lookup("brothers"));
        Assert.NotEmpty(WordLexicon.Lookup("brethren"));
        Assert.NotEmpty(WordLexicon.Lookup("spilt"));
        Assert.Empty(WordLexicon.Lookup("childs"));
        Assert.Empty(WordLexicon.Lookup("betaked"));
    }

    /// <summary>
    ///     An irregular form that the dictionary already lists is known.
    /// </summary>
    [Theory]
    [InlineData("went")]
    [InlineData("children")]
    [InlineData("happier")]
    public void WordLexicon_Contains_IrregularFormInDictionary_IsKnown(string form)
    {
        Assert.True(WordLexicon.Contains(form));
    }

    /// <summary>
    ///     Wrong regular forms of irregular words are not generated.
    /// </summary>
    [Theory]
    [InlineData("goed")]
    [InlineData("childs")]
    [InlineData("knowed")]
    public void WordLexicon_Contains_WrongRegularFormOfIrregularWord_IsAbsent(string form)
    {
        Assert.False(WordLexicon.Contains(form));
    }

    /// <summary>
    ///     A form with many senses has one analysis for each lemma, part of speech and features, never one for each sense:
    ///     <c>go</c> has dozens of senses and a handful of analyses, and no analysis occurs twice.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_FormWithManySenses_ReturnsOneAnalysisPerLemmaPartAndFeatures()
    {
        var go = WordLexicon.Lookup("go");
        var bows = WordLexicon.Lookup("bows");

        Assert.True(WordLexicon.SensesOf("go", PartOfSpeech.Verb).Length > 20, "go has many senses");
        Assert.InRange(go.Length, 2, 8);
        Assert.Equal(go.Length, go.Distinct().Count());
        Assert.Equal(bows.Length, bows.Distinct().Count());
        Assert.Contains(bows, s => s.Lemma == "bow" && s.PartOfSpeech == PartOfSpeech.Noun && s.Features == "Number=Plur");
        Assert.Contains(bows, s => s.Lemma == "bow" && s.PartOfSpeech == PartOfSpeech.Verb && s.Features == "Person=3+Tense=Pres");
        Assert.All(go, s => Assert.NotEmpty(WordLexicon.SensesOf(s.Lemma, s.PartOfSpeech)));
    }

    /// <summary>
    ///     A capitalized or inflected spelling behaves like its base: <c>Record</c> finds <c>record</c>, and <c>Polish</c>,
    ///     <c>August</c>, <c>using</c>, <c>left</c> and <c>paid</c> have analyses whose lemma and part of speech have senses.
    /// </summary>
    [Theory]
    [InlineData("Polish", "Polish", PartOfSpeech.Adjective)]
    [InlineData("Polish", "polish", PartOfSpeech.Verb)]
    [InlineData("August", "August", PartOfSpeech.Noun)]
    [InlineData("using", "use", PartOfSpeech.Verb)]
    [InlineData("left", "leave", PartOfSpeech.Verb)]
    [InlineData("left", "left", PartOfSpeech.Adjective)]
    [InlineData("paid", "pay", PartOfSpeech.Verb)]
    [InlineData("Records", "record", PartOfSpeech.Noun)]
    public void WordLexicon_Lookup_CapitalizedAndInflectedForms_BehaveLikeTheirBase(string word, string lemma, PartOfSpeech pos)
    {
        var analysis = Assert.Single(WordLexicon.Lookup(word).Where(s => s.Lemma == lemma && s.PartOfSpeech == pos).Take(1));

        Assert.NotEmpty(WordLexicon.SensesOf(analysis.Lemma, analysis.PartOfSpeech));
        Assert.Equal(WordLexicon.Lookup("record"), WordLexicon.Lookup("Record"));
    }

    /// <summary>
    ///     The lexicon lists candidates, it does not choose among them: the exact spelling <c>Jack</c> lists the capitalized
    ///     lemma of its own entry (marked <c>Capitalization=Title</c>) beside the lower-case lemmas of the same spelling.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_ExactCapitalizedSpelling_ListsTheCapitalizedLemmaBesideTheLowerCaseOnes()
    {
        // Arrange: Jack is stored as a spelling of its own, with a capitalized lemma that only that spelling has

        // Act
        var jack = WordLexicon.Lookup("Jack");

        // Assert
        Assert.Contains(jack, s => s.Lemma == "Jack" && s.PartOfSpeech == PartOfSpeech.Noun && s.Features.Contains("Capitalization=Title", StringComparison.Ordinal));
        Assert.Contains(jack, s => s.Lemma == "jack" && s.PartOfSpeech == PartOfSpeech.Verb);
    }

    /// <summary>
    ///     The lower-case spelling of a name that only its exact capitalized spelling has (<c>jack</c>) does not list it.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_LowerCaseSpelling_DoesNotListACapitalizedLemmaOfTheExactSpelling()
    {
        // Arrange: the capitalized lemma Jack belongs to the spelling Jack only

        // Act
        var jack = WordLexicon.Lookup("jack");

        // Assert
        Assert.DoesNotContain(jack, s => s.Lemma == "Jack");
        Assert.Contains(jack, s => s.Lemma == "jack");
    }

    /// <summary>
    ///     <c>Polish</c> has a capitalized and a lower-case lemma in both spellings: the form tables carry the capitalized
    ///     entries into the lower-case spelling.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_PolishInBothSpellings_ListsBothLemmas()
    {
        // Arrange: the spellings Polish and polish

        // Act
        var capitalized = WordLexicon.Lookup("Polish");
        var lower = WordLexicon.Lookup("polish");

        // Assert: the language and the adjective, the shine and the verb, in both
        foreach (var analyses in new[] { capitalized, lower })
        {
            Assert.Contains(analyses, s => s.Lemma == "Polish" && s.PartOfSpeech == PartOfSpeech.Noun);
            Assert.Contains(analyses, s => s.Lemma == "Polish" && s.PartOfSpeech == PartOfSpeech.Adjective);
            Assert.Contains(analyses, s => s.Lemma == "polish" && s.PartOfSpeech == PartOfSpeech.Noun);
            Assert.Contains(analyses, s => s.Lemma == "polish" && s.PartOfSpeech == PartOfSpeech.Verb);
        }
    }

    /// <summary>
    ///     <c>August</c> has a capitalized and a lower-case lemma in both spellings.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_AugustInBothSpellings_ListsBothLemmas()
    {
        // Arrange: the spellings August and august

        // Act
        var capitalized = WordLexicon.Lookup("August");
        var lower = WordLexicon.Lookup("august");

        // Assert: the month and the adjective, in both
        foreach (var analyses in new[] { capitalized, lower })
        {
            Assert.Contains(analyses, s => s.Lemma == "August" && s.PartOfSpeech == PartOfSpeech.Noun);
            Assert.Contains(analyses, s => s.Lemma == "august" && s.PartOfSpeech == PartOfSpeech.Adjective);
        }
    }

    /// <summary>
    ///     The capitalized lemmas that the lookup lists have senses in the catalog.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_CapitalizedLemmas_HaveSenses()
    {
        // Arrange: the capitalized lemmas of Jack, Polish and August

        // Act
        var lemmas = new[] { ("Jack", PartOfSpeech.Noun), ("Polish", PartOfSpeech.Noun), ("polish", PartOfSpeech.Noun), ("August", PartOfSpeech.Noun) };

        // Assert
        Assert.All(lemmas, s => Assert.NotEmpty(WordLexicon.SensesOf(s.Item1, s.Item2)));
    }

    /// <summary>
    ///     A spelling that is stored exactly gives exactly the analyses of that spelling, without a merge with those of
    ///     another spelling.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_StoredSpelling_GivesTheAnalysesOfThatSpellingOnly()
    {
        // Arrange: Polish is stored as a spelling
        var image = WordLexicon.Current;

        // Act
        var lookup = WordLexicon.Lookup("Polish");

        // Assert
        Assert.Equal(image.Analyses(image.Find("Polish")), lookup);
        Assert.NotEqual(WordLexicon.Lookup("polish"), lookup);
    }

    /// <summary>
    ///     A spelling that is not stored (<c>POLISH</c>) gives the analyses of its lower-case spelling first and then those
    ///     of the other stored spellings with the same lower-case form, each analysis once.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_SpellingThatIsNotStored_ListsTheLowerCaseSpellingFirstWithoutDuplicates()
    {
        // Arrange: POLISH is not stored; polish and Polish are
        var expected = WordLexicon.Lookup("polish").Concat(WordLexicon.Lookup("Polish")).Distinct().ToList();

        // Act
        var lookup = WordLexicon.Lookup("POLISH");

        // Assert: the order is that of the concatenation, and no analysis occurs twice
        Assert.Equal(expected, lookup);
        Assert.Equal(lookup.Length, lookup.Distinct().Count());
    }

    /// <summary>
    ///     The past and the present of <c>read</c> are analyses of one entry that differ in their features, and the form
    ///     <c>reads</c> is the present only.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_ReadForms_HaveThePastAndThePresentAnalysis()
    {
        var read = WordLexicon.Lookup("read").Where(s => s.Lemma == "read" && s.PartOfSpeech == PartOfSpeech.Verb).ToList();

        Assert.Contains(read, s => s.Features.Contains("Tense=Past", StringComparison.Ordinal));
        Assert.Contains(read, s => s.Features.Contains("Tense=Pres", StringComparison.Ordinal));
        Assert.Equal(2, read.Count);

        var reads = Assert.Single(WordLexicon.Lookup("reads"), s => s.PartOfSpeech == PartOfSpeech.Verb);
        Assert.Equal("Tense=Pres", reads.Features);
    }

    /// <summary>
    ///     A form of a lemma that the entry spells differently (<c>wicks</c>) is analyzed through the right entry.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_FormsWithTrainedLemmaDifferentFromEntry_UseFixedLemmas()
    {
        var wicks = WordLexicon.Lookup("wicks");
        Assert.Contains(wicks, s => s.Lemma == "wick" && s.PartOfSpeech == PartOfSpeech.Verb);
        Assert.DoesNotContain(wicks, s => s.Lemma == "wicked");

        Assert.Contains(WordLexicon.Lookup("re-signed"), s => s.Lemma == "re-sign");
        Assert.NotEmpty(WordLexicon.Lookup("axes"));
    }

    /// <summary>
    ///     Every form of a lemma is a lower-case surface form.
    /// </summary>
    [Fact]
    public void WordLexicon_FormsOf_Forms_AreLowerCase()
    {
        Assert.All(WordLexicon.FormsOf("record"), key => Assert.Equal(key.ToLowerInvariant(), key));
    }

    /// <summary>
    ///     The determiners, and the words that Moby lists with another code first, are in the lexicon with their real
    ///     parts of speech; a determiner has exactly one analysis.
    /// </summary>
    [Theory]
    [InlineData("the", PartOfSpeech.Determiner)]
    [InlineData("a", PartOfSpeech.Determiner)]
    [InlineData("an", PartOfSpeech.Determiner)]
    [InlineData("his", PartOfSpeech.Determiner)]
    [InlineData("albeit", PartOfSpeech.Conjunction)]
    [InlineData("alas", PartOfSpeech.Interjection)]
    public void WordLexicon_Lookup_ClosedClassWord_HasItsPartOfSpeechAlone(string word, PartOfSpeech expected)
    {
        Assert.True(WordLexicon.TryLookup(word, out var senses));

        Assert.Equal(expected, Assert.Single(senses).PartOfSpeech);
    }

    /// <summary>
    ///     Words with several parts of speech list them all, so and will among them.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_SoAndWill_ListTheirParts()
    {
        Assert.Contains(WordLexicon.Lookup("so"), s => s.PartOfSpeech == PartOfSpeech.Adverb);
        Assert.Contains(WordLexicon.Lookup("so"), s => s.PartOfSpeech == PartOfSpeech.Conjunction);
        Assert.Contains(WordLexicon.Lookup("will"), s => s.PartOfSpeech == PartOfSpeech.Verb);
        Assert.Contains(WordLexicon.Lookup("will"), s => s.PartOfSpeech == PartOfSpeech.Noun);
    }

    /// <summary>
    ///     The auxiliary verbs have an Auxiliary analysis after the analyses they had, and their forms do not gain it.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_Auxiliaries_HaveAnAuxiliaryAnalysis()
    {
        Assert.Equal(
            [PartOfSpeech.Verb, PartOfSpeech.Noun, PartOfSpeech.Auxiliary],
            WordLexicon.Lookup("will").Select(s => s.PartOfSpeech));
        Assert.Equal(PartOfSpeech.Auxiliary, WordLexicon.Lookup("is")[^1].PartOfSpeech);
        Assert.Equal(PartOfSpeech.Auxiliary, WordLexicon.Lookup("having")[^1].PartOfSpeech);
        Assert.DoesNotContain(WordLexicon.Lookup("wills"), s => s.PartOfSpeech == PartOfSpeech.Auxiliary);
        Assert.DoesNotContain(WordLexicon.Lookup("record"), s => s.PartOfSpeech == PartOfSpeech.Auxiliary);
    }

    /// <summary>
    ///     A form may have several lemmas: <c>drove</c> is a verb of <c>drive</c> and a noun of its own.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_FormWithSeveralLemmas_ListsEach()
    {
        var drove = WordLexicon.Lookup("drove");

        Assert.Equal("drive", drove[0].Lemma);
        Assert.Contains(drove, s => s.Lemma == "drove" && s.PartOfSpeech == PartOfSpeech.Noun);
    }

    /// <summary>
    ///     The exact spelling is found first, then the lower-case spelling; the curly apostrophe counts as a straight one.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_Case_FoldsToLowerCase()
    {
        var record = WordLexicon.Lookup("record");

        Assert.Equal(record, WordLexicon.Lookup("Record"));
        Assert.Equal(record, WordLexicon.Lookup("RECORD"));
        Assert.True(WordLexicon.Contains("Record"));
        Assert.Equal(WordLexicon.Lookup("o'clock"), WordLexicon.Lookup("o\u2019clock"));
    }

    /// <summary>
    ///     A possessive of a known word is analyzed through its stem with the possessive feature, for both apostrophes.
    /// </summary>
    [Theory]
    [InlineData("dog's", "Number=Sing+Poss=Yes")]
    [InlineData("dog\u2019s", "Number=Sing+Poss=Yes")]
    [InlineData("dogs'", "Number=Plur+Poss=Yes")]
    [InlineData("dogs\u2019", "Number=Plur+Poss=Yes")]
    public void WordLexicon_Lookup_PossessiveOfKnownWord_AddsPossessiveFeature(string word, string features)
    {
        var senses = WordLexicon.Lookup(word);

        Assert.NotEmpty(senses);
        Assert.All(senses, s => Assert.Contains("Poss=Yes", s.Features, StringComparison.Ordinal));
        Assert.Contains(senses, s => s.Lemma == "dog" && s.PartOfSpeech == PartOfSpeech.Noun && s.Features == features);
    }

    /// <summary>
    ///     A stored contraction wins over the possessive fallback, and a possessive of an unknown word stays unknown.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_StoredContractionOrUnknownStem_IsNotPossessive()
    {
        Assert.DoesNotContain(WordLexicon.Lookup("let's"), s => s.Features.Contains("Poss=Yes", StringComparison.Ordinal));
        Assert.Contains(WordLexicon.Lookup("don't"), s => s.Features.Contains("Contraction=Yes", StringComparison.Ordinal));
        Assert.Empty(WordLexicon.Lookup("zzqxjw's"));
        Assert.Empty(WordLexicon.Lookup("'s"));
        Assert.Empty(WordLexicon.Lookup("a'b's"));
    }

    /// <summary>
    ///     A word that the dictionary does not know stays absent.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_UnknownWord_IsAbsent()
    {
        Assert.False(WordLexicon.TryLookup("zzqxjw", out var senses));
        Assert.True(senses.IsDefault);
        Assert.Empty(WordLexicon.Lookup("zzqxjw"));
        Assert.Empty(WordLexicon.Lookup(string.Empty));
        Assert.Empty(WordLexicon.Lookup(new string('a', 400)));
        Assert.False(WordLexicon.Contains("zzqxjw"));
    }

    /// <summary>
    ///     A token without a letter (a number written with digits, a number with separators or punctuation) is never a
    ///     word; the answer is the same as for an unknown word, and the number words and words that contain a digit next
    ///     to letters keep their analyses.
    /// </summary>
    [Theory]
    [InlineData("3")]
    [InlineData("1999")]
    [InlineData("0")]
    [InlineData("24/7")]
    [InlineData(".22")]
    [InlineData("3.5")]
    [InlineData("-")]
    public void WordLexicon_Lookup_TokenWithoutLetter_IsAbsent(string token)
    {
        // Arrange: the token has no letter

        // Act
        var found = WordLexicon.TryLookup(token, out var senses);

        // Assert: TryLookup, Lookup and Contains agree with an unknown word
        Assert.False(found);
        Assert.True(senses.IsDefault);
        Assert.Empty(WordLexicon.Lookup(token));
        Assert.False(WordLexicon.Contains(token));
        Assert.Empty(WordLexicon.FormsOf(token));
    }

    /// <summary>
    ///     The number words and the dictionary entries that mix letters and digits are not affected by the rule for tokens
    ///     without a letter.
    /// </summary>
    [Theory]
    [InlineData("three")]
    [InlineData("Twenty")]
    [InlineData("3rd")]
    public void WordLexicon_Lookup_WordWithALetter_IsStillFound(string word)
    {
        // Arrange: the word has a letter, so the rule does not apply

        // Act
        var found = WordLexicon.TryLookup(word, out var senses);

        // Assert
        Assert.True(found);
        Assert.NotEmpty(senses);
    }

    /// <summary>
    ///     The image holds no spelling without a letter (the numerals of Open English WordNet are left out when it is
    ///     compiled), so the count of the lexicon and the senses of a numeral agree with <c>Lookup</c>.
    /// </summary>
    [Fact]
    public void WordLexicon_Image_HasNoSpellingWithoutALetter()
    {
        // Arrange
        var image = WordLexicon.Current;

        // Act
        var withoutLetter = Enumerable.Range(0, image.SpellingCount).Select(image.Spelling).Where(s => !CueModel.HasLetter(s)).Take(5).ToList();

        // Assert: no spelling, no entry and no sense of a numeral
        Assert.Empty(withoutLetter);
        Assert.Equal(image.SpellingCount, WordLexicon.Count);
        Assert.True(image.Find("24/7") < 0);
        Assert.Empty(WordLexicon.SensesOf("3", PartOfSpeech.Noun));
        Assert.Empty(WordLexicon.SensesOf("24/7", PartOfSpeech.Adjective));
        Assert.All(image.AllSenses(), s => Assert.True(CueModel.HasLetter(s.Lemma), s.SenseId));
    }

    /// <summary>
    ///     A null word throws.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_NullWord_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => WordLexicon.Lookup(null!));
        Assert.Throws<ArgumentNullException>(() => WordLexicon.FormsOf(null!));
    }

    /// <summary>
    ///     The forms of a lemma list every spelling that is an analysis of it.
    /// </summary>
    [Fact]
    public void WordLexicon_FormsOf_Lemma_ListsItsForms()
    {
        var forms = WordLexicon.FormsOf("record");

        Assert.Contains("record", forms);
        Assert.Contains("records", forms);
        Assert.Contains("recorded", forms);
        Assert.Contains("recorded", forms);
        Assert.Equal(forms.Order(StringComparer.Ordinal).Distinct(), forms);
        Assert.Empty(WordLexicon.FormsOf("zzqxjw"));
    }

    /// <summary>
    ///     The embedded image loads and holds many words, each with at least one analysis; the lookup returns equal arrays
    ///     every time.
    /// </summary>
    [Fact]
    public void WordLexicon_Count_EmbeddedImage_LoadsAndIsNotEmpty()
    {
        Assert.True(WordLexicon.Count > 300_000);
        Assert.All(KnownWords, word => Assert.NotEmpty(WordLexicon.Lookup(word)));
        Assert.Equal(WordLexicon.Lookup("record"), WordLexicon.Lookup("record"));
    }

    /// <summary>
    ///     Concurrent lookups give the same answers.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_ConcurrentAccess_ReturnsSameAnswers()
    {
        var results = Enumerable.Range(0, 32).AsParallel().Select(_ => WordLexicon.Lookup("record").Length).Distinct().ToList();

        Assert.Single(results);
    }

    /// <summary>
    ///     The senses of a lemma and part of speech are listed in the order of Open English WordNet: <c>bank</c> as a noun
    ///     starts with the sloping land, then the financial institution, as the position of the senses in the entry puts them.
    /// </summary>
    [Fact]
    public void WordLexicon_SensesOf_LemmaAndPartOfSpeech_ListsInOewnOrder()
    {
        var senses = WordLexicon.SensesOf("bank", PartOfSpeech.Noun);

        Assert.True(senses.Length >= 10);
        Assert.Equal("bank%1:17:01::", senses[0].SenseKey);
        Assert.Equal("bank%1:14:00::", senses[1].SenseKey);
        Assert.All(senses, s => Assert.Equal(("bank", PartOfSpeech.Noun), (s.Lemma, s.PartOfSpeech)));
        Assert.Equal(senses.Length, senses.Select(s => s.SenseId).Distinct().Count());
        Assert.All(senses, s => Assert.StartsWith("bank#", s.SenseId, StringComparison.Ordinal));
    }

    /// <summary>
    ///     A lemma or part of speech without senses has an empty list.
    /// </summary>
    [Fact]
    public void WordLexicon_SensesOf_UnknownLemma_IsEmpty()
    {
        Assert.Empty(WordLexicon.SensesOf("zzqxjw", PartOfSpeech.Noun));
        Assert.Empty(WordLexicon.SensesOf("bank", PartOfSpeech.Interjection));
        Assert.Empty(WordLexicon.SensesOf(string.Empty, PartOfSpeech.Noun));
        Assert.Empty(WordLexicon.SensesOf(new string('a', 400), PartOfSpeech.Noun));
    }

    /// <summary>
    ///     A null lemma throws.
    /// </summary>
    [Fact]
    public void WordLexicon_SensesOf_NullLemma_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => WordLexicon.SensesOf(null!, PartOfSpeech.Noun));
    }

    /// <summary>
    ///     A lemma and part of speech with one sense lists exactly that sense.
    /// </summary>
    [Fact]
    public void WordLexicon_SensesOf_SingleSenseLemma_ListsOneSense()
    {
        var the = Assert.Single(WordLexicon.SensesOf("the", PartOfSpeech.Determiner));

        Assert.Equal("the#determiner", the.SenseId);
        Assert.Null(the.GroupId);
    }

    /// <summary>
    ///     The catalog holds the sense keys of Open English WordNet: every sense of the dictionary that has a key can be
    ///     found by it, and the catalog holds at least the 129 thousand senses of the single-token entries.
    /// </summary>
    [Fact]
    public void WordLexicon_SensesOf_EverySingleTokenOewnSense_IsInTheCatalog()
    {
        var senses = WordLexicon.Current.AllSenses().ToList();
        var keyed = senses.Where(s => s.SenseKey is not null).ToList();

        Assert.True(keyed.Count >= 128_000, $"{keyed.Count} keyed senses");
        Assert.All(keyed, s => Assert.StartsWith(s.Lemma.ToLowerInvariant() + "%", s.SenseKey, StringComparison.Ordinal));
        Assert.Equal(keyed.Count, keyed.Select(s => s.SenseKey).Distinct().Count());
        Assert.All(senses, s => Assert.Contains(s, WordLexicon.SensesOf(s.Lemma, s.PartOfSpeech)));
    }

    /// <summary>
    ///     The project senses (the closed classes, the auxiliaries and the discourse senses) are in the catalog with no key.
    /// </summary>
    [Theory]
    [InlineData("the", PartOfSpeech.Determiner, "the#determiner")]
    [InlineData("or", PartOfSpeech.Conjunction, "or#conjunction")]
    [InlineData("will", PartOfSpeech.Auxiliary, "will#auxiliary")]
    [InlineData("well", PartOfSpeech.Interjection, "well#discourse-marker")]
    public void WordLexicon_SensesOf_ProjectSenses_AreInTheCatalog(string lemma, PartOfSpeech pos, string id)
    {
        var sense = Assert.Single(WordLexicon.SensesOf(lemma, pos), s => s.SenseId == id);

        Assert.Null(sense.SenseKey);
        Assert.False(WordLexicon.TryGetSenseByKey(lemma + "%1:00:00::", out _));
    }

    /// <summary>
    ///     A sense is found by its readable id, with its key, group and part of speech.
    /// </summary>
    [Fact]
    public void WordLexicon_TryGetSense_ById_FindsSense()
    {
        Assert.True(WordLexicon.TryGetSense("back#football-position", out var sense));

        Assert.Equal("back", sense.Lemma);
        Assert.Equal(PartOfSpeech.Noun, sense.PartOfSpeech);
        Assert.Equal("back%1:04:00::", sense.SenseKey);
        Assert.Equal("back#group:football", sense.GroupId);
        Assert.Contains(sense, WordLexicon.SensesOf("back", PartOfSpeech.Noun));
    }

    /// <summary>
    ///     An unknown id, an empty id and an id that is too long are not found; a null id throws.
    /// </summary>
    [Fact]
    public void WordLexicon_TryGetSense_UnknownId_IsFalse()
    {
        Assert.False(WordLexicon.TryGetSense("back#no-such-sense", out var sense));
        Assert.Null(sense);
        Assert.False(WordLexicon.TryGetSense(string.Empty, out _));
        Assert.False(WordLexicon.TryGetSense(new string('a', 400), out _));
        Assert.Throws<ArgumentNullException>(() => WordLexicon.TryGetSense(null!, out _));
    }

    /// <summary>
    ///     The key leads to the same sense as the id.
    /// </summary>
    [Fact]
    public void WordLexicon_TryGetSenseByKey_FindsTheSameSenseAsItsId()
    {
        Assert.True(WordLexicon.TryGetSense("back#football-position", out var byId));

        Assert.True(WordLexicon.TryGetSenseByKey("back%1:04:00::", out var byKey));
        Assert.Equal(byId, byKey);
    }

    /// <summary>
    ///     An unknown key, an empty key and a key that is too long are not found; a null key throws.
    /// </summary>
    [Fact]
    public void WordLexicon_TryGetSenseByKey_UnknownKey_IsFalse()
    {
        Assert.False(WordLexicon.TryGetSenseByKey("back%9:99:99::", out var sense));
        Assert.Null(sense);
        Assert.False(WordLexicon.TryGetSenseByKey(string.Empty, out _));
        Assert.False(WordLexicon.TryGetSenseByKey("zzqxjw%1:00:00::", out _));
        Assert.Throws<ArgumentNullException>(() => WordLexicon.TryGetSenseByKey(null!, out _));
    }

    /// <summary>
    ///     A project sense has no key, so no key finds it, and the senses found by their keys all have one.
    /// </summary>
    [Fact]
    public void WordLexicon_TryGetSenseByKey_ProjectSense_IsNeverFound()
    {
        var project = WordLexicon.Current.AllSenses().Where(s => s.SenseKey is null).ToList();

        Assert.True(project.Count >= 900);
        Assert.All(project, s => Assert.False(WordLexicon.TryGetSenseByKey(s.Lemma + "%", out _)));
        Assert.All(
            WordLexicon.SensesOf("back", PartOfSpeech.Noun),
            s => Assert.True(WordLexicon.TryGetSenseByKey(s.SenseKey!, out var found) && found.SenseId == s.SenseId));
    }

    /// <summary>
    ///     A group is found by its id and lists the senses it holds in inventory order.
    /// </summary>
    [Fact]
    public void WordLexicon_TryGetGroup_ById_ListsMemberSenses()
    {
        Assert.True(WordLexicon.TryGetGroup("back#group:football", out var group));

        Assert.Equal(("back", PartOfSpeech.Noun), (group.Lemma, group.PartOfSpeech));
        Assert.Equal(["back#football-back", "back#football-position"], group.SenseIds.Order(StringComparer.Ordinal));
        Assert.All(group.SenseIds, id => Assert.True(WordLexicon.TryGetSense(id, out var sense) && sense.GroupId == "back#group:football"));
    }

    /// <summary>
    ///     An unknown group id is not found; a null id throws.
    /// </summary>
    [Fact]
    public void WordLexicon_TryGetGroup_UnknownId_IsFalse()
    {
        Assert.False(WordLexicon.TryGetGroup("back#group:no-such-group", out var group));
        Assert.Null(group);
        Assert.False(WordLexicon.TryGetGroup(string.Empty, out _));
        Assert.Throws<ArgumentNullException>(() => WordLexicon.TryGetGroup(null!, out _));
    }

    /// <summary>
    ///     A sense is in at most one group: the group a sense names lists the sense, and the groups together list exactly
    ///     the senses that name a group.
    /// </summary>
    [Fact]
    public void WordLexicon_Groups_EverySense_BelongsToAtMostOneGroup()
    {
        var senses = WordLexicon.Current.AllSenses().ToList();
        var groups = WordLexicon.Current.AllGroups().ToList();

        var listed = groups.SelectMany(g => g.SenseIds).ToList();
        Assert.Equal(listed.Count, listed.Distinct().Count());
        Assert.Equal(senses.Count(s => s.GroupId is not null), listed.Count);
        Assert.All(senses.Where(s => s.GroupId is not null), s => Assert.Contains(s.SenseId, groups.Single(g => g.GroupId == s.GroupId).SenseIds));
    }

    /// <summary>
    ///     A group has the lemma and the part of speech of all its senses.
    /// </summary>
    [Fact]
    public void WordLexicon_Groups_EveryGroup_HasOneLemmaAndOnePartOfSpeech()
    {
        foreach (var group in WordLexicon.Current.AllGroups())
        {
            foreach (var id in group.SenseIds)
            {
                Assert.True(WordLexicon.TryGetSense(id, out var sense));
                Assert.Equal((group.Lemma, group.PartOfSpeech), (sense.Lemma, sense.PartOfSpeech));
            }
        }
    }

    /// <summary>
    ///     A group exists only where it was curated: it has at least two senses and never all the senses of its lemma and
    ///     part of speech.
    /// </summary>
    [Fact]
    public void WordLexicon_Groups_EveryGroup_HasAtLeastTwoSensesAndNotAllOfThem()
    {
        var groups = WordLexicon.Current.AllGroups().ToList();

        Assert.NotEmpty(groups);
        Assert.All(groups, g => Assert.True(g.SenseIds.Length >= 2, g.GroupId));
        Assert.All(groups, g => Assert.True(g.SenseIds.Length < WordLexicon.SensesOf(g.Lemma, g.PartOfSpeech).Length, g.GroupId));
    }

    /// <summary>
    ///     A group id is the lemma, the group marker and a slug, so it can never equal a sense id.
    /// </summary>
    [Fact]
    public void WordLexicon_Groups_EveryGroupId_HasGroupMarkerAndNeverEqualsASenseId()
    {
        var senseIds = WordLexicon.Current.AllSenses().Select(s => s.SenseId).ToHashSet(StringComparer.Ordinal);
        var groups = WordLexicon.Current.AllGroups().ToList();

        Assert.All(groups, g => Assert.StartsWith(g.Lemma + "#group:", g.GroupId, StringComparison.Ordinal));
        Assert.All(groups, g => Assert.DoesNotContain(g.GroupId, senseIds));
        Assert.DoesNotContain(senseIds, id => id.Contains("#group:", StringComparison.Ordinal));
    }

    /// <summary>
    ///     The version of the inventory is 16 lower-case hexadecimal digits and does not change between calls.
    /// </summary>
    [Fact]
    public void WordLexicon_Version_IsStableLowerCaseHex()
    {
        var version = WordLexicon.Version;

        Assert.Equal(16, version.Length);
        Assert.All(version, c => Assert.True(char.IsAsciiHexDigitLower(c), version));
        Assert.Equal(version, WordLexicon.Version);
    }

    /// <summary>
    ///     The embedded image opens from its own resource stream.
    /// </summary>
    [Fact]
    public void WordLexicon_OpenImage_EmbeddedResource_Opens()
    {
        // Arrange
        using var stream = typeof(WordLexicon).Assembly.GetManifestResourceStream("DemaConsulting.WordSense.wordsense.img");

        // Act
        var image = WordLexicon.OpenImage(stream);

        // Assert
        Assert.True(image.SpellingCount > 0);
    }

    /// <summary>
    ///     A missing resource is reported as <see cref="ModelIntegrityException"/> that carries the original
    ///     <see cref="InvalidDataException"/>.
    /// </summary>
    [Fact]
    public void WordLexicon_OpenImage_MissingResource_ThrowsModelIntegrity()
    {
        // Arrange / Act
        var failure = Assert.Throws<ModelIntegrityException>(() => WordLexicon.OpenImage(null));

        // Assert
        Assert.IsType<InvalidDataException>(failure.InnerException);
        Assert.Contains("missing", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A corrupt image (bytes that are not an image, and a real image with a flipped byte) is reported as
    ///     <see cref="ModelIntegrityException"/> that carries the original <see cref="InvalidDataException"/>.
    /// </summary>
    /// <param name="flipRealImage">Whether to flip one byte of the real image, instead of using bytes that are not an image.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WordLexicon_OpenImage_CorruptResource_ThrowsModelIntegrity(bool flipRealImage)
    {
        // Arrange: a stream that is not an image, or the real image with one byte flipped
        byte[] bytes;
        if (flipRealImage)
        {
            using var stream = typeof(WordLexicon).Assembly.GetManifestResourceStream("DemaConsulting.WordSense.wordsense.img")!;
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            bytes[^1] ^= 0xFF;
        }
        else
        {
            bytes = "this is not a word image"u8.ToArray();
        }

        using var corrupt = new MemoryStream(bytes);

        // Act
        var failure = Assert.Throws<ModelIntegrityException>(() => WordLexicon.OpenImage(corrupt));

        // Assert: the original exception is kept
        Assert.IsType<InvalidDataException>(failure.InnerException);
    }

    /// <summary>
    ///     A damaged sense catalog found while reading it is reported as <see cref="ModelIntegrityException"/> that
    ///     carries the original exception; other exceptions are not changed.
    /// </summary>
    [Fact]
    public void WordLexicon_ReadCatalog_DamagedCatalog_ThrowsModelIntegrity()
    {
        // Arrange: reads that fail the way a damaged catalog does, and the way a caller mistake does
        var damaged = new InvalidDataException("The word image has an invalid catalog: a sense table has the wrong length.");

        // Act
        var failure = Assert.Throws<ModelIntegrityException>(() => WordLexicon.ReadCatalog<int>(_ => throw damaged));

        // Assert
        Assert.Same(damaged, failure.InnerException);
        Assert.Throws<InvalidOperationException>(() => WordLexicon.ReadCatalog<int>(_ => throw new InvalidOperationException("A caller mistake.")));
        Assert.Equal(7, WordLexicon.ReadCatalog(_ => 7));
    }

    /// <summary>
    ///     The sense catalog of the real image reads through the integrity mapping without changing its answers.
    /// </summary>
    [Fact]
    public void WordLexicon_SensesOf_RealImage_ReturnsSenses()
    {
        // Arrange / Act
        var senses = WordLexicon.SensesOf("bank", PartOfSpeech.Noun);

        // Assert
        Assert.NotEmpty(senses);
        Assert.Contains(senses, s => s.SenseKey is not null);
        Assert.True(WordLexicon.TryGetSense(senses[0].SenseId, out var found));
        Assert.Equal(senses[0], found);
    }
}
