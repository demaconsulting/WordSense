namespace WordSense.Builder;

/// <summary>
///     The method that fits the one-versus-rest classifiers of a lemma.
/// </summary>
internal enum FitSolver
{
    /// <summary>
    ///     Newton's method in the span of the training samples (see <see cref="KernelBasis"/>) when there are fewer
    ///     samples than features, and in the feature space otherwise. It finds the same optimum as <see cref="Primal"/>.
    /// </summary>
    Kernel = 0,

    /// <summary>
    ///     Newton's method in the feature space, whatever the number of samples: the reference solver, much slower when
    ///     there are fewer samples than features.
    /// </summary>
    Primal = 1,
}

/// <summary>
///     Fits the one-versus-rest logistic regressions of the anchors of one lemma, or of a subset of them (a
///     cross-validation fold).
/// </summary>
/// <remarks>
///     The Gram matrix of the features is computed once, on first use, and shared by every subset and every class, so the
///     cross-validation folds and the final fit of a lemma pay for it once. The classes (the one-versus-rest rows) are
///     fitted concurrently, each into its own slice of the result, so the result does not depend on the number of
///     threads. The members are thread-safe: the folds of one lemma may use one instance at the same time.
/// </remarks>
internal sealed class LemmaFitter
{
    private readonly float[][] _features;
    private readonly FitSolver _solver;
    private readonly int _parallelism;
    private readonly Lazy<double[][]> _gram;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LemmaFitter"/> class.
    /// </summary>
    /// <param name="features">The features of all the anchors of the lemma.</param>
    /// <param name="solver">The solver.</param>
    /// <param name="parallelism">The number of classes fitted concurrently.</param>
    public LemmaFitter(float[][] features, FitSolver solver, int parallelism = 1)
    {
        _features = features ?? throw new ArgumentNullException(nameof(features));
        _solver = solver;
        _parallelism = Math.Max(1, parallelism);
        _gram = new Lazy<double[][]>(() => KernelBasis.Gram(features), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    ///     Fits one row per sense on the anchors of a subset.
    /// </summary>
    /// <param name="members">The indexes of the anchors that train the fit.</param>
    /// <param name="labels">The sense (row) of every anchor, indexed like the features.</param>
    /// <param name="senseCount">The number of senses.</param>
    /// <param name="c">The inverse regularization strength.</param>
    /// <returns>Row-major weights (one row per sense) and one bias per row.</returns>
    public (float[] Weights, float[] Bias) Fit(IReadOnlyList<int> members, int[] labels, int senseCount, double c)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(labels);
        var dim = _features[0].Length;
        var weights = new float[senseCount * dim];
        var bias = new float[senseCount];
        var kernel = _solver == FitSolver.Kernel && members.Count < dim;
        var basis = kernel ? KernelBasis.Create(_gram.Value, members) : null;
        var subset = kernel ? null : members.Select(m => _features[m]).ToArray();
        Parallel.For(0, senseCount, new ParallelOptions { MaxDegreeOfParallelism = _parallelism }, row =>
        {
            var positive = new bool[members.Count];
            for (var k = 0; k < positive.Length; k++)
            {
                positive[k] = labels[members[k]] == row;
            }

            if (basis is not null)
            {
                var (coefficients, b) = LogisticTrainer.TrainRows(basis.Rows, positive, c);
                Combine(members, basis.Expand(coefficients), weights.AsSpan(row * dim, dim));
                bias[row] = (float)b;
            }
            else
            {
                var (w, b) = LogisticTrainer.Train(subset!, positive, c);
                for (var d = 0; d < dim; d++)
                {
                    weights[(row * dim) + d] = (float)w[d];
                }

                bias[row] = (float)b;
            }
        });

        return (weights, bias);
    }

    /// <summary>
    ///     Writes the weight vector <c>Σ βᵢ·xᵢ</c>, rounded to single precision.
    /// </summary>
    /// <param name="members">The anchors of the fit.</param>
    /// <param name="beta">The coefficient of each member.</param>
    /// <param name="target">Receives the weights of one row.</param>
    private void Combine(IReadOnlyList<int> members, double[] beta, Span<float> target)
    {
        for (var d = 0; d < target.Length; d++)
        {
            double sum = 0;
            for (var k = 0; k < beta.Length; k++)
            {
                if (beta[k] != 0)
                {
                    sum += beta[k] * _features[members[k]][d];
                }
            }

            target[d] = (float)sum;
        }
    }
}
