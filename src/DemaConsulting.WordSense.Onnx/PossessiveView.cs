namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The words of a text as the part-of-speech head sees them, and their pooled encoder vectors.
/// </summary>
/// <remarks>
///     A possessive that is written as one token (<c>technician's</c>) is two tokens in the view (<c>technician</c> and
///     <c>'s</c>), the way the open corpus that the head was trained on separates them. <see cref="Start"/> maps a word of
///     the text to its first token in the view, so the vector of a word is <c>Vectors[Start[i]]</c>, its previous
///     neighbor is <c>Vectors[Start[i] - 1]</c> (for a word after a possessive that is the vector of the <c>'s</c>) and
///     its next neighbor is the token after the word (<c>Start[i + 1]</c>, or the <c>'s</c> for a possessive).
/// </remarks>
/// <param name="Tokens">The tokens of the view: the words of the text with every possessive split.</param>
/// <param name="Start">For every word of the text, the index of its first token in <paramref name="Tokens"/>.</param>
/// <param name="Vectors">The pooled vector of every token of the view, or null for a token without word pieces.</param>
internal sealed record WordVectors(IReadOnlyList<string> Tokens, int[] Start, double[]?[] Vectors);

/// <summary>
///     The possessive view of a text: a possessive written as one token is split into its stem and its marker.
/// </summary>
/// <remarks>
///     The WordPiece tokens of the split view equal those of the joined token, so the encoder sees the same text. What
///     changes is the pooling: the stem gets its own vector (the vector of the stem pieces alone, which is what the head
///     saw in training) and the marker <c>'s</c> gets one of its own, which is the previous vector of the next word.
/// </remarks>
internal static class PossessiveView
{
    private const string PossessiveFeature = "Poss=Yes";

    /// <summary>
    ///     Splits every possessive of a text.
    /// </summary>
    /// <param name="words">The words of the text.</param>
    /// <param name="isPossessive">Tells whether a word is a possessive written as one token.</param>
    /// <returns>The tokens of the view and, for every word, the index of its first token.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument or a word is null.</exception>
    public static (string[] Tokens, int[] Start) Expand(IReadOnlyList<string> words, Func<string, bool> isPossessive)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(isPossessive);
        var tokens = new List<string>(words.Count + 4);
        var start = new int[words.Count];
        for (var i = 0; i < words.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(words[i]);
            start[i] = tokens.Count;
            var suffix = isPossessive(words[i]) ? SuffixLength(words[i]) : 0;
            if (suffix > 0 && words[i].Length > suffix)
            {
                tokens.Add(words[i][..^suffix]);
                tokens.Add(words[i][^suffix..]);
            }
            else
            {
                tokens.Add(words[i]);
            }
        }

        return ([.. tokens], start);
    }

    /// <summary>
    ///     Tells whether a word is a possessive written as one token: it ends in <c>'s</c> or <c>'</c> after a stem, it is in
    ///     the lexicon and every analysis of it carries <c>Poss=Yes</c> (<c>technician's</c>, <c>sergeants'</c>).
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <returns>True when the word is a possessive.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the word is null.</exception>
    public static bool IsLexiconPossessive(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var key = BasicClassifier.Normalize(word);
        var suffix = SuffixLength(key);
        if (suffix == 0 || key.Length <= suffix || !WordLexicon.TryLookup(key, out var analyses) || analyses.IsDefaultOrEmpty)
        {
            return false;
        }

        return analyses.All(a => HasFeature(a.Features, PossessiveFeature));
    }

    /// <summary>
    ///     Gets the length of the possessive marker at the end of a word.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <returns>2 for <c>'s</c>, 1 for a final apostrophe, 0 for neither (the typographic apostrophe counts).</returns>
    private static int SuffixLength(string word)
    {
        var n = word.Length;
        if (n >= 2 && IsApostrophe(word[n - 2]) && (word[n - 1] == 's' || word[n - 1] == 'S'))
        {
            return 2;
        }

        return n >= 1 && IsApostrophe(word[n - 1]) ? 1 : 0;
    }

    private static bool IsApostrophe(char c) => c is '\'' or '\u2019';

    /// <summary>
    ///     Tells whether a feature string, whose items are separated by <c>+</c>, contains one item exactly.
    /// </summary>
    /// <param name="features">The feature string of an analysis.</param>
    /// <param name="item">The item.</param>
    /// <returns>True when the item is one of the items.</returns>
    private static bool HasFeature(string features, string item) =>
        features.Split('+').Contains(item, StringComparer.Ordinal);
}
