namespace DemaConsulting.WordSense.Basic;

/// <summary>
///     One context feature of a target word: a neighboring token and where it was found.
/// </summary>
/// <param name="Kind">The kind of the feature: <see cref="CueModel.Window"/>, <see cref="CueModel.Left"/> or <see cref="CueModel.Right"/>.</param>
/// <param name="Token">The lower-case neighboring token.</param>
internal readonly record struct FeatureRef(int Kind, string Token)
{
    /// <summary>
    ///     Gets the 16-bit hash of the feature: a 32-bit FNV-1a over the UTF-16 code units of its name (the token, with
    ///     the prefix <c>L1:</c> or <c>R1:</c> for the immediate neighbors) folded by xor-ing its upper half into its lower half.
    /// </summary>
    public ushort Hash
    {
        get
        {
            var h = CueModel.OffsetBasis;
            if (Kind != CueModel.Window)
            {
                h = Mix(h, Kind == CueModel.Left ? 'L' : 'R');
                h = Mix(h, '1');
                h = Mix(h, ':');
            }

            foreach (var ch in Token)
            {
                h = Mix(h, ch);
            }

            return (ushort)((h ^ (h >> 16)) & 0xFFFF);
        }
    }

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
///     The context cue model: extracts the context features of a target word and scores the senses of its lemma from
///     the lemma's table of hashed features, a naive Bayes classifier with additive smoothing whose log weights are
///     quantized to eighths of a nat.
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
    ///     Scores the candidate senses of a word.
    /// </summary>
    /// <param name="cue">The cue table of the lemma.</param>
    /// <param name="features">The features of the target.</param>
    /// <param name="senses">The candidate senses.</param>
    /// <param name="scores">Receives one log score per candidate (the prior plus the weights of the target's features); <see cref="double.NaN"/> for a candidate the table does not have.</param>
    public static void Score(LemmaCue cue, ReadOnlySpan<FeatureRef> features, WordDetails[] senses, Span<double> scores)
    {
        ArgumentNullException.ThrowIfNull(cue);
        ArgumentNullException.ThrowIfNull(senses);
        Span<int> rows = features.Length <= MaxFeatures ? stackalloc int[MaxFeatures] : new int[features.Length];
        var found = 0;
        foreach (var feature in features)
        {
            var row = Array.BinarySearch(cue.Hashes, feature.Hash);
            if (row >= 0)
            {
                rows[found++] = row;
            }
        }

        var width = cue.Senses.Length;
        for (var j = 0; j < senses.Length; j++)
        {
            var column = cue.RowOf(senses[j]);
            if (column < 0)
            {
                scores[j] = double.NaN;
                continue;
            }

            var sum = cue.Senses[column].Prior;
            for (var r = 0; r < found; r++)
            {
                sum += cue.Weights[(rows[r] * width) + column];
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
        foreach (var ch in token)
        {
            if (char.IsLetter(ch))
            {
                return true;
            }
        }

        return false;
    }
}
