using System.Globalization;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Helpers that build corpus rows from the real sense catalog of the word image, so the tests do not hard-code
///     sense ids that the data may relabel.
/// </summary>
internal static class SenseFixtures
{
    /// <summary>
    ///     Takes the first senses of a lemma and part of speech in catalog order.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="pos">The part of speech.</param>
    /// <param name="count">The number of senses wanted.</param>
    /// <returns>The senses.</returns>
    public static SenseInfo[] Senses(string lemma, PartOfSpeech pos, int count)
    {
        var senses = WordLexicon.SensesOf(lemma, pos);
        Assert.True(senses.Length >= count, $"The catalog has fewer than {count} senses of {lemma} ({pos}).");
        return [.. senses.Take(count)];
    }

    /// <summary>
    ///     Builds an anchor row for a sense: the sentence is <c>The {lemma} number.</c> with the lemma as target.
    /// </summary>
    /// <param name="sense">The sense of the row.</param>
    /// <param name="number">A number that makes the sentence unique.</param>
    /// <returns>The row.</returns>
    public static CorpusRow Row(SenseInfo sense, int number)
    {
        var text = number.ToString(CultureInfo.InvariantCulture);
        return new CorpusRow(sense.Lemma, sense.SenseId, sense.PartOfSpeech, $"The {{{sense.Lemma}}} {text}.", "a.tsv", ["The", sense.Lemma, text, "."], 1);
    }

    /// <summary>
    ///     Finds a lemma of the catalog whose noun senses include two senses of one group, a sense of another group and a
    ///     sense that is in no group.
    /// </summary>
    /// <param name="lemma">The lemma that is searched.</param>
    /// <returns>Two senses of one group, a sense of another group and a sense without a group.</returns>
    public static (SenseInfo A1, SenseInfo A2, SenseInfo B, SenseInfo None) Grouped(string lemma)
    {
        var senses = WordLexicon.SensesOf(lemma, PartOfSpeech.Noun);
        var byGroup = senses.Where(s => s.GroupId is not null).GroupBy(s => s.GroupId).ToList();
        var pair = byGroup.First(g => g.Count() >= 2).Take(2).ToArray();
        var other = byGroup.First(g => g.Key != pair[0].GroupId).First();
        var none = senses.First(s => s.GroupId is null);
        return (pair[0], pair[1], other, none);
    }
}
