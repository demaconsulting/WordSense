namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     System-level tests of the sense catalog of the lexicon: the identifiers, keys and groups of the senses as a caller
///     sees them through the public lookups.
/// </summary>
public class CatalogTests
{
    /// <summary>
    ///     Every sense id is unique, and every OEWN sense key leads to exactly one sense.
    /// </summary>
    [Fact]
    public void WordSense_Catalog_SenseIds_AreUniqueAndLinkedToOneKey()
    {
        var senses = WordLexicon.Current.AllSenses().ToList();

        Assert.Equal(senses.Count, senses.Select(s => s.SenseId).Distinct(StringComparer.Ordinal).Count());
        var keyed = senses.Where(s => s.SenseKey is not null).ToList();
        Assert.Equal(keyed.Count, keyed.Select(s => s.SenseKey).Distinct(StringComparer.Ordinal).Count());
        Assert.All(senses, s => Assert.StartsWith(s.Lemma + "#", s.SenseId, StringComparison.Ordinal));
    }

    /// <summary>
    ///     A project sense (a meaning that Open English WordNet does not cover) has an id and no key.
    /// </summary>
    [Fact]
    public void WordSense_Catalog_ProjectSenses_HaveNoKey()
    {
        Assert.True(WordLexicon.TryGetSense("the#determiner", out var the));
        Assert.True(WordLexicon.TryGetSense("or#conjunction", out var or));

        Assert.Null(the.SenseKey);
        Assert.Null(or.SenseKey);
        Assert.Contains(WordLexicon.Current.AllSenses(), s => s.SenseKey is null && s.SenseId == "well#discourse-marker");
    }

    /// <summary>
    ///     The id and the key of a sense lead to the same sense.
    /// </summary>
    [Theory]
    [InlineData("bank#incline", "bank%1:17:01::")]
    [InlineData("back#football-back", "back%1:18:00::")]
    public void WordSense_Catalog_LookupByIdAndByKey_FindTheSameSense(string id, string key)
    {
        Assert.True(WordLexicon.TryGetSense(id, out var byId));
        Assert.True(WordLexicon.TryGetSenseByKey(key, out var byKey));

        Assert.Equal(byId, byKey);
        Assert.Equal(key, byId.SenseKey);
    }

    /// <summary>
    ///     A sense is in at most one group, and a group is only a set of some senses of one lemma and part of speech.
    /// </summary>
    [Fact]
    public void WordSense_Catalog_EverySense_BelongsToAtMostOneGroup()
    {
        var groupedSenses = WordLexicon.Current.AllSenses().Where(s => s.GroupId is not null).ToList();
        var members = WordLexicon.Current.AllGroups().SelectMany(g => g.SenseIds).ToList();

        Assert.NotEmpty(groupedSenses);
        Assert.Equal(members.Count, members.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(groupedSenses.Count, members.Count);
        Assert.Contains(WordLexicon.Current.AllSenses(), s => s.GroupId is null);
    }

    /// <summary>
    ///     A group lists its senses, and each of them names the group back.
    /// </summary>
    [Fact]
    public void WordSense_Catalog_Group_ListsItsSenses()
    {
        Assert.True(WordLexicon.TryGetGroup("back#group:football", out var group));

        Assert.All(group.SenseIds, id =>
        {
            Assert.True(WordLexicon.TryGetSense(id, out var sense));
            Assert.Equal(group.GroupId, sense.GroupId);
            Assert.Equal(group.Lemma, sense.Lemma);
        });
    }
}
