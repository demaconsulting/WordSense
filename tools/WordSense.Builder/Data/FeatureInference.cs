namespace WordSense.Builder.Data;

/// <summary>
///     Infers the grammatical features of an analysis from the spelling, the lemma and the part of speech, with the
///     regular English endings. Used once by the bootstrap; every row it infers is marked as not reviewed.
/// </summary>
internal static class FeatureInference
{
    /// <summary>
    ///     Infers the features.
    /// </summary>
    /// <param name="spelling">The written form.</param>
    /// <param name="lemma">The lemma of the entry.</param>
    /// <param name="pos">The part of speech letter of the <c>pos</c> vocabulary.</param>
    /// <returns>The features in canonical order (sorted items joined by <c>+</c>), or an empty string when unknown.</returns>
    public static string Infer(string spelling, string lemma, string pos)
    {
        var form = DataLayout.AsciiLower(spelling);
        var @base = DataLayout.AsciiLower(lemma);
        var same = form == @base;
        switch (pos)
        {
            case "n":
                return same ? "Number=Sing" : "Number=Plur";
            case "v":
                if (same)
                {
                    return "VerbForm=Inf";
                }

                if (form.EndsWith("ing", StringComparison.Ordinal))
                {
                    return "VerbForm=Ger";
                }

                if (form.EndsWith("ed", StringComparison.Ordinal))
                {
                    return "Tense=Past";
                }

                return form.EndsWith('s') ? "Person=3+Tense=Pres" : string.Empty;
            case "a":
            case "r":
                if (same)
                {
                    return "Degree=Pos";
                }

                if (form.EndsWith("est", StringComparison.Ordinal))
                {
                    return "Degree=Sup";
                }

                return form.EndsWith("er", StringComparison.Ordinal) ? "Degree=Cmp" : string.Empty;
            default:
                return string.Empty;
        }
    }
}
