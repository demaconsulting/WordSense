using System.Numerics;

namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     Small vector helpers used by scoring and feature extraction.
/// </summary>
/// <remarks>
///     Uses <see cref="Vector{T}"/> for hardware acceleration without any extra package dependency.
///     All methods are pure and thread-safe.
/// </remarks>
internal static class VectorMath
{
    /// <summary>
    ///     Computes the dot product of two vectors over the length of the first one.
    /// </summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector; at least as long as <paramref name="left"/>.</param>
    /// <returns>The sum of element-wise products over the length of <paramref name="left"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="right"/> is shorter than <paramref name="left"/>.</exception>
    public static float Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var length = left.Length;

        // Refuse a short second vector up front: the SIMD and the scalar loop would otherwise fail differently
        if (right.Length < length)
        {
            throw new ArgumentException("The second vector must be at least as long as the first.", nameof(right));
        }

        var sum = 0f;
        var i = 0;

        // Process full SIMD lanes, then finish the remainder scalar
        if (Vector.IsHardwareAccelerated && length >= Vector<float>.Count)
        {
            var accumulator = Vector<float>.Zero;
            var lastBlock = length - Vector<float>.Count;
            for (; i <= lastBlock; i += Vector<float>.Count)
            {
                accumulator += new Vector<float>(left.Slice(i)) * new Vector<float>(right.Slice(i));
            }

            sum = Vector.Sum(accumulator);
        }

        for (; i < length; i++)
        {
            sum += left[i] * right[i];
        }

        return sum;
    }
}
