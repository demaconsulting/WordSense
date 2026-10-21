using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     An analysis that a corpus row stands for: a lemma, a part of speech and the features that must be present.
/// </summary>
/// <param name="Lemma">The lemma of the analysis.</param>
/// <param name="Pos">The part of speech of the analysis.</param>
/// <param name="Features">The required grammatical features (<c>Key=Value</c> items joined by <c>+</c>); empty when any features do.</param>
internal readonly record struct AnalysisKey(string Lemma, PartOfSpeech Pos, string Features)
{
    /// <summary>
    ///     Determines whether an analysis of the lexicon is the one this key stands for: the same lemma and part of speech
    ///     and every required feature item present.
    /// </summary>
    /// <param name="details">The analysis of the lexicon.</param>
    /// <returns>True when the analysis matches.</returns>
    public bool Matches(WordDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (!string.Equals(Lemma, details.Lemma, StringComparison.Ordinal) || Pos != details.PartOfSpeech)
        {
            return false;
        }

        if (Features.Length == 0)
        {
            return true;
        }

        var have = details.Features.Split('+');
        return Features.Split('+').All(item => have.Contains(item, StringComparer.Ordinal));
    }

    /// <summary>
    ///     Finds the spelling of the lemma that the word image uses. A corpus lemma is lower-case, but the entry of the
    ///     lexicon may spell it with a capital (<c>august</c> the adjective, <c>August</c> the month): an analysis that the
    ///     image does not have is looked for under the capitalized spelling.
    /// </summary>
    /// <param name="key">The key with the corpus lemma.</param>
    /// <returns>The key with the spelling of the image when it has the analysis only under a capital, otherwise the key unchanged.</returns>
    public static AnalysisKey Resolve(AnalysisKey key)
    {
        var image = WordLexicon.Current;
        if (key.Lemma.Length == 0 || image.AnalysisExists(key.Lemma, key.Pos, key.Features))
        {
            return key;
        }

        var capitalized = string.Concat(char.ToUpperInvariant(key.Lemma[0]).ToString(), key.Lemma.AsSpan(1));
        return WordLexicon.TryLookup(capitalized, out var found)
            && found.FirstOrDefault(d => d.PartOfSpeech == key.Pos && string.Equals(d.Lemma, key.Lemma, StringComparison.OrdinalIgnoreCase)) is { } match
            && image.AnalysisExists(match.Lemma, key.Pos, key.Features)
                ? key with { Lemma = match.Lemma }
                : key;
    }
}
