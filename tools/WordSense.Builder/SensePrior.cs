using System.Globalization;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     The settings of the usage prior (see <see cref="SensePrior"/>).
/// </summary>
/// <param name="Alpha">The exponent that tempers the usage share of a sense; 0 divides out the training share only.</param>
/// <param name="Smoothing">The constant added to the tag count of every sense of a lemma (add-k smoothing).</param>
internal sealed record PriorSettings(double Alpha = PriorSettings.DefaultAlpha, double Smoothing = PriorSettings.DefaultSmoothing)
{
    /// <summary>
    ///     The default exponent: a tempered prior, because the counts are small and come from another genre.
    /// </summary>
    public const double DefaultAlpha = 0.5;

    /// <summary>
    ///     The default smoothing constant.
    /// </summary>
    public const double DefaultSmoothing = 0.5;

    /// <summary>
    ///     Checks the settings.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the exponent is negative or not finite, or the smoothing constant is not positive.</exception>
    public void Validate()
    {
        if (!double.IsFinite(Alpha) || Alpha < 0)
        {
            throw new ArgumentException($"The prior exponent must be a number that is not negative, not {Alpha.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (!double.IsFinite(Smoothing) || Smoothing <= 0)
        {
            throw new ArgumentException($"The prior smoothing constant must be a positive number, not {Smoothing.ToString(CultureInfo.InvariantCulture)}.");
        }
    }
}

/// <summary>
///     The usage the prior is computed from: the WordNet 3.0 tag count of each sense key, and the settings.
/// </summary>
/// <param name="TagCounts">The tag count of every sense key of the sense-frequency table.</param>
/// <param name="Settings">The settings of the prior.</param>
internal sealed record SenseUsage(IReadOnlyDictionary<string, int> TagCounts, PriorSettings Settings);

/// <summary>
///     The usage prior of the per-sense scores: the correction from the proportions of the training sentences to the
///     proportions of real use.
/// </summary>
/// <remarks>
///     <para>
///     The classifiers of a lemma are fitted on anchors that are balanced by sense quota, so their probabilities carry
///     the share of each sense among the anchors, <c>q(s)</c>. The correction is
///     <c>p'(s|x) ~ p(s|x) / q(s) * pi(s)^alpha</c> over the senses of the lemma that have a column, where
///     <c>pi(s)</c> is the usage share of the sense: its WordNet 3.0 tag count plus the smoothing constant <c>k</c>,
///     divided by the sum of that over the senses. A sense without a count (a project sense has no sense key, and the
///     table may not list a key) gets the constant alone. The scores are one-versus-rest log-odds whose softmax is the
///     probability, so the correction is the additive offset <c>-ln q(s) + alpha ln pi(s)</c> on each score, which
///     the trainer folds into the stored biases: the weights keep their size and the run time keeps its code.
///     </para>
///     <para>
///     The functions are deterministic and free of side effects.
///     </para>
/// </remarks>
internal static class SensePrior
{
    /// <summary>
    ///     Reads the tag counts of the senses from the sense-frequency table of the data folder.
    /// </summary>
    /// <param name="data">The data directory.</param>
    /// <returns>The tag count of every sense key.</returns>
    /// <exception cref="InvalidDataException">Thrown when the table is missing or empty: a prior without counts would silently reduce to the training share.</exception>
    public static IReadOnlyDictionary<string, int> ReadCounts(string data)
    {
        var counts = Tiers.ReadTagCounts(data);
        return counts.Count == 0
            ? throw new InvalidDataException($"The sense-frequency table of '{data}' has no counts, so the usage prior cannot be computed; give --no-prior to train without it.")
            : counts;
    }

    /// <summary>
    ///     Computes the additive offset of every column of a lemma.
    /// </summary>
    /// <param name="senses">The senses of the columns, in column order.</param>
    /// <param name="anchorCounts">The number of anchors of each column; every column has at least one.</param>
    /// <param name="usage">The tag counts and the settings.</param>
    /// <returns>The offset of each column: <c>-ln q + alpha ln pi</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when the arrays disagree in length or a column has no anchor.</exception>
    public static double[] Offsets(IReadOnlyList<SenseInfo> senses, IReadOnlyList<int> anchorCounts, SenseUsage usage)
    {
        ArgumentNullException.ThrowIfNull(senses);
        ArgumentNullException.ThrowIfNull(anchorCounts);
        ArgumentNullException.ThrowIfNull(usage);
        var tagCounts = senses.Select(s => s.SenseKey is { } key && usage.TagCounts.TryGetValue(key, out var count) ? count : 0).ToArray();
        return Offsets(anchorCounts, tagCounts, usage.Settings);
    }

    /// <summary>
    ///     Computes the additive offset of every column from the anchor counts and the tag counts.
    /// </summary>
    /// <param name="anchorCounts">The number of anchors of each column; every column has at least one.</param>
    /// <param name="tagCounts">The tag count of each column, 0 for a sense without a count.</param>
    /// <param name="settings">The settings.</param>
    /// <returns>The offset of each column: <c>-ln q + alpha ln pi</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when the arrays disagree in length, a column has no anchor or a tag count is negative.</exception>
    public static double[] Offsets(IReadOnlyList<int> anchorCounts, IReadOnlyList<int> tagCounts, PriorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(anchorCounts);
        ArgumentNullException.ThrowIfNull(tagCounts);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        if (anchorCounts.Count != tagCounts.Count || anchorCounts.Count == 0)
        {
            throw new ArgumentException("There must be one anchor count and one tag count per column.", nameof(tagCounts));
        }

        if (anchorCounts.Any(c => c < 1) || tagCounts.Any(c => c < 0))
        {
            throw new ArgumentException("Every column needs at least one anchor and no tag count may be negative.", nameof(anchorCounts));
        }

        double anchorTotal = anchorCounts.Sum();
        var usageTotal = tagCounts.Sum(c => c + settings.Smoothing);
        var offsets = new double[anchorCounts.Count];
        for (var i = 0; i < offsets.Length; i++)
        {
            var q = anchorCounts[i] / anchorTotal;
            var pi = (tagCounts[i] + settings.Smoothing) / usageTotal;
            offsets[i] = -Math.Log(q) + (settings.Alpha * Math.Log(pi));
        }

        return offsets;
    }

    /// <summary>
    ///     Adds the offsets to the biases of a fit.
    /// </summary>
    /// <param name="bias">The fitted biases, one per column.</param>
    /// <param name="offsets">The offsets, or null for no prior.</param>
    /// <returns>A new array with the corrected biases, or a copy of the biases without a prior.</returns>
    /// <exception cref="ArgumentException">Thrown when the offsets do not have one number per bias.</exception>
    public static float[] Apply(float[] bias, IReadOnlyList<double>? offsets)
    {
        ArgumentNullException.ThrowIfNull(bias);
        var result = (float[])bias.Clone();
        if (offsets is null)
        {
            return result;
        }

        if (offsets.Count != bias.Length)
        {
            throw new ArgumentException("There must be one offset per bias.", nameof(offsets));
        }

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (float)(result[i] + offsets[i]);
        }

        return result;
    }
}
