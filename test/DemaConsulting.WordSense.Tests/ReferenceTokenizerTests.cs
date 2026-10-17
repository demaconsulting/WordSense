using System.Text.RegularExpressions;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests that the reference tokenizer published in the documentation behaves as documented.
/// </summary>
public class ReferenceTokenizerTests
{
    /// <summary>
    ///     The pattern as it is written in the namespace documentation, the README and the user guide.
    /// </summary>
    private const string Pattern = @"\d+(?:[.,]\d+)+|[\p{L}\p{Nd}\p{Mn}\p{Mc}]+(?:['\u2019-][\p{L}\p{Nd}\p{Mn}\p{Mc}]+)*|[\uD800-\uDBFF][\uDC00-\uDFFF]|\S";

    private static readonly Regex TokenPattern = new(Pattern, RegexOptions.CultureInvariant);

    private static string[] Tokenize(string sentence) => TokenPattern.Matches(sentence).Select(match => match.Value).ToArray();

    /// <summary>
    ///     The documented sentence is split as the documentation states.
    /// </summary>
    [Fact]
    public void ReferenceTokenizer_DocumentedSentence_SplitsAsDocumented()
    {
        string[] expected = ["Smith's", "well-known", "dog", "didn't", "\"", "bark", "\"", "at", "3.5", "cats", "."];

        Assert.Equal(expected, Tokenize("Smith's well-known dog didn't \"bark\" at 3.5 cats."));
    }

    /// <summary>
    ///     Punctuation, dashes, abbreviations and numbers with separators follow the documented rules.
    /// </summary>
    [Fact]
    public void ReferenceTokenizer_PunctuationAndNumbers_FollowDocumentedRules()
    {
        Assert.Equal(["Mr", ".", "Jones", "paid", "1,000", "—", "or", "so", "!"], Tokenize("Mr. Jones paid 1,000 — or so!"));
        Assert.Equal(["(", "it", "rained", ")", ",", "he", "said", "."], Tokenize("(it rained), he said."));
    }

    /// <summary>
    ///     A curly apostrophe stays inside the word, and a trailing period is split off.
    /// </summary>
    [Fact]
    public void ReferenceTokenizer_CurlyApostropheAndTrailingPeriod_AreHandled()
    {
        var sentence = "They don" + (char)0x2019 + "t race.";

        Assert.Equal(["They", "don" + (char)0x2019 + "t", "race", "."], Tokenize(sentence));
    }

    /// <summary>
    ///     The documented tokens classify as documented: a contraction is unknown, a lexicon word is found.
    /// </summary>
    [Fact]
    public async Task ReferenceTokenizer_Tokens_ClassifyAsDocumented()
    {
        var model = new Basic.BasicWordSenseModel();
        var classifier = model.Create();

        var senses = await classifier.ClassifyAsync(Tokenize("They didn't record the record."), TestContext.Current.CancellationToken);

        Assert.Equal(6, senses.Length);
        Assert.Null(senses[1].Details);
        Assert.Equal(0, senses[1].Confidence);
        Assert.NotNull(senses[2].Details);
    }

    /// <summary>
    ///     The pattern in the tests is the pattern that the shipped documentation contains.
    /// </summary>
    [Fact]
    public void ReferenceTokenizer_Pattern_MatchesNamespaceDocumentation()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "DemaConsulting.WordSense.xml");
        var documentation = File.ReadAllText(path);

        Assert.Contains(Pattern, documentation, StringComparison.Ordinal);
    }
}
