using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the form table produced by <see cref="FormGenerator"/>: the shipped rows keep the corroboration policy.
/// </summary>
public sealed class FormGeneratorDataTests
{
    private static readonly Lazy<List<string[]>> Forms = new(Load);

    private static string RepositoryData()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "data", "schema.yaml")))
        {
            dir = dir.Parent;
        }

        return dir is null ? throw new InvalidOperationException("The data folder was not found.") : Path.Join(dir.FullName, "data");
    }

    private static List<string[]> Load()
    {
        var data = RepositoryData();
        var schema = DataSchema.Load(Path.Join(data, "schema.yaml"));
        return DataTables.Read(data, schema.Table("form")).Select(r => r.ToArray()).ToList();
    }

    private static IEnumerable<string[]> Rows(string spelling) => Forms.Value.Where(r => r[0] == spelling);

    /// <summary>
    ///     Every row names a provenance from the closed list.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Data_Provenance_IsKnown()
    {
        string[] known = ["carried", "contraction", "hand", "irregular_list", "moby_reverse", "oewn_form", "rule", "rule_variant", "llm"];
        var unknown = Forms.Value.Select(r => r[^1]).Distinct().Where(p => !known.Contains(p)).ToList();
        Assert.Empty(unknown);
    }

    /// <summary>
    ///     Regular inflections of real lemmas stay resolved.
    /// </summary>
    /// <param name="spelling">The inflected spelling.</param>
    [Theory]
    [InlineData("tossed")]
    [InlineData("brothers")]
    [InlineData("spilled")]
    [InlineData("others")]
    public void WordSenseBuilder_FormGenerator_Data_RegularInflection_IsResolved(string spelling)
    {
        Assert.NotEmpty(Rows(spelling));
    }

    /// <summary>
    ///     An -ing form leads with its verb analysis rather than a plural noun.
    /// </summary>
    /// <param name="spelling">The -ing spelling.</param>
    [Theory]
    [InlineData("walking")]
    [InlineData("smoking")]
    [InlineData("leaving")]
    public void WordSenseBuilder_FormGenerator_Data_IngForm_LeadsWithVerb(string spelling)
    {
        var first = Rows(spelling).OrderBy(r => int.Parse(r[1], System.Globalization.CultureInfo.InvariantCulture)).First();
        Assert.EndsWith("-v", first[2], StringComparison.Ordinal);
        Assert.Contains("VerbForm=Ger", first[3], StringComparison.Ordinal);
    }

    /// <summary>
    ///     No -ing spelling is a plural noun.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Data_IngForm_IsNotPluralNoun()
    {
        Assert.DoesNotContain(Rows("walking"), r => r[3].Contains("Number=Plur", StringComparison.Ordinal));
        Assert.DoesNotContain(Rows("smoking"), r => r[3].Contains("Number=Plur", StringComparison.Ordinal));
    }

    /// <summary>
    ///     A participial adjective of the dictionary keeps its own analysis beside the verb form that an earlier
    ///     reviewed analysis gave the spelling (damaged as the adjective in a damaged seal).
    /// </summary>
    /// <param name="spelling">The spelling.</param>
    /// <param name="entry">The entry of the adjective.</param>
    [Theory]
    [InlineData("damaged", "oewn-damaged-a")]
    [InlineData("drained", "oewn-drained-a")]
    [InlineData("drilled", "oewn-drilled-a")]
    [InlineData("alloyed", "oewn-alloyed-a")]
    public void WordSenseBuilder_FormGenerator_Data_OwnAdjectiveEntry_HasItsAnalysis(string spelling, string entry)
    {
        Assert.Contains(Rows(spelling), r => r[2] == entry && r[3] == "Degree=Pos");
    }

    /// <summary>
    ///     Only the participial adjectives of the list <c>own-adjectives.tsv</c> get the analysis: the common participles
    ///     (covered, changed, based) stay verb forms, as the reviewed analyses have them.
    /// </summary>
    /// <param name="spelling">The spelling.</param>
    /// <param name="entry">The entry of the adjective.</param>
    [Theory]
    [InlineData("covered", "oewn-covered-a")]
    [InlineData("changed", "oewn-changed-a")]
    [InlineData("based", "oewn-based-a")]
    public void WordSenseBuilder_FormGenerator_Data_UnlistedParticipialAdjective_KeepsTheReviewedAnalysis(string spelling, string entry)
    {
        Assert.DoesNotContain(Rows(spelling), r => r[2] == entry);
    }

    /// <summary>
    ///     Every spelling of <c>own-adjectives.tsv</c> has the analysis of its adjective entry.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Data_AdjectiveEntries_AreReachable()
    {
        var data = RepositoryData();
        var listed = File.ReadAllLines(Path.Join(data, "..", "corpus", "lexicon", "own-adjectives.tsv"))
            .Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')[0]).ToList();
        Assert.NotEmpty(listed);
        var lost = listed.Where(s => !Rows(s).Any(r => r[2] == "oewn-" + s + "-a")).ToList();
        Assert.Empty(lost);
    }

    /// <summary>
    ///     A spelling of the reviewed input gains the base analysis of the dictionary entry whose part of speech the
    ///     additions list names for the word and that no reviewed row names: the adjective of <c>graduate</c> (a graduate
    ///     student) and the noun of <c>separate</c> (the separates of a wardrobe).
    /// </summary>
    /// <param name="spelling">The spelling.</param>
    /// <param name="entry">The entry of the analysis.</param>
    /// <param name="features">The features of the analysis.</param>
    [Theory]
    [InlineData("graduate", "oewn-graduate-a", "Degree=Pos")]
    [InlineData("separate", "oewn-separate-n", "Number=Sing")]
    public void WordSenseBuilder_FormGenerator_Data_AddedPartOfSpeech_GainsTheAnalysisOfAnEntryWithoutOne(string spelling, string entry, string features)
    {
        // Arrange: the shipped form table, read for the spelling of the added part of speech
        var rows = Rows(spelling);

        // Act: look for the base analysis of the entry
        var found = rows.Any(r => r[2] == entry && r[3] == features);

        // Assert: the analysis is there
        Assert.True(found);
    }

    /// <summary>
    ///     The reviewed analysis of a spelling stays when the additions list gives it a part of speech (the verb
    ///     <c>graduate</c>), and a spelling whose dictionary has no entry of the part of speech gains none (no adjective
    ///     <c>desert</c>, no noun <c>reread</c>).
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Data_AddedPartOfSpeech_KeepsTheReviewedAnalysesAndInventsNone()
    {
        // Arrange: the spellings that have a reviewed analysis and those whose dictionary lacks the part of speech
        var graduate = Rows("graduate").ToList();
        var desert = Rows("desert").ToList();
        var reread = Rows("reread").ToList();

        // Act: look for the reviewed verb of graduate, an adjective of desert and a noun of reread
        var keptVerb = graduate.Any(r => r[2] == "oewn-graduate-v" && r[3] == "VerbForm=Inf");
        var inventedAdjective = desert.Any(r => r[2].EndsWith("-a", StringComparison.Ordinal));
        var inventedNoun = reread.Any(r => r[2].EndsWith("-n", StringComparison.Ordinal));

        // Assert: the reviewed analysis stays and no analysis is invented
        Assert.True(keptVerb);
        Assert.False(inventedAdjective);
        Assert.False(inventedNoun);
    }

    /// <summary>
    ///     A spelling that the reviewed input marks open gains the analyses that regular inflection gives the entries of
    ///     the dictionary: the plural and the third person of <c>axe</c> in <c>axes</c> and the plural of the noun
    ///     <c>separate</c> in <c>separates</c>.
    /// </summary>
    /// <param name="spelling">The spelling.</param>
    /// <param name="entry">The entry of the analysis.</param>
    /// <param name="features">The features of the analysis.</param>
    [Theory]
    [InlineData("separates", "oewn-separate-n", "Number=Plur")]
    [InlineData("axes", "oewn-axe-n", "Number=Plur")]
    [InlineData("axes", "oewn-axe-v", "Person=3+Tense=Pres")]
    public void WordSenseBuilder_FormGenerator_Data_OpenSpelling_GainsTheInflectionOfAnEntryWithoutOne(string spelling, string entry, string features)
    {
        // Arrange: the shipped form table, read for the open spelling
        var rows = Rows(spelling);

        // Act: collect the regular inflections of the entry
        var found = rows.Where(r => r[2] == entry && r[3] == features).ToList();

        // Assert: the analysis is there exactly once
        Assert.Single(found);
    }

    /// <summary>
    ///     The reviewed analyses of an open spelling stay beside the analyses that it gains (the plurals of <c>ax</c> and
    ///     of <c>axis</c> in <c>axes</c>).
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Data_OpenSpelling_KeepsItsReviewedAnalyses()
    {
        // Arrange: the shipped form table, read for the open spelling
        var rows = Rows("axes").ToList();

        // Act: look for the two reviewed plurals
        var pluralOfAx = rows.Any(r => r[2] == "oewn-ax-n" && r[3] == "Number=Plur");
        var pluralOfAxis = rows.Any(r => r[2] == "oewn-axis-n" && r[3] == "Number=Plur");

        // Assert: both stay
        Assert.True(pluralOfAx);
        Assert.True(pluralOfAxis);
    }

    /// <summary>
    ///     A verb that the dictionary lacks but the additions list has verb forms (torque a bolt).
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Data_AddedVerb_HasItsForms()
    {
        Assert.Contains(Rows("torque"), r => r[7] == "v" && r[3] == "VerbForm=Inf");
        Assert.Contains(Rows("torqued"), r => r[7] == "v" && r[3].Contains("Tense=Past", StringComparison.Ordinal));
        Assert.Contains(Rows("torquing"), r => r[7] == "v" && r[3] == "VerbForm=Ger");
    }

    /// <summary>
    ///     The analyses that our own sources showed wrong are gone or retyped.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Data_AuditCorrections_AreApplied()
    {
        foreach (var gone in new[] { "araglis", "madornos", "cercariiae", "filariiae", "photosynthesises" })
        {
            Assert.Empty(Rows(gone));
        }

        Assert.DoesNotContain(Rows("behooves"), r => r[3] == "Number=Plur");
        Assert.DoesNotContain(Rows("kindling"), r => r[3] == "Number=Plur");
        Assert.DoesNotContain(Rows("unmaned"), r => r[3].Contains("VerbForm=Part", StringComparison.Ordinal));
        Assert.DoesNotContain(Rows("cates"), r => r[2].EndsWith("CAT-n", StringComparison.Ordinal));
        Assert.Contains(Rows("muskallunge"), r => r[3] == "Number=Sing");
        Assert.Contains(Rows("gan"), r => r[3] == "Tense=Past");
    }

    /// <summary>
    ///     A spelling in s of a verb is a third person, never a past.
    /// </summary>
    /// <param name="spelling">The spelling.</param>
    [Theory]
    [InlineData("chevies")]
    [InlineData("gasses")]
    [InlineData("programmes")]
    [InlineData("swops")]
    public void WordSenseBuilder_FormGenerator_Data_VerbSpellingInS_IsThirdPerson(string spelling)
    {
        Assert.DoesNotContain(Rows(spelling), r => r[3].Contains("Tense=Past", StringComparison.Ordinal));
    }

    /// <summary>
    ///     A phrase whose head is an irregular verb takes no regular past (goed ahead).
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Data_PhraseWithIrregularHead_HasNoRegularPast()
    {
        Assert.Empty(Rows("goed ahead"));
        Assert.Empty(Rows("bed active"));
        Assert.Empty(Rows("doed away with"));
    }

    /// <summary>
    ///     The spellings that the regular rules once gave wrongly are not shipped: the plural of a plural lemma in -men
    ///     (fix 1), -mans for a compound of man (fix 2), -ys for a noun in -quy (fix 7), -os for a compound of hero (fix 8),
    ///     -es for an invariant unit (fix 10) and the gerund of a verb in -ie that keeps the e (fix 9).
    /// </summary>
    /// <param name="spelling">The spelling.</param>
    [Theory]
    [InlineData("bagmens")]
    [InlineData("dairywomens")]
    [InlineData("icewomans")]
    [InlineData("obsequys")]
    [InlineData("superheros")]
    [InlineData("hertzes")]
    [InlineData("birdiing")]
    public void WordSenseBuilder_FormGenerator_Data_RuleDefect_IsNotShipped(string spelling)
    {
        Assert.Empty(Rows(spelling));
    }

    /// <summary>
    ///     The corrected spellings are shipped, and the forms that the authors wrote carry the provenance llm.
    /// </summary>
    /// <param name="spelling">The spelling.</param>
    /// <param name="entry">The entry that has the form.</param>
    [Theory]
    [InlineData("icewomen", "oewn-icewoman-n")]
    [InlineData("superheroes", "oewn-superhero-n")]
    [InlineData("obsequies", "oewn-obsequy-n")]
    [InlineData("birdieing", "oewn-birdie-v")]
    [InlineData("wiretapped", "oewn-wiretap-v")]
    public void WordSenseBuilder_FormGenerator_Data_AuthoredForm_ShipsWithTheProvenanceLlm(string spelling, string entry)
    {
        Assert.Contains(Rows(spelling), r => r[2] == entry && r[^1] == "llm");
    }

    /// <summary>
    ///     A word in -men that is no plural, a word in -man whose plural is regular and a noun that only ends like hero keep the
    ///     regular plural.
    /// </summary>
    /// <param name="spelling">The spelling.</param>
    [Theory]
    [InlineData("specimens")]
    [InlineData("germans")]
    [InlineData("rancheros")]
    public void WordSenseBuilder_FormGenerator_Data_RegularPlural_OfAWordThatOnlyEndsLikeACompound_IsShipped(string spelling)
    {
        Assert.Contains(Rows(spelling), r => r[3].Contains("Number=Plur", StringComparison.Ordinal));
    }

    /// <summary>
    ///     The authored forms are authoritative: foots is no plural of foot, because the authors gave feet only.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Data_AuthoredPlural_LeavesOutTheRegularSpelling()
    {
        Assert.DoesNotContain(Rows("foots"), r => r[3].Contains("Number=Plur", StringComparison.Ordinal));
        Assert.Contains(Rows("feet"), r => r[3].Contains("Number=Plur", StringComparison.Ordinal));
    }
}
