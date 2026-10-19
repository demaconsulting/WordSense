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
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "schema.yaml")))
        {
            dir = dir.Parent;
        }

        return dir is null ? throw new InvalidOperationException("The data folder was not found.") : Path.Combine(dir.FullName, "data");
    }

    private static List<string[]> Load()
    {
        var data = RepositoryData();
        var schema = DataSchema.Load(Path.Combine(data, "schema.yaml"));
        return DataTables.Read(data, schema.Table("form")).Select(r => r.ToArray()).ToList();
    }

    private static IEnumerable<string[]> Rows(string spelling) => Forms.Value.Where(r => r[0] == spelling);

    /// <summary>
    ///     Every row names a provenance from the closed list.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Data_Provenance_IsKnown()
    {
        string[] known = ["agid", "carried", "contraction", "hand", "irregular_list", "moby_reverse", "oewn_form", "rule", "rule_variant", "llm"];
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
}
