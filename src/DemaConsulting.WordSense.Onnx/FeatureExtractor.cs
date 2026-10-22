namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     Computes the classifier feature of a target word from a pooled encoder vector.
/// </summary>
/// <remarks>
///     The feature is the pooled vector minus the global mean vector stored in the weights, scaled to unit length.
///     The classifier and the weights builder share this one implementation so training and inference can never diverge.
/// </remarks>
internal static class FeatureExtractor
{
    /// <summary>
    ///     Centers a pooled vector on the global mean and scales it to unit length.
    /// </summary>
    /// <param name="pooled">The pooled vector.</param>
    /// <param name="mean">The global mean to subtract, or empty to subtract nothing; otherwise it must have the length of <paramref name="pooled"/>.</param>
    /// <returns>The unit-length feature, or null when it has no magnitude (a zero vector after centering, or a vector with a non-finite value).</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pooled"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="mean"/> is not empty and its length differs from the length of <paramref name="pooled"/>.</exception>
    public static float[]? Center(double[] pooled, ReadOnlySpan<float> mean)
    {
        ArgumentNullException.ThrowIfNull(pooled);
        var dimension = pooled.Length;

        // A mean of another dimension would be silently truncated or read out of range: refuse it explicitly
        if (!mean.IsEmpty && mean.Length != dimension)
        {
            throw new ArgumentException("The mean must be empty or have the dimension of the pooled vector.", nameof(mean));
        }

        // Center on the mean and scale to unit length
        var feature = new float[dimension];
        double norm = 0;
        for (var d = 0; d < dimension; d++)
        {
            var value = pooled[d] - (mean.IsEmpty ? 0 : mean[d]);
            feature[d] = (float)value;
            norm += value * value;
        }

        if (norm <= 0 || !double.IsFinite(norm))
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
