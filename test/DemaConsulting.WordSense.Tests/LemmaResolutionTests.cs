namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for the lemma resolution of <see cref="WordSenseTable"/> and the gated <see cref="LemmaMorphology"/>.
/// </summary>
public class LemmaResolutionTests
{
    /// <summary>
    ///     Builds a table with trained lemmas bat and lead, and the given dictionary words.
    /// </summary>
    /// <param name="rules">The enabled morphology rules.</param>
    /// <param name="words">The dictionary words, in any order.</param>
    /// <returns>The table.</returns>
    private static WordSenseTable Table(LemmaMorphologyRules rules, params (string Word, PartOfSpeech[] Parts)[] words)
    {
        var trained = WordSenseTableTests.CreateSmallTable();
        var all = trained.ContextLemmas
            .Concat(words.Select(w => WordSenseTableTests.Word(w.Word, w.Parts)))
            .OrderBy(l => l.Lemma, StringComparer.Ordinal)
            .ToArray();
        return new WordSenseTable(all, trained.Forms, rules);
    }

    private static readonly PartOfSpeech[] N = [PartOfSpeech.Noun];
    private static readonly PartOfSpeech[] V = [PartOfSpeech.Verb];
    private static readonly PartOfSpeech[] NV = [PartOfSpeech.Noun, PartOfSpeech.Verb];
    private static readonly PartOfSpeech[] Adj = [PartOfSpeech.Adjective];
    private static readonly PartOfSpeech[] AdjN = [PartOfSpeech.Noun, PartOfSpeech.Adjective];

    /// <summary>
    ///     A word found in the form map resolves to its trained lemma, and an exact trained or dictionary lemma resolves
    ///     to itself.
    /// </summary>
    [Fact]
    public void TryResolve_FormExactAndDictionary_Resolve()
    {
        var table = Table(LemmaMorphologyRules.None, ("cat", N));

        Assert.True(table.TryResolve("led", true, out var form));
        Assert.Equal("lead", form.Entry.Lemma);
        Assert.False(form.IsNarrowed);
        Assert.True(table.TryResolve("BATS", true, out var upper));
        Assert.Equal("bat", upper.Entry.Lemma);
        Assert.True(table.TryResolve("cat", true, out var cat));
        Assert.False(cat.Entry.IsContextual);
        Assert.False(table.TryResolve("cat", false, out _));
        Assert.True(table.TryResolve("bat", false, out var bat));
        Assert.True(bat.Entry.IsContextual);
        Assert.False(table.TryResolve("cats", true, out _));
        Assert.False(table.TryResolve(string.Empty, true, out _));
        Assert.False(table.TryResolve("unknown", true, out _));
    }

    /// <summary>
    ///     A possessive resolves only to an entry with trained weights, and the typographic apostrophe is accepted.
    /// </summary>
    [Fact]
    public void TryResolve_Possessive_OnlyForWeightedEntries()
    {
        var table = Table(LemmaMorphologyRules.None, ("cat", N));

        Assert.True(table.TryResolve("bat's", true, out var a));
        Assert.Equal("bat", a.Entry.Lemma);
        Assert.True(table.TryResolve("bat\u2019s", false, out var b));
        Assert.Equal("bat", b.Entry.Lemma);
        Assert.False(table.TryResolve("cat's", true, out _));
    }

    /// <summary>
    ///     Plural resolves for noun-only and verb-only bases, in the plain, -ies and -es spellings, but not otherwise.
    /// </summary>
    [Fact]
    public void TryResolve_Plural_GatedOnBase()
    {
        var table = Table(
            LemmaMorphologyRules.Plural,
            ("cat", N), ("fly", N), ("box", N), ("jump", V), ("walk", NV), ("bus", N), ("glass", N));

        Assert.True(table.TryResolve("cats", true, out var cats));
        Assert.Equal(("cat", PartOfSpeech.Noun), (cats.Entry.Lemma, cats.Entry.Senses[cats.SenseIndex].Pos));
        Assert.True(table.TryResolve("flies", true, out var flies));
        Assert.Equal("fly", flies.Entry.Lemma);
        Assert.True(table.TryResolve("boxes", true, out var boxes));
        Assert.Equal("box", boxes.Entry.Lemma);
        Assert.True(table.TryResolve("jumps", true, out var jumps));
        Assert.Equal(PartOfSpeech.Verb, jumps.Entry.Senses[jumps.SenseIndex].Pos);

        // A base that is both noun and verb is ambiguous, and -ss/-us/-is words are never plurals
        Assert.False(table.TryResolve("walks", true, out _));
        Assert.False(table.TryResolve("glasss", true, out _));
        Assert.False(table.TryResolve("buss", true, out _));
        Assert.False(table.TryResolve("dogs", true, out _));

        // A trained lemma is never a morphology base: 'leads' does not resolve at all
        Assert.False(table.TryResolve("leads", true, out _));

        // A plural yields a one-sense entry, and words of three letters or fewer are never derived
        Assert.Single(cats.Entry.Senses);
        var short3 = Table(LemmaMorphologyRules.Plural, ("ab", N), ("abc", N), ("xyz", N));
        Assert.False(short3.TryResolve("abs", true, out _));
        Assert.True(short3.TryResolve("xyzs", true, out _));
    }

    /// <summary>
    ///     Past tense resolves for bases that can be verbs, including doubled consonants and -ied.
    /// </summary>
    [Fact]
    public void TryResolve_PastTense_GatedOnVerbBase()
    {
        var table = Table(
            LemmaMorphologyRules.PastTense,
            ("jump", V), ("love", V), ("stop", V), ("carry", V), ("walk", NV), ("blue", Adj));

        foreach (var (word, lemma) in new[] { ("jumped", "jump"), ("loved", "love"), ("stopped", "stop"), ("carried", "carry"), ("walked", "walk") })
        {
            Assert.True(table.TryResolve(word, true, out var r), word);
            Assert.Equal(lemma, r.Entry.Lemma);
            Assert.Equal(PartOfSpeech.Verb, r.Entry.Senses[r.SenseIndex].Pos);
        }

        Assert.False(table.TryResolve("blued", true, out _));
        Assert.False(table.TryResolve("jumping", true, out _));
    }

    /// <summary>
    ///     Adverb resolves for adjective-only bases, with the -ily spelling, and yields an adverb sense.
    /// </summary>
    [Fact]
    public void TryResolve_Adverb_GatedOnAdjectiveOnlyBase()
    {
        var table = Table(LemmaMorphologyRules.Adverb, ("quick", Adj), ("happy", Adj), ("early", AdjN), ("cat", N));

        Assert.True(table.TryResolve("quickly", true, out var quickly));
        Assert.Equal(PartOfSpeech.Adverb, quickly.Entry.Senses[quickly.SenseIndex].Pos);
        Assert.True(table.TryResolve("happily", true, out var happily));
        Assert.Equal(PartOfSpeech.Adverb, happily.Entry.Senses[happily.SenseIndex].Pos);
        Assert.False(table.TryResolve("earlyly", true, out _));
        Assert.False(table.TryResolve("catly", true, out _));
    }

    /// <summary>
    ///     A disabled rule does not apply, a word the table contains is never overridden, and no dictionary use
    ///     disables morphology.
    /// </summary>
    [Fact]
    public void TryResolve_DisabledRuleOrNoDictionary_DoesNotApply()
    {
        var plural = Table(LemmaMorphologyRules.Plural, ("cat", N));
        var none = Table(LemmaMorphologyRules.None, ("cat", N));
        var present = Table(LemmaMorphologyRules.Default, ("cat", N), ("cats", NV));

        Assert.True(plural.TryResolve("cats", true, out _));
        Assert.False(plural.TryResolve("cats", false, out _));
        Assert.False(plural.TryResolve("catted", true, out _));
        Assert.False(none.TryResolve("cats", true, out _));
        Assert.True(present.TryResolve("cats", true, out var cats));
        Assert.Equal("cats", cats.Entry.Lemma);
        Assert.Equal(2, cats.Entry.Senses.Count);
    }

    /// <summary>
    ///     Dictionary words stay out of the context-lemma list but are counted and enumerated.
    /// </summary>
    [Fact]
    public void Table_CountLemmasAndContextLemmas_Differ()
    {
        var table = Table(LemmaMorphologyRules.None, ("cat", N), ("dog", N));

        Assert.Equal(4, table.Count);
        Assert.Equal(2, table.ContextLemmas.Count);
        Assert.Equal(["bat", "cat", "dog", "lead"], table.Lemmas.Select(l => l.Lemma));
    }
}
