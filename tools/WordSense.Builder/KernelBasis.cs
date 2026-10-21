namespace WordSense.Builder;

/// <summary>
///     The coordinates of a set of samples in an orthonormal basis of their span, which a linear model with an L2
///     penalty can be trained in instead of the (much larger) feature space.
/// </summary>
/// <remarks>
///     <para>
///     The weight vector of an L2-regularized linear model lies in the span of the training samples (the gradient of the
///     loss is a combination of them and the penalty is the squared length), so the model can be trained in the
///     coordinates of that span: the rows <c>x̃ᵢ</c> of this type have the same inner products as the samples
///     (<c>x̃ᵢ·x̃ⱼ = xᵢ·xⱼ</c>), which is all the loss and the penalty see. They come from the Cholesky factor of the Gram
///     matrix <c>K = L·Lᵀ</c> (the rows of <c>L</c>). A sample that is a combination of the earlier ones (a duplicate)
///     adds no coordinate. With fewer samples than features the number of coordinates is at most the number of samples,
///     which is what makes the fit fast.
///     </para>
///     <para>
///     <see cref="Expand"/> turns coefficients in these coordinates back into the combination of the samples
///     <c>w = Σ βᵢ·xᵢ</c> that is the same weight vector. Everything is computed in double precision in a fixed order,
///     so the result is deterministic.
///     </para>
/// </remarks>
internal sealed class KernelBasis
{
    private const double RankTolerance = 1e-9;

    private readonly double[][] _factor;
    private readonly int[] _pivots;
    private readonly int _members;

    private KernelBasis(double[][] rows, double[][] factor, int[] pivots, int members)
    {
        Rows = rows;
        _factor = factor;
        _pivots = pivots;
        _members = members;
    }

    /// <summary>
    ///     Gets the coordinates of each sample, one row per sample, all of the length <see cref="Dimension"/>.
    /// </summary>
    public double[][] Rows { get; }

    /// <summary>
    ///     Gets the number of coordinates: the rank of the samples.
    /// </summary>
    public int Dimension => _pivots.Length;

    /// <summary>
    ///     Computes the Gram matrix of a set of features.
    /// </summary>
    /// <param name="features">The features.</param>
    /// <returns>The lower triangle of the inner products (row <c>i</c> holds the products with the samples <c>0..i</c>), in double precision.</returns>
    public static double[][] Gram(float[][] features)
    {
        ArgumentNullException.ThrowIfNull(features);
        var gram = new double[features.Length][];
        for (var i = 0; i < features.Length; i++)
        {
            gram[i] = new double[i + 1];
            for (var j = 0; j <= i; j++)
            {
                double sum = 0;
                var a = features[i];
                var b = features[j];
                for (var d = 0; d < a.Length; d++)
                {
                    sum += (double)a[d] * b[d];
                }

                gram[i][j] = sum;
            }
        }

        return gram;
    }

    /// <summary>
    ///     Builds the basis of a subset of the samples from the Gram matrix of all of them.
    /// </summary>
    /// <param name="gram">The lower triangle of the Gram matrix of all samples (see <see cref="Gram"/>).</param>
    /// <param name="members">The indexes of the samples of the subset, in the order of the rows.</param>
    /// <returns>The basis.</returns>
    public static KernelBasis Create(double[][] gram, IReadOnlyList<int> members)
    {
        ArgumentNullException.ThrowIfNull(gram);
        ArgumentNullException.ThrowIfNull(members);
        var n = members.Count;
        var pivots = new List<int>();
        var factor = new double[n][];
        for (var j = 0; j < n; j++)
        {
            // The coordinates of sample j against the columns found so far
            var row = new double[n];
            for (var c = 0; c < pivots.Count; c++)
            {
                var pivot = pivots[c];
                var sum = Product(gram, members[j], members[pivot]);
                for (var k = 0; k < c; k++)
                {
                    sum -= row[k] * factor[pivot][k];
                }

                row[c] = sum / factor[pivot][c];
            }

            var remainder = Product(gram, members[j], members[j]);
            for (var c = 0; c < pivots.Count; c++)
            {
                remainder -= row[c] * row[c];
            }

            // A sample inside the span of the earlier ones adds no column
            if (remainder > RankTolerance)
            {
                row[pivots.Count] = Math.Sqrt(remainder);
                pivots.Add(j);
            }

            factor[j] = row;
        }

        var rank = pivots.Count;
        var rows = new double[n][];
        for (var j = 0; j < n; j++)
        {
            rows[j] = factor[j][..rank];
        }

        return new KernelBasis(rows, factor, [.. pivots], n);
    }

    /// <summary>
    ///     Turns coefficients in the coordinates of the basis into the combination of the samples with the same weight vector.
    /// </summary>
    /// <param name="coefficients">One coefficient per coordinate, <see cref="Dimension"/> numbers.</param>
    /// <returns>The coefficient <c>β</c> of every sample (zero for a sample that adds no coordinate), so that the weight vector is <c>Σ βᵢ·xᵢ</c>.</returns>
    public double[] Expand(double[] coefficients)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        var rank = _pivots.Length;
        var beta = new double[_members];

        // The pivot samples have a lower triangular matrix of coordinates; solve its transpose against the coefficients
        for (var c = rank - 1; c >= 0; c--)
        {
            var sum = coefficients[c];
            for (var later = c + 1; later < rank; later++)
            {
                sum -= _factor[_pivots[later]][c] * beta[_pivots[later]];
            }

            beta[_pivots[c]] = sum / _factor[_pivots[c]][c];
        }

        return beta;
    }

    private static double Product(double[][] gram, int a, int b) => a >= b ? gram[a][b] : gram[b][a];
}
