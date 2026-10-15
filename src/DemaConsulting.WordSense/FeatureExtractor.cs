namespace DemaConsulting.WordSense;

/// <summary>
///     Computes the classifier feature of a target word from encoder hidden states.
/// </summary>
/// <remarks>
///     The feature is the mean of the hidden states of the target's WordPiece tokens, minus the anchor
///     mean stored in the table, scaled to unit length. The classifier and the table builder share this
///     one implementation so training and inference can never diverge.
/// </remarks>
internal static class FeatureExtractor
{
    /// <summary>
    ///     Computes the feature of the tokens <c>[first, end)</c> of an encoded sequence.
    /// </summary>
    /// <param name="hidden">Row-major hidden states, one row of <paramref name="dimension"/> values per token.</param>
    /// <param name="dimension">The size of each hidden state.</param>
    /// <param name="first">First target row.</param>
    /// <param name="end">Exclusive end target row.</param>
    /// <param name="mean">The anchor mean to subtract, or null to subtract nothing.</param>
    /// <returns>The unit-length feature, or null when the range is empty or the feature has no magnitude.</returns>
    public static float[]? Extract(ReadOnlySpan<float> hidden, int dimension, int first, int end, ReadOnlySpan<float> mean)
    {
        var rows = hidden.Length / dimension;
        if (first < 0 || end > rows || end <= first)
        {
            return null;
        }

        // Average the target rows in double to keep the result independent of summation order effects
        var sums = new double[dimension];
        for (var row = first; row < end; row++)
        {
            var slice = hidden.Slice(row * dimension, dimension);
            for (var d = 0; d < dimension; d++)
            {
                sums[d] += slice[d];
            }
        }

        var count = end - first;
        var feature = new float[dimension];
        double norm = 0;
        for (var d = 0; d < dimension; d++)
        {
            var value = (sums[d] / count) - (mean.IsEmpty ? 0 : mean[d]);
            feature[d] = (float)value;
            norm += value * value;
        }

        if (norm <= 0 || double.IsNaN(norm))
        {
            return null;
        }

        var scale = (float)(1.0 / Math.Sqrt(norm));
        for (var d = 0; d < dimension; d++)
        {
            feature[d] *= scale;
        }

        return feature;
    }
}
