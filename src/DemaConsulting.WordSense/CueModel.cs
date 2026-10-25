using System.Collections.Frozen;
using System.Collections.Immutable;

namespace DemaConsulting.WordSense;

/// <summary>
///     One context feature of a target word: a neighboring token and where it was found.
/// </summary>
/// <param name="Kind">The kind of the feature: <see cref="CueModel.Window"/>, <see cref="CueModel.Left"/>, <see cref="CueModel.Right"/>, and for the tense model also <see cref="CueModel.LeftWindow"/>, <see cref="CueModel.RightWindow"/> and <see cref="CueModel.Governor"/>.</param>
/// <param name="Token">The lower-case neighboring token.</param>
internal readonly record struct FeatureRef(int Kind, string Token)
{
    /// <summary>
    ///     Gets the 16-bit hash of the feature: a 32-bit FNV-1a over the UTF-16 code units of its name (the token, with
    ///     a prefix of the kind: <c>L1:</c> or <c>R1:</c> for the immediate neighbors, <c>LW:</c> and <c>RW:</c> for the
    ///     left and right window of the tense model and <c>NA:</c> for its governing function word; the plain window
    ///     feature has none) folded by xor-ing its upper half into its lower half.
    /// </summary>
    public ushort Hash
    {
        get
        {
            var h = CueModel.OffsetBasis;
            if (Kind != CueModel.Window)
            {
                foreach (var ch in Prefix(Kind))
                {
                    h = Mix(h, ch);
                }
            }

            foreach (var ch in Token)
            {
                h = Mix(h, ch);
            }

            return (ushort)((h ^ (h >> 16)) & 0xFFFF);
        }
    }

    /// <summary>
    ///     Gets the name prefix of a feature kind that is not a plain window feature.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The prefix that is hashed before the token.</returns>
    private static string Prefix(int kind) => kind switch
    {
        CueModel.Left => "L1:",
        CueModel.Right => "R1:",
        CueModel.LeftWindow => "LW:",
        CueModel.RightWindow => "RW:",
        _ => "NA:",
    };

    /// <summary>
    ///     Folds one UTF-16 code unit into a running 32-bit FNV-1a hash: the low byte first, then the high byte.
    /// </summary>
    /// <param name="h">The running hash.</param>
    /// <param name="ch">The code unit to add.</param>
    /// <returns>The updated hash.</returns>
    private static uint Mix(uint h, char ch)
    {
        unchecked
        {
            h ^= (byte)(ch & 0xFF);
            h *= CueModel.Prime;
            h ^= (byte)(ch >> 8);
            h *= CueModel.Prime;
            return h;
        }
    }
}

/// <summary>
///     The context cue model: extracts the context features of a target word and scores the analyses of the word from
///     the table of hashed features of its training lemma, a naive Bayes classifier with additive smoothing whose log
///     weights are quantized to eighths of a nat. The same machinery with a richer feature set scores a verb as past or
///     non-past from the table of the tense model, which is shared by all verbs.
/// </summary>
internal static class CueModel
{
    /// <summary>
    ///     The kind of a feature found anywhere in the window around the target.
    /// </summary>
    public const int Window = 0;

    /// <summary>
    ///     The kind of the feature that is the token directly before the target.
    /// </summary>
    public const int Left = 1;

    /// <summary>
    ///     The kind of the feature that is the token directly after the target.
    /// </summary>
    public const int Right = 2;

    /// <summary>
    ///     The number of tokens on each side of the target that make window features.
    /// </summary>
    public const int WindowRadius = 5;

    /// <summary>
    ///     The largest number of features of one target.
    /// </summary>
    public const int MaxFeatures = (2 * WindowRadius) + 2;

    /// <summary>
    ///     The FNV-1a offset basis.
    /// </summary>
    public const uint OffsetBasis = 2166136261;

    /// <summary>
    ///     The FNV-1a prime.
    /// </summary>
    public const uint Prime = 16777619;

    /// <summary>
    ///     The scale of the quantized weights: a stored value of 1 is 1/8 nat.
    /// </summary>
    public const double Quantum = 0.125;

    /// <summary>
    ///     Extracts the features of a target word. The context is limited to the sentence segment the caller passes in.
    /// </summary>
    /// <param name="lower">The lower-case tokens of the segment.</param>
    /// <param name="index">The index of the target in <paramref name="lower"/>.</param>
    /// <param name="features">Receives the features; at least <see cref="MaxFeatures"/> long.</param>
    /// <returns>The number of features written. Window features are the distinct tokens that contain a letter within
    /// <see cref="WindowRadius"/> positions of the target, excluding the target itself; the immediate neighbors are
    /// also written as left and right features, whatever they are.</returns>
    public static int Extract(ReadOnlySpan<string> lower, int index, Span<FeatureRef> features)
    {
        var count = 0;
        var start = Math.Max(0, index - WindowRadius);
        var end = Math.Min(lower.Length - 1, index + WindowRadius);
        for (var i = start; i <= end; i++)
        {
            if (i == index || !HasLetter(lower[i]))
            {
                continue;
            }

            var duplicate = false;
            for (var j = 0; j < count && !duplicate; j++)
            {
                duplicate = features[j].Kind == Window && string.Equals(features[j].Token, lower[i], StringComparison.Ordinal);
            }

            if (!duplicate)
            {
                features[count++] = new FeatureRef(Window, lower[i]);
            }
        }

        if (index > 0)
        {
            features[count++] = new FeatureRef(Left, lower[index - 1]);
        }

        if (index + 1 < lower.Length)
        {
            features[count++] = new FeatureRef(Right, lower[index + 1]);
        }

        return count;
    }

    /// <summary>
    ///     The kind of a tense feature found anywhere to the left of the target within the window.
    /// </summary>
    public const int LeftWindow = 3;

    /// <summary>
    ///     The kind of a tense feature found anywhere to the right of the target within the window.
    /// </summary>
    public const int RightWindow = 4;

    /// <summary>
    ///     The kind of the tense feature that is the nearest function word to the left of the target that carries the tense
    ///     of a verb phrase (see <see cref="IsGovernor"/>).
    /// </summary>
    public const int Governor = 5;

    /// <summary>
    ///     The token that stands for the edge of the sentence segment, as the left neighbor of its first word and the right
    ///     neighbor of its last word, in the features of the tense model.
    /// </summary>
    public const string SegmentEdge = "<edge>";

    /// <summary>
    ///     The number of tokens to the left of the target in which the governing function word is looked for.
    /// </summary>
    public const int GovernorRadius = 6;

    /// <summary>
    ///     The largest number of tense features of one target: the cue features, the distinct tokens of the left and right
    ///     window and the governing function word.
    /// </summary>
    public const int MaxTenseFeatures = MaxFeatures + (2 * WindowRadius) + 1;

    private static readonly FrozenSet<string> Governors = new[]
    {
        "be", "am", "is", "are", "was", "were", "been", "being", "have", "has", "had", "having", "do", "does", "did",
        "will", "would", "can", "could", "shall", "should", "may", "might", "must", "to", "not", "cannot",
        "n't", "don't", "doesn't", "didn't", "won't", "wouldn't", "can't", "couldn't", "shouldn't", "haven't", "hasn't", "hadn't",
        "isn't", "wasn't", "aren't", "weren't", "'m", "'s", "'re", "'ve", "'d", "'ll",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    ///     Extracts the features of the tense model for a target word: the cue features (<see cref="Extract"/>), the same
    ///     tokens told apart by the side of the target they stand on, and the governing function word. The target itself
    ///     is never a feature, so the model reads only the context and cannot tell one verb from another.
    /// </summary>
    /// <param name="lower">The lower-case tokens of the segment.</param>
    /// <param name="index">The index of the target in <paramref name="lower"/>.</param>
    /// <param name="features">Receives the features; at least <see cref="MaxTenseFeatures"/> long.</param>
    /// <returns>The number of features written: those of <see cref="Extract"/>, the segment edge as the left neighbor of the
    /// first word of the segment (no token with a letter before the target) or the right neighbor of its last word (no token
    /// with a letter after the target), then the distinct tokens that contain a
    /// letter within <see cref="WindowRadius"/> positions to the left (<see cref="LeftWindow"/>) and to the right
    /// (<see cref="RightWindow"/>) of the target, then the nearest token to the left within <see cref="GovernorRadius"/>
    /// positions for which <see cref="IsGovernor"/> holds (<see cref="Governor"/>), if there is one.</returns>
    public static int ExtractTense(ReadOnlySpan<string> lower, int index, Span<FeatureRef> features)
    {
        var count = Extract(lower, index, features);

        // The edge of the segment is context too: the first word of a sentence is mostly an imperative or a past, never
        // governed by an auxiliary, and cue features alone say nothing when there is no neighbor to name. The first
        // word is the first token with a letter and the last word the last one, so a quotation mark or the closing
        // punctuation of the sentence does not hide the edge
        if (!HasWord(lower, 0, index))
        {
            features[count++] = new FeatureRef(Left, SegmentEdge);
        }

        if (!HasWord(lower, index + 1, lower.Length))
        {
            features[count++] = new FeatureRef(Right, SegmentEdge);
        }

        var windowEnd = count;
        var start = Math.Max(0, index - WindowRadius);
        var end = Math.Min(lower.Length - 1, index + WindowRadius);
        for (var i = start; i <= end; i++)
        {
            if (i == index || !HasLetter(lower[i]))
            {
                continue;
            }

            var kind = i < index ? LeftWindow : RightWindow;
            var duplicate = false;
            for (var j = windowEnd; j < count && !duplicate; j++)
            {
                duplicate = features[j].Kind == kind && string.Equals(features[j].Token, lower[i], StringComparison.Ordinal);
            }

            if (!duplicate)
            {
                features[count++] = new FeatureRef(kind, lower[i]);
            }
        }

        for (var i = index - 1; i >= Math.Max(0, index - GovernorRadius); i--)
        {
            if (IsGovernor(lower[i]))
            {
                features[count++] = new FeatureRef(Governor, lower[i]);
                break;
            }
        }

        return count;
    }

    /// <summary>
    ///     Tells whether a token is one of the function words that carry the tense of a verb phrase: a form of
    ///     <c>be</c>, <c>have</c> or <c>do</c>, a modal, the infinitive marker <c>to</c>, a negation, or a contraction
    ///     of them. It is a closed class of English grammar, not a list of verbs: the tense model learns from the
    ///     training text what each of them says about the verb that follows.
    /// </summary>
    /// <param name="token">The lower-case token.</param>
    /// <returns>True for a function word of the closed class.</returns>
    public static bool IsGovernor(string token) => Governors.Contains(token);

    /// <summary>
    ///     Scores how likely the target word is a past form of a verb rather than a non-past one, from its context.
    /// </summary>
    /// <param name="cue">The tense table.</param>
    /// <param name="features">The tense features of the target (see <see cref="ExtractTense"/>).</param>
    /// <returns>The log odds of past against non-past (the prior plus the weights of the features that the table knows); positive when the context says past.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="cue"/> is null.</exception>
    public static double ScoreTense(TenseCue cue, ReadOnlySpan<FeatureRef> features)
    {
        ArgumentNullException.ThrowIfNull(cue);
        var hashes = cue.Hashes.AsSpan();
        var weights = cue.Weights.AsSpan();
        int sum = cue.Prior;
        foreach (var feature in features)
        {
            var row = hashes.BinarySearch(feature.Hash);
            if (row >= 0)
            {
                sum += weights[row];
            }
        }

        return sum * Quantum;
    }

    /// <summary>
    ///     Scores the candidate analyses of a word.
    /// </summary>
    /// <param name="cue">The cue table of the word or of its lemma.</param>
    /// <param name="features">The features of the target.</param>
    /// <param name="analyses">The candidate analyses (lemma, part of speech and features), each matched to its column with <see cref="LemmaCue.RowOf"/>.</param>
    /// <param name="scores">Receives one log score per candidate (the prior plus the weights of the target's features); <see cref="double.NaN"/> for a candidate the table has no column for.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="cue"/> is null or <paramref name="analyses"/> is the default array.</exception>
    public static void Score(LemmaCue cue, ReadOnlySpan<FeatureRef> features, ImmutableArray<WordDetails> analyses, Span<double> scores)
    {
        ArgumentNullException.ThrowIfNull(cue);
        if (analyses.IsDefault)
        {
            throw new ArgumentNullException(nameof(analyses));
        }
        // Read-only spans over the immutable arrays keep the inner loop free of per-access checks
        var hashes = cue.Hashes.AsSpan();
        var weights = cue.Weights.AsSpan();
        Span<int> rows = features.Length <= MaxFeatures ? stackalloc int[MaxFeatures] : new int[features.Length];
        var found = 0;
        foreach (var feature in features)
        {
            var row = hashes.BinarySearch(feature.Hash);
            if (row >= 0)
            {
                rows[found++] = row;
            }
        }

        var width = cue.Analyses.Length;
        for (var j = 0; j < analyses.Length; j++)
        {
            var column = cue.RowOf(analyses[j]);
            if (column < 0)
            {
                scores[j] = double.NaN;
                continue;
            }

            int sum = cue.Analyses[column].Prior;
            for (var r = 0; r < found; r++)
            {
                sum += weights[(rows[r] * width) + column];
            }

            scores[j] = sum * Quantum;
        }
    }

    /// <summary>
    ///     Gets whether a token contains at least one letter.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns>True when a character is a letter.</returns>
    public static bool HasLetter(string token)
    {
        return token.Any(char.IsLetter);
    }

    /// <summary>
    ///     Tells whether a part of a segment has a word, a token with at least one letter.
    /// </summary>
    /// <param name="lower">The lower-case tokens of the segment.</param>
    /// <param name="start">The index of the first token to look at.</param>
    /// <param name="end">The index after the last token to look at.</param>
    /// <returns>True when a token from <paramref name="start"/> up to <paramref name="end"/> has a letter.</returns>
    private static bool HasWord(ReadOnlySpan<string> lower, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (HasLetter(lower[i]))
            {
                return true;
            }
        }

        return false;
    }
}
