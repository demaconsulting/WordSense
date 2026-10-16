namespace DemaConsulting.WordSense;

/// <summary>
///     Computes the classifier feature of a target word from a pooled encoder vector.
/// </summary>
/// <remarks>
///     The feature is the pooled vector minus the anchor mean stored in the weights table, scaled to unit length.
///     The classifier and the table builder share this one implementation so training and inference can never diverge.
/// </remarks>
internal static class FeatureExtractor
{
    /// <summary>
    ///     Centers a pooled vector on the anchor mean and scales it to unit length.
    /// </summary>
    /// <param name="pooled">The pooled vector.</param>
    /// <param name="mean">The anchor mean to subtract, or empty to subtract nothing.</param>
    /// <returns>The unit-length feature, or null when it has no magnitude.</returns>
    public static float[]? Center(double[] pooled, ReadOnlySpan<float> mean)
    {
        var dimension = pooled.Length;

        // Center on the anchor mean and scale to unit length
        var feature = new float[dimension];
        double norm = 0;
        for (var d = 0; d < dimension; d++)
        {
            var value = pooled[d] - (mean.IsEmpty ? 0 : mean[d]);
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
