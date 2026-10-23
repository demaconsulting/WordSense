using System.Numerics;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests of <see cref="VectorMath"/>: the dot product equals the plain sum of products for every length, whether the
///     SIMD path, the scalar remainder or both are used.
/// </summary>
public class VectorMathTests
{
    /// <summary>
    ///     For lengths below, at and around one and several SIMD widths (and the real dimensions of the encoders) the
    ///     dot product equals the scalar sum. The values are small integers, so every sum is exact in single precision.
    /// </summary>
    [Fact]
    public void VectorMath_Dot_LengthsAroundTheSimdWidth_EqualTheScalarSum()
    {
        // Arrange: the lanes of one SIMD register decide where the remainder starts
        var lanes = Vector<float>.Count;
        int[] lengths =
        [
            1, lanes - 1, lanes, lanes + 1, (2 * lanes) - 1, 2 * lanes, (2 * lanes) + 1, (3 * lanes) + 3, 384, 385, 768,
        ];

        foreach (var length in lengths.Where(l => l > 0))
        {
            var left = Enumerable.Range(0, length).Select(i => (float)((i % 7) + 1)).ToArray();
            var right = Enumerable.Range(0, length).Select(i => (float)((i % 5) - 2)).ToArray();
            double expected = 0;
            for (var i = 0; i < length; i++)
            {
                expected += (double)left[i] * right[i];
            }

            // Act
            var actual = VectorMath.Dot(left, right);

            // Assert
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    ///     The dot product runs over the length of the first vector; extra values at the end of the second one are ignored.
    /// </summary>
    [Fact]
    public void VectorMath_Dot_ShorterFirstVector_UsesItsLength()
    {
        // Arrange: the second vector is longer than the first, with large values beyond the first vector's length
        var length = (2 * Vector<float>.Count) + 1;
        var left = Enumerable.Repeat(2f, length).ToArray();
        var right = Enumerable.Repeat(3f, length + 5).ToArray();
        right.AsSpan(length).Fill(1000f);

        // Act
        var actual = VectorMath.Dot(left, right);

        // Assert
        Assert.Equal(6f * length, actual);
    }

    /// <summary>
    ///     Two empty vectors have the dot product zero.
    /// </summary>
    [Fact]
    public void VectorMath_Dot_EmptyVectors_IsZero()
    {
        // Act
        var actual = VectorMath.Dot([], []);

        // Assert
        Assert.Equal(0f, actual);
    }

    /// <summary>
    ///     A second vector that is shorter than the first is rejected, for a length with and without a SIMD block.
    /// </summary>
    /// <param name="extra">How many values the first vector has more than the second one, in SIMD widths (0 for one value).</param>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void VectorMath_Dot_RightShorterThanLeft_Throws(int extra)
    {
        // Arrange
        var length = (extra * Vector<float>.Count) + 1;
        var left = new float[length + 1];
        var right = new float[length];

        // Act / Assert
        var error = Assert.Throws<ArgumentException>(() => VectorMath.Dot(left, right));
        Assert.Equal("right", error.ParamName);
    }
}
