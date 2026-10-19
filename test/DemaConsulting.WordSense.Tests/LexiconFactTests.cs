namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Linguistic facts about the lexicon, checked row by row against the shipped word image. Two files carry the facts:
///     <c>lexicon-facts.tsv</c> holds the hand-observed rows (words seen failing, kept as regression guards) and
///     <c>lexicon-facts-generated.tsv</c> holds the source-derived rows that the <c>generate-facts</c> command of the builder
///     writes mechanically from the sources of the lexicon with a fixed seed. A fact asserts that an analysis exists (any) or
///     that the word is unknown (absent); it never asserts the order of the analyses, because the rank of an analysis is only a
///     prior and not the answer for a sentence.
/// </summary>
public class LexiconFactTests
{
    private static readonly string[] FactFiles = ["lexicon-facts.tsv", "lexicon-facts-generated.tsv"];
    private static readonly string[] AllowedScopes = ["any", "absent"];

    /// <summary>
    ///     The fact rows of both files: category, word, lemma, part of speech, features ("*" for any, a leading "~" for features that must all be among those of the analysis) and the mode (any, absent).
    /// </summary>
    public static TheoryData<string, string, string, string, string, string> Facts()
    {
        var data = new TheoryData<string, string, string, string, string, string>();
        foreach (var c in ReadRows())
        {
            data.Add(c[0], c[1], c[2], c[3], c[4], c[5]);
        }

        return data;
    }

    /// <summary>
    ///     Each fact holds: the analysis exists (any) or the word is unknown (absent).
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

        bool Matches(WordDetails s) =>
            (lemma == "*" || string.Equals(s.Lemma, lemma, StringComparison.OrdinalIgnoreCase))
            && (pos == "*" || s.PartOfSpeech == Enum.Parse<PartOfSpeech>(pos))
            && (features == "*" || s.Features == features
                || (features.StartsWith('~') && features[1..].Split('+').All(p => s.Features.Split('+').Contains(p))));

        Assert.True(senses.Any(Matches), $"{category}: '{word}' lacks {lemma} {pos} {features}");
    }

    /// <summary>
    ///     The fact files cover the hand-observed rows and every kind of source-derived row, and no row uses a mode other than
    ///     any or absent.
    /// </summary>
    [Fact]
    public void LexiconFacts_Files_CoverEveryCategoryAndMode()
    {
        var rows = ReadRows().ToList();

        Assert.Superset(
            new HashSet<string>
            {
                "hand-observed",
                "source-irregular-closed",
                "source-irregular-hand",
                "source-regular-verb",
                "source-regular-noun",
                "source-possessive",
                "source-contraction",
                "source-ing-entry",
            },
            rows.Select(r => r[0]).ToHashSet());
        Assert.All(rows, r => Assert.Contains(r[5], AllowedScopes));
    }

    /// <summary>
    ///     The words of a gerund have the verb gerund analysis and, when the dictionary lists an entry for the spelling as a
    ///     noun or adjective, that entry's analysis as well, so that none of its senses is unreachable.
    /// </summary>
    [Theory]
    [InlineData("tracing", "trace", PartOfSpeech.Verb, "tracing", PartOfSpeech.Noun)]
    [InlineData("drawing", "draw", PartOfSpeech.Verb, "drawing", PartOfSpeech.Noun)]
    [InlineData("walking", "walk", PartOfSpeech.Verb, "walking", PartOfSpeech.Adjective)]
    public void WordLexicon_Lookup_GerundWithOwnEntry_HasBothAnalyses(string word, string verbLemma, PartOfSpeech verbPos, string entryLemma, PartOfSpeech entryPos)
    {
        var senses = WordLexicon.Lookup(word);

        Assert.Contains(senses, s => s.Lemma == verbLemma && s.PartOfSpeech == verbPos && s.Features.Contains("VerbForm=Ger", StringComparison.Ordinal));
        Assert.Contains(senses, s => s.Lemma == entryLemma && s.PartOfSpeech == entryPos);
    }

    private static IEnumerable<string[]> ReadRows() =>
        FactFiles.SelectMany(file => File.ReadLines(Path.Join(AppContext.BaseDirectory, "Fixtures", file)).Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t')));
}
