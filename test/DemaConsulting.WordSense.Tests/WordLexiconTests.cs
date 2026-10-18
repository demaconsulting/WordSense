namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="WordLexicon"/>.
/// </summary>
public class WordLexiconTests
{
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
    ///     A form answers only with the senses it can have: <c>bowed</c> only bends, <c>bows</c> names all three senses of
    ///     <c>bow</c>, and every sense has an id of the trained lemma.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_FormOfHeteronym_HasOnlyItsSenses()
    {
        var bowed = Assert.Single(WordLexicon.Lookup("bowed"));
        Assert.Equal(PartOfSpeech.Verb, bowed.PartOfSpeech);
        Assert.Equal("bend", WordLexicon.MeaningOf(bowed));
        Assert.Equal("bow", WordLexicon.TrainedLemmaOf(bowed));
        Assert.Equal("Tense=Past", bowed.Features);

        var bows = WordLexicon.Lookup("bows");
        Assert.Equal(
            [(PartOfSpeech.Noun, "bend"), (PartOfSpeech.Verb, "bend"), (PartOfSpeech.Noun, "weapon")],
            bows.Select(b => (b.PartOfSpeech, WordLexicon.MeaningOf(b))));
        Assert.Equal(3, bows.Select(b => b.SenseId).Distinct().Count());
        Assert.All(bows, b => Assert.StartsWith("bow#", b.SenseId, StringComparison.Ordinal));
        Assert.Equal(3, WordLexicon.Lookup("bow").Count(b => b.SenseId is not null));
    }

    /// <summary>
    ///     The present-tense <c>reads</c> has only the present sense, while <c>read</c> keeps both its past and present
    ///     senses.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_ReadForms_SplitBySense()
    {
        var reads = Assert.Single(WordLexicon.Lookup("reads"));
        Assert.Equal("present", WordLexicon.MeaningOf(reads));

        var read = WordLexicon.Lookup("read").Select(WordLexicon.MeaningOf).Where(m => m is not null);
        Assert.Equal(["past", "present"], read.Order(StringComparer.Ordinal));
    }

    /// <summary>
    ///     The trained senses of a restricted form come first; <c>lives</c> starts with the verb sense and never offers
    ///     the adverb or adjective sense, <c>wounds</c> only the injury.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_RestrictedForm_NeverOffersExcludedSense()
    {
        var lives = WordLexicon.Lookup("lives");
        Assert.Equal(PartOfSpeech.Verb, lives[0].PartOfSpeech);
        Assert.Equal("dwell", WordLexicon.MeaningOf(lives[0]));
        Assert.DoesNotContain(lives, s => s.PartOfSpeech is PartOfSpeech.Adjective or PartOfSpeech.Adverb);

        var wounds = Assert.Single(WordLexicon.Lookup("wounds"));
        Assert.Equal(PartOfSpeech.Noun, wounds.PartOfSpeech);
        Assert.Equal("injury", WordLexicon.MeaningOf(wounds));
    }

    /// <summary>
    ///     A form of a trained lemma that the entry lemma does not spell the same is still keyed by the trained lemma
    ///     (<c>axes</c> is a form of the entry <c>ax</c>), and the 19 wrong-lemma forms point at the right entry.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_FormsWithTrainedLemmaDifferentFromEntry_UseBridgeAndFixedLemmas()
    {
        var wicks = WordLexicon.Lookup("wicks");
        Assert.Contains(wicks, s => s.Lemma == "wick" && s.PartOfSpeech == PartOfSpeech.Verb);
        Assert.DoesNotContain(wicks, s => s.Lemma == "wicked");

        Assert.Contains(WordLexicon.Lookup("re-signed"), s => s.Lemma == "re-sign");
        Assert.NotEmpty(WordLexicon.Lookup("axes"));
    }

    /// <summary>
    ///     Every key is a lower-case surface form.
    /// </summary>
    [Fact]
    public void WordLexicon_View_Keys_AreLowerCase()
    {
        Assert.All(WordLexicon.View.Keys, key => Assert.Equal(key.ToLowerInvariant(), key));
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
    public void WordLexicon_Lookup_Auxiliaries_HaveTrailingAuxiliaryAnalysis()
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
    ///     A word that is not in the lexicon has no analysis.
    /// </summary>
    [Fact]
    public void WordLexicon_Lookup_UnknownWord_IsAbsent()
    {
        Assert.False(WordLexicon.TryLookup("zzqxjw", out var senses));
        Assert.True(senses.IsDefault);
        Assert.Empty(WordLexicon.Lookup("zzqxjw"));
        Assert.Empty(WordLexicon.Lookup(string.Empty));
        Assert.Empty(WordLexicon.Lookup(new string('a', 400)));
        Assert.False(WordLexicon.View.ContainsKey("zzqxjw"));
        Assert.Throws<KeyNotFoundException>(() => WordLexicon.View["zzqxjw"]);
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
        Assert.Equal(WordLexicon.Count, WordLexicon.View.Count);
        Assert.All(WordLexicon.View.Take(1000), pair => Assert.NotEmpty(pair.Value));
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
}
