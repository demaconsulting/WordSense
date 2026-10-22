namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests of <see cref="FeatureExtractor"/>: the feature of a target word is its pooled vector centered on the global
///     mean and scaled to unit length, and a vector without magnitude has no feature.
/// </summary>
public class FeatureExtractorTests
{
    private const double Tolerance = 1e-6;

    /// <summary>
    ///     The mean is subtracted and the result is scaled to unit length.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Center_Vector_SubtractsTheMeanAndScalesToUnitLength()
    {
        // Arrange: the centered vector is (3, 4), whose length is 5
        double[] pooled = [4, 6];
        float[] mean = [1, 2];

        // Act
        var feature = FeatureExtractor.Center(pooled, mean);

        // Assert
        Assert.NotNull(feature);
        Assert.Equal(0.6, feature[0], Tolerance);
        Assert.Equal(0.8, feature[1], Tolerance);
        Assert.Equal(1.0, feature.Sum(v => (double)v * v), Tolerance);
    }

    /// <summary>
    ///     An empty mean subtracts nothing, so the feature is the normalized pooled vector, and the input is not changed.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Center_EmptyMean_ScalesThePooledVector()
    {
        // Arrange
        double[] pooled = [0, 3, 0, 4];

        // Act
        var feature = FeatureExtractor.Center(pooled, []);

        // Assert
        Assert.NotNull(feature);
        Assert.Equal(4, feature.Length);
        Assert.Equal(0.0, feature[0], Tolerance);
        Assert.Equal(0.6, feature[1], Tolerance);
        Assert.Equal(0.0, feature[2], Tolerance);
        Assert.Equal(0.8, feature[3], Tolerance);
        Assert.Equal([0d, 3d, 0d, 4d], pooled);
    }

    /// <summary>
    ///     A pooled vector that equals the mean has no magnitude after centering, so it has no feature.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Center_VectorEqualToMean_ReturnsNull()
    {
        // Arrange
        double[] pooled = [1, 2, 3];
        float[] mean = [1, 2, 3];

        // Act
        var feature = FeatureExtractor.Center(pooled, mean);

        // Assert
        Assert.Null(feature);
    }

    /// <summary>
    ///     A vector with a NaN or infinite value (or a mean with one) has no feature, never a NaN feature.
    /// </summary>
    /// <param name="bad">The non-finite value to put into the pooled vector.</param>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FeatureExtractor_Center_NonFiniteValue_ReturnsNull(double bad)
    {
        // Arrange
        double[] pooled = [1, bad, 3];
        float[] mean = [0, 0, 0];
        double[] finite = [1, 2, 3];
        float[] badMean = [0, float.NaN, 0];

        // Act
        var fromPooled = FeatureExtractor.Center(pooled, mean);
        var fromMean = FeatureExtractor.Center(finite, badMean);

        // Assert
        Assert.Null(fromPooled);
        Assert.Null(fromMean);
    }

    /// <summary>
    ///     A mean whose length differs from the pooled vector is rejected instead of being truncated or read out of range.
    /// </summary>
    /// <param name="meanLength">The length of the mean.</param>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void FeatureExtractor_Center_MeanOfOtherDimension_Throws(int meanLength)
    {
        // Arrange
        double[] pooled = [1, 2, 3];
        var mean = new float[meanLength];

        // Act / Assert
        var error = Assert.Throws<ArgumentException>(() => FeatureExtractor.Center(pooled, mean));
        Assert.Equal("mean", error.ParamName);
    }

    /// <summary>
    ///     A null pooled vector is rejected.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Center_NullVector_Throws()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => FeatureExtractor.Center(null!, []));
    }

    /// <summary>
    ///     A vector without any value has no magnitude, so it has no feature.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Center_EmptyVector_ReturnsNull()
    {
        // Act
        var feature = FeatureExtractor.Center([], []);

        // Assert
        Assert.Null(feature);
    }
}
