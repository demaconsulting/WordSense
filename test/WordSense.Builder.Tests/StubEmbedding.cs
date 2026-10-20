using System.Globalization;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     A deterministic stand-in for the encoder: the vector of a target word depends on a marker word of its sentence
///     and on a spread that depends on the whole text, so senses can be made separable or confusable without a model.
/// </summary>
internal static class StubEmbedding
{
    /// <summary>
    ///     Builds the anchor row of a sense whose sentence carries a marker word that selects its embedding.
    /// </summary>
    /// <param name="sense">The sense of the row.</param>
    /// <param name="marker">The marker word (<c>noun</c>, <c>verb</c>, <c>a1</c>, <c>a2</c>, <c>b</c> or <c>n</c>).</param>
    /// <param name="number">A number that makes the sentence unique.</param>
    /// <returns>The row.</returns>
    public static CorpusRow Row(SenseInfo sense, string marker, int number)
    {
        var text = number.ToString(CultureInfo.InvariantCulture);
        return new CorpusRow(sense.Lemma, sense.SenseId, sense.PartOfSpeech, $"The {{{sense.Lemma}}} {marker} {text}.", "a.tsv", ["The", sense.Lemma, marker, text, "."], 1);
    }

    /// <summary>
    ///     Computes the pseudo-embedding of a target word.
    /// </summary>
    /// <param name="words">The words of the sentence; the third word is the marker.</param>
    /// <returns>A four-number vector.</returns>
    public static double[] Embed(IReadOnlyList<string> words)
    {
        var seed = 17u;
        foreach (var ch in string.Join(' ', words))
        {
            seed = (seed * 31) + ch;
        }

        double Unit(int k) => ((((seed >> k) * 2654435761u) >> 8) & 0xFFFF) / 65535.0;
        var (x, y) = words[2] switch
        {
            "noun" or "a1" or "a2" => (2.0, 0.5),
            "verb" or "b" => (-2.0, -0.5),
            _ => (0.0, 3.0),
        };
        return [x + Unit(1), y + Unit(3), Unit(5), Unit(7) - 0.5];
    }
}
