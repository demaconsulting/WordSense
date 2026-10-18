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
    public void WordLexicon_Words_KnownWord_ReturnsItsSenses()
    {
        Assert.True(WordLexicon.Words.TryGetValue("record", out var senses));

        var parts = senses.Select(s => s.PartOfSpeech).ToHashSet();
        Assert.Contains(PartOfSpeech.Noun, parts);
        Assert.Contains(PartOfSpeech.Verb, parts);
        Assert.All(senses, s => Assert.Equal("record", s.Lemma));
    }

    /// <summary>
    ///     The inflected forms materialized at build time for every dictionary word resolve to the lemma of the base form.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_InflectedForm_ReturnsSensesOfBase()
    {
        Assert.True(WordLexicon.Words.TryGetValue("records", out var senses));

        Assert.Contains(senses, s => s.Lemma == "record");
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
    public void WordLexicon_Words_InflectedFormOfAnyDictionaryWord_ResolvesToLemma(string form, string lemma, PartOfSpeech pos)
    {
        Assert.True(WordLexicon.Words.TryGetValue(form, out var senses));

        Assert.Contains(senses, s => s.Lemma == lemma && s.PartOfSpeech == pos);
    }

    /// <summary>
    ///     An irregular form that the dictionary already lists keeps its dictionary entry (the dictionary wins), and is known.
    /// </summary>
    [Theory]
    [InlineData("went")]
    [InlineData("children")]
    [InlineData("happier")]
    public void WordLexicon_Words_IrregularFormInDictionary_IsKnown(string form)
    {
        Assert.True(WordLexicon.Words.ContainsKey(form));
    }

    /// <summary>
    ///     Wrong regular forms of irregular words are not generated.
    /// </summary>
    [Theory]
    [InlineData("goed")]
    [InlineData("childs")]
    [InlineData("knowed")]
    public void WordLexicon_Words_WrongRegularFormOfIrregularWord_IsAbsent(string form)
    {
        Assert.False(WordLexicon.Words.ContainsKey(form));
    }

    /// <summary>
    ///     A form that the forms file lists answers only with the senses its labels name: <c>bowed</c> only bends,
    ///     <c>bows</c> names all three senses of <c>bow</c>.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_ListedFormOfHeteronym_HasOnlyItsLabeledSenses()
    {
        var bowed = Assert.Single(WordLexicon.Words["bowed"]);
        Assert.Equal(new WordDetails("bow", PartOfSpeech.Verb, "bend", "bow"), bowed);

        var bows = WordLexicon.Words["bows"];
        Assert.Equal(
            [
                new WordDetails("bow", PartOfSpeech.Noun, "bend", "bow"),
                new WordDetails("bow", PartOfSpeech.Verb, "bend", "bow"),
                new WordDetails("bow", PartOfSpeech.Noun, "weapon", "boh"),
            ],
            bows);
        Assert.Equal(3, WordLexicon.Words["bow"].Length);
    }

    /// <summary>
    ///     The present-tense <c>reads</c> has only the present sense, while <c>read</c> keeps both its past and present
    ///     senses.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_ReadForms_SplitByLabels()
    {
        var reads = Assert.Single(WordLexicon.Words["reads"]);
        Assert.Equal(new WordDetails("read", PartOfSpeech.Verb, "present", "reed"), reads);

        var read = WordLexicon.Words["read"].Where(s => s.Meaning is not null);
        Assert.Equal(["past", "present"], read.Select(s => s.Meaning).Order(StringComparer.Ordinal));
    }

    /// <summary>
    ///     The trained senses of a restricted form come first; <c>lives</c> starts with the verb sense and never offers
    ///     the adverb or adjective sense, <c>wounds</c> only the injury.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_RestrictedForm_NeverOffersExcludedSense()
    {
        var lives = WordLexicon.Words["lives"];
        Assert.Equal(new WordDetails("live", PartOfSpeech.Verb, "dwell", "liv"), lives[0]);
        Assert.DoesNotContain(lives, s => s.PartOfSpeech is PartOfSpeech.Adjective or PartOfSpeech.Adverb);
        Assert.DoesNotContain(lives, s => s.Respelling == "lyve");

        var wounds = Assert.Single(WordLexicon.Words["wounds"]);
        Assert.Equal(new WordDetails("wound", PartOfSpeech.Noun, "injury", "woond"), wounds);
    }

    /// <summary>
    ///     Every key is a lower-case surface form.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_Keys_AreLowerCase()
    {
        Assert.All(WordLexicon.Words.Keys, key => Assert.Equal(key.ToLowerInvariant(), key));
    }

    /// <summary>
    ///     The determiners, and the words that Moby lists with another code first, are in the lexicon with their real
    ///     parts of speech; a determiner has exactly one sense.
    /// </summary>
    [Theory]
    [InlineData("the", PartOfSpeech.Determiner)]
    [InlineData("a", PartOfSpeech.Determiner)]
    [InlineData("an", PartOfSpeech.Determiner)]
    [InlineData("his", PartOfSpeech.Determiner)]
    [InlineData("albeit", PartOfSpeech.Conjunction)]
    [InlineData("alas", PartOfSpeech.Interjection)]
    public void WordLexicon_Words_ClosedClassWord_HasItsPartOfSpeechAlone(string word, PartOfSpeech expected)
    {
        Assert.True(WordLexicon.Words.TryGetValue(word, out var senses));

        Assert.Equal(expected, Assert.Single(senses).PartOfSpeech);
    }

    /// <summary>
    ///     Words with several parts of speech list them all, so and will among them.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_SoAndWill_ListTheirParts()
    {
        Assert.Contains(WordLexicon.Words["so"], s => s.PartOfSpeech == PartOfSpeech.Adverb);
        Assert.Contains(WordLexicon.Words["so"], s => s.PartOfSpeech == PartOfSpeech.Conjunction);
        Assert.Contains(WordLexicon.Words["will"], s => s.PartOfSpeech == PartOfSpeech.Verb);
        Assert.Contains(WordLexicon.Words["will"], s => s.PartOfSpeech == PartOfSpeech.Noun);
    }

    /// <summary>
    ///     The auxiliary verbs have an Auxiliary sense after the senses they had, and their forms do not gain it.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_Auxiliaries_HaveTrailingAuxiliarySense()
    {
        Assert.Equal(
            [PartOfSpeech.Verb, PartOfSpeech.Noun, PartOfSpeech.Auxiliary],
            WordLexicon.Words["will"].Select(s => s.PartOfSpeech));
        Assert.Equal(PartOfSpeech.Auxiliary, WordLexicon.Words["is"][^1].PartOfSpeech);
        Assert.Equal("is", WordLexicon.Words["is"][^1].Lemma);
        Assert.Equal(PartOfSpeech.Auxiliary, WordLexicon.Words["having"][^1].PartOfSpeech);
        Assert.DoesNotContain(WordLexicon.Words["wills"], s => s.PartOfSpeech == PartOfSpeech.Auxiliary);
        Assert.DoesNotContain(WordLexicon.Words["record"], s => s.PartOfSpeech == PartOfSpeech.Auxiliary);
    }

    /// <summary>
    ///     Every sense of a word has the lemma of its first sense, and so the weights of one lemma cover the word, or it
    ///     is the word's own extra: a form may have several lemmas, so a sense of a part of speech that the labels of a
    ///     form do not cover has the form itself as lemma and no meaning and no respelling (<c>drove</c> is a verb of
    ///     <c>drive</c> and a noun of its own).
    /// </summary>
    [Fact]
    public void WordLexicon_Words_EverySense_SharesTheFirstLemmaOrIsTheOwnExtra()
    {
        Assert.All(
            WordLexicon.Words,
            pair => Assert.All(
                pair.Value,
                s => Assert.True(
                    s.Lemma == pair.Value[0].Lemma || (s.Lemma == pair.Key && s.Meaning is null && s.Respelling is null),
                    $"{pair.Key}: {s}")));

        var drove = WordLexicon.Words["drove"];
        Assert.Equal("drive", drove[0].Lemma);
        Assert.Equal(new WordDetails("drove", PartOfSpeech.Noun, null, null), drove[^1]);
    }

    /// <summary>
    ///     The published sense lists cannot be mutated: not even through the list interfaces, and a lookup returns the
    ///     very same list every time (no copy).
    /// </summary>
    [Fact]
    public void WordLexicon_Words_PublishedValues_CannotBeMutated()
    {
        var senses = WordLexicon.Words["record"];
        IList<WordDetails> list = senses;
        var first = senses[0];

        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = new WordDetails("x", PartOfSpeech.Noun, null, null));
        Assert.Throws<NotSupportedException>(() => list.Add(first));
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Equal(first, WordLexicon.Words["record"][0]);
        Assert.True(senses == WordLexicon.Words["record"]);
        Assert.All(WordLexicon.Words.Take(1000), pair => Assert.False(pair.Value.IsDefault));
    }

    /// <summary>
    ///     A word that is not in the lexicon is absent, and so is a key that is not lower case.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_UnknownWord_IsAbsent()
    {
        Assert.False(WordLexicon.Words.ContainsKey("zzqxjw"));
        Assert.False(WordLexicon.Words.ContainsKey("Record"));
    }

    /// <summary>
    ///     The embedded blob loads and holds many words, each with at least one sense.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_EmbeddedBlob_LoadsAndIsNotEmpty()
    {
        var words = WordLexicon.Words;

        Assert.True(words.Count > 100_000);
        Assert.All(words.Take(1000), pair => Assert.NotEmpty(pair.Value));
    }

    /// <summary>
    ///     Concurrent access returns the same instance everywhere.
    /// </summary>
    [Fact]
    public void WordLexicon_Words_ConcurrentAccess_ReturnsSameInstance()
    {
        var instances = Enumerable.Range(0, 16).AsParallel().Select(_ => WordLexicon.Words).Distinct().ToList();

        Assert.Single(instances);
    }
}
