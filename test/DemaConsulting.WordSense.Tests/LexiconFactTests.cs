namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Linguistic facts about the lexicon, checked row by row against the shipped word image. The facts come from the
///     sources of the lexicon (irregular verb and noun lists, the contractions, the possessive rule and the words that an
///     earlier rebuild lost), not from the evaluation sentences.
/// </summary>
public class LexiconFactTests
{
    /// <summary>
    ///     The fact rows: category, word, lemma, part of speech, features ("*" for any) and the mode (any, first, absent).
    /// </summary>
    public static TheoryData<string, string, string, string, string, string> Facts()
    {
        var data = new TheoryData<string, string, string, string, string, string>();
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "lexicon-facts.tsv");
        foreach (var line in File.ReadLines(path).Skip(1).Where(l => l.Length > 0))
        {
            var c = line.Split('\t');
            data.Add(c[0], c[1], c[2], c[3], c[4], c[5]);
        }

        return data;
    }

    /// <summary>
    ///     Each fact holds: the analysis exists (any), leads (first) or the word is unknown (absent).
    /// </summary>
    [Theory]
    [MemberData(nameof(Facts))]
    public void WordLexicon_Lookup_Fact_Holds(string category, string word, string lemma, string pos, string features, string mode)
    {
        var senses = WordLexicon.Lookup(word);
        if (mode == "absent")
        {
            Assert.True(senses.IsDefaultOrEmpty, $"{category}: '{word}' must be unknown");
            return;
        }

        var part = Enum.Parse<PartOfSpeech>(pos);
        bool Matches(WordDetails s) =>
            string.Equals(s.Lemma, lemma, StringComparison.OrdinalIgnoreCase) && s.PartOfSpeech == part && (features == "*" || s.Features == features);

        Assert.NotEmpty(senses);
        if (mode == "first")
        {
            Assert.True(Matches(senses[0]), $"{category}: the first analysis of '{word}' is {senses[0].Lemma} {senses[0].PartOfSpeech} {senses[0].Features}");
        }
        else
        {
            Assert.True(senses.Any(Matches), $"{category}: '{word}' lacks {lemma} {pos} {features}");
        }
    }

    /// <summary>
    ///     The fact file is not empty and covers every category the fact test set promises.
    /// </summary>
    [Fact]
    public void LexiconFacts_File_CoversEveryCategory()
    {
        var categories = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "lexicon-facts.tsv")).Skip(1).Select(l => l.Split('\t')[0]).ToHashSet();

        Assert.Superset(new HashSet<string> { "irregular", "participle", "possessive", "contraction", "lost" }, categories);
    }
}
