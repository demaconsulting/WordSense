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
    [InlineData("unrip", "unripped", "unripping")]
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

    /// <summary>
    ///     Man, woman and their compounds take -men and -women (the authors of the forms agree on every compound they were asked).
    /// </summary>
    /// <param name="word">The singular.</param>
    /// <param name="plural">The expected primary plural.</param>
    [Theory]
    [InlineData("man", "men")]
    [InlineData("woman", "women")]
    [InlineData("chairman", "chairmen")]
    [InlineData("dairywoman", "dairywomen")]
    [InlineData("icewoman", "icewomen")]
    [InlineData("whaleman", "whalemen")]
    public void WordSenseBuilder_InflectionRules_NounPlural_ManCompound_EndsInMen(string word, string plural)
    {
        Assert.Equal(plural, InflectionRules.NounPlural(word)[0]);
    }

    /// <summary>
    ///     A word that merely ends in -man keeps the regular plural (human, german, talisman and the compounds of human).
    /// </summary>
    /// <param name="word">The singular.</param>
    /// <param name="plural">The expected primary plural.</param>
    [Theory]
    [InlineData("human", "humans")]
    [InlineData("superhuman", "superhumans")]
    [InlineData("german", "germans")]
    [InlineData("roman", "romans")]
    [InlineData("talisman", "talismans")]
    [InlineData("ottoman", "ottomans")]
    [InlineData("shaman", "shamans")]
    public void WordSenseBuilder_InflectionRules_NounPlural_WordEndingInMan_KeepsTheRegularPlural(string word, string plural)
    {
        Assert.Equal(plural, InflectionRules.NounPlural(word)[0]);
    }

    /// <summary>
    ///     The compounds of the nouns in -o, -f and -fe take the plural of the noun; a word that only ends alike does not.
    /// </summary>
    /// <param name="word">The singular.</param>
    /// <param name="plural">The expected primary plural.</param>
    [Theory]
    [InlineData("superhero", "superheroes")]
    [InlineData("antihero", "antiheroes")]
    [InlineData("subecho", "subechoes")]
    [InlineData("supercargo", "supercargoes")]
    [InlineData("ranchero", "rancheros")]
    [InlineData("mooncalf", "mooncalves")]
    [InlineData("werewolf", "werewolves")]
    [InlineData("midwife", "midwives")]
    [InlineData("penknife", "penknives")]
    [InlineData("lowlife", "lowlifes")]
    [InlineData("behalf", "behalfs")]
    public void WordSenseBuilder_InflectionRules_NounPlural_Compound_TakesThePluralOfItsLastNoun(string word, string plural)
    {
        Assert.Equal(plural, InflectionRules.NounPlural(word)[0]);
    }

    /// <summary>
    ///     A noun in -quy turns the y into ies (the u of qu is no vowel before the y).
    /// </summary>
    /// <param name="word">The singular.</param>
    /// <param name="plural">The expected plural.</param>
    [Theory]
    [InlineData("obsequy", "obsequies")]
    [InlineData("colloquy", "colloquies")]
    [InlineData("soliloquy", "soliloquies")]
    [InlineData("guy", "guys")]
    public void WordSenseBuilder_InflectionRules_NounPlural_QuY_EndsInIes(string word, string plural)
    {
        Assert.Equal(plural, InflectionRules.NounPlural(word)[0]);
    }

    /// <summary>
    ///     The final consonant doubles after the qu of a short word and in the compounds of a monosyllabic verb; a y before a vowel
    ///     is no vowel of the last syllable (crayon, layer, gauffer).
    /// </summary>
    /// <param name="word">The base.</param>
    /// <param name="past">The expected past.</param>
    /// <param name="gerund">The expected gerund.</param>
    [Theory]
    [InlineData("squeg", "squegged", "squegging")]
    [InlineData("quiz", "quizzed", "quizzing")]
    [InlineData("equip", "equipped", "equipping")]
    [InlineData("wiretap", "wiretapped", "wiretapping")]
    [InlineData("suntan", "suntanned", "suntanning")]
    [InlineData("shrinkwrap", "shrinkwrapped", "shrinkwrapping")]
    [InlineData("chairman", "chairmanned", "chairmanning")]
    [InlineData("beflag", "beflagged", "beflagging")]
    [InlineData("outwit", "outwitted", "outwitting")]
    [InlineData("crayon", "crayoned", "crayoning")]
    [InlineData("layer", "layered", "layering")]
    [InlineData("gauffer", "gauffered", "gauffering")]
    [InlineData("happen", "happened", "happening")]
    [InlineData("carpet", "carpeted", "carpeting")]
    public void WordSenseBuilder_InflectionRules_VerbRegular_Doubling_FollowsTheLastSyllable(string word, string past, string gerund)
    {
        var listed = new HashSet<string> { "wire", "sun", "shrink", "chair", "hap", "car", "name" };
        var forms = InflectionRules.VerbRegular(word, listed.Contains);
        Assert.Equal(past, forms.Past[0]);
        Assert.Equal(gerund, forms.Gerund[0]);
    }

    /// <summary>
    ///     The doubled spelling is the alternative; the dictionary listing it, and not the single one, makes it the primary
    ///     spelling (backlogged, kayakked), and a listing of neither leaves the rule.
    /// </summary>
    /// <param name="word">The base.</param>
    /// <param name="listedPast">The spelling that the dictionary lists.</param>
    /// <param name="expected">The expected primary past.</param>
    [Theory]
    [InlineData("backlog", "backlogged", "backlogged")]
    [InlineData("kayak", "kayakked", "kayakked")]
    [InlineData("backlog", "unrelated", "backloged")]
    [InlineData("travel", "travelled", "travelled")]
    [InlineData("travel", "unrelated", "traveled")]
    public void WordSenseBuilder_InflectionRules_VerbRegular_ListedDoubledSpelling_BecomesPrimary(string word, string listedPast, string expected)
    {
        var forms = InflectionRules.VerbRegular(word, w => w == listedPast);

        Assert.Equal(expected, forms.Past[0]);
    }

    /// <summary>
    ///     A verb in -ie keeps the e (birdie, boogie) unless it is tie, lie, vie, die, hie or a compound of tie, lie or vie.
    /// </summary>
    /// <param name="word">The base.</param>
    /// <param name="past">The expected past.</param>
    /// <param name="gerund">The expected gerund.</param>
    [Theory]
    [InlineData("birdie", "birdied", "birdieing")]
    [InlineData("boogie", "boogied", "boogieing")]
    [InlineData("caddie", "caddied", "caddieing")]
    [InlineData("tie", "tied", "tying")]
    [InlineData("die", "died", "dying")]
    [InlineData("untie", "untied", "untying")]
    [InlineData("hogtie", "hogtied", "hogtying")]
    [InlineData("outvie", "outvied", "outvying")]
    public void WordSenseBuilder_InflectionRules_VerbRegular_Ie_KeepsTheEOrTurnsToY(string word, string past, string gerund)
    {
        var forms = InflectionRules.VerbRegular(word, _ => false);
        Assert.Equal(past, forms.Past[0]);
        Assert.Equal(gerund, forms.Gerund[0]);
    }
}
