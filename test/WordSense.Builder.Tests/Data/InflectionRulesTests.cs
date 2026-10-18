using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for <see cref="InflectionRules"/>: the regular English inflection rules used by the form generator.
/// </summary>
public sealed class InflectionRulesTests
{
    /// <summary>
    ///     Regular noun plurals.
    /// </summary>
    /// <param name="word">The singular.</param>
    /// <param name="plural">The expected primary plural.</param>
    [Theory]
    [InlineData("cat", "cats")]
    [InlineData("box", "boxes")]
    [InlineData("church", "churches")]
    [InlineData("city", "cities")]
    [InlineData("day", "days")]
    public void WordSenseBuilder_InflectionRules_NounPlural_Regular_AddsSuffix(string word, string plural)
    {
        Assert.Equal(plural, InflectionRules.NounPlural(word)[0]);
    }

    /// <summary>
    ///     Third person singular of verbs.
    /// </summary>
    /// <param name="word">The base.</param>
    /// <param name="third">The expected form.</param>
    [Theory]
    [InlineData("walk", "walks")]
    [InlineData("pass", "passes")]
    [InlineData("carry", "carries")]
    [InlineData("play", "plays")]
    public void WordSenseBuilder_InflectionRules_VerbThirdPerson_Regular_AddsSuffix(string word, string third)
    {
        Assert.Equal(third, InflectionRules.VerbThirdPerson(word));
    }

    /// <summary>
    ///     Regular past and gerund spellings, including doubling and dropping of letters.
    /// </summary>
    /// <param name="word">The base.</param>
    /// <param name="past">The expected past.</param>
    /// <param name="gerund">The expected gerund.</param>
    [Theory]
    [InlineData("walk", "walked", "walking")]
    [InlineData("stop", "stopped", "stopping")]
    [InlineData("hope", "hoped", "hoping")]
    [InlineData("carry", "carried", "carrying")]
    public void WordSenseBuilder_InflectionRules_VerbRegular_Spellings(string word, string past, string gerund)
    {
        var forms = InflectionRules.VerbRegular(word, _ => false);
        Assert.Equal(gerund, forms.Gerund[0]);
        Assert.Equal(past, forms.Past[0]);
    }

    /// <summary>
    ///     Comparative and superlative of short adjectives; long ones take none.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_InflectionRules_AdjectiveDegrees_ShortAndLong()
    {
        Assert.Equal(("bigger", "biggest"), InflectionRules.AdjectiveDegrees("big"));
        Assert.Equal(("happier", "happiest"), InflectionRules.AdjectiveDegrees("happy"));
        Assert.Null(InflectionRules.AdjectiveDegrees("beautiful"));
    }

    /// <summary>
    ///     The reverse candidates of a plural include its singular.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_InflectionRules_ReverseCandidates_Plural_ContainsSingular()
    {
        var candidates = InflectionRules.ReverseCandidates("cities", "n");
        Assert.Contains(candidates, c => c.Base == "city" && c.Features == "Number=Plur");
    }
}
