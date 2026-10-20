using System.Numerics;

namespace WordSense.Builder;

/// <summary>
///     Trains an L2-regularized binary logistic regression with a deterministic Newton method.
/// </summary>
/// <remarks>
///     <para>
///     Minimizes <c>0.5·|w|² + C·Σ log(1 + exp(−y·(w·x + b)))</c> with an unregularized bias. The method
///     starts from zero, uses fixed iteration rules and a backtracking line search, and works in double
///     precision, so the same input always produces the same weights on the same platform. Every operation that adds
///     up numbers (a dot product, a sum over the samples) adds them in a fixed scalar order; only element-wise updates
///     are vectorized, and they do not depend on the width of the vector unit, so the result does not depend on the
///     processor either.
///     </para>
///     <para>
///     <see cref="Train"/> solves the problem in the coordinates of the features (the primal form, the reference
///     solver). <see cref="TrainRows"/> runs the same Newton iteration on any rows: given the coordinates of the samples
///     in an orthonormal basis of their span (see <see cref="KernelBasis"/>), the iterates are those of the primal form
///     (the weights of a regularized linear model always lie in the span of the samples, and the span has at most as
///     many dimensions as there are samples), so when there are fewer samples than dimensions the cost of one iteration
///     falls from the cube of the dimension to the cube of the number of samples.
///     </para>
/// </remarks>
internal static class LogisticTrainer
{
    private const int MaxIterations = 60;
    private const double Tolerance = 1e-9;

    /// <summary>
    ///     Trains one binary classifier in the coordinates of the features.
    /// </summary>
    /// <param name="features">One feature vector per sample.</param>
    /// <param name="positive">Whether each sample belongs to the positive class.</param>
    /// <param name="c">The inverse regularization strength.</param>
    /// <returns>The weight vector and the bias.</returns>
    public static (double[] Weights, double Bias) Train(IReadOnlyList<float[]> features, IReadOnlyList<bool> positive, double c)
    {
        var rows = new double[features.Count][];
        for (var s = 0; s < rows.Length; s++)
        {
            rows[s] = Array.ConvertAll(features[s], f => (double)f);
        }

        return TrainRows(rows, positive, c);
    }

    /// <summary>
    ///     Trains one binary classifier on samples given in any coordinates.
    /// </summary>
    /// <param name="rows">One coordinate vector per sample, all of the same length.</param>
    /// <param name="positive">Whether each sample belongs to the positive class.</param>
    /// <param name="c">The inverse regularization strength.</param>
    /// <returns>The weight vector (one coefficient per coordinate) and the bias.</returns>
    public static (double[] Weights, double Bias) TrainRows(double[][] rows, IReadOnlyList<bool> positive, double c)
    {
        var n = rows.Length;
        var dim = rows[0].Length;
        var size = dim + 1;
        var theta = new double[size];
        var objective = Objective(rows, positive, theta, c);
        var hessian = new double[size * size];
        var gradient = new double[size];
        var candidate = new double[size];

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            GradientAndHessian(rows, positive, theta, c, n, dim, gradient, hessian);
            var step = SolveSpd(hessian, gradient, size);

            // Backtrack until the objective decreases
            var scale = 1.0;
            var candidateObjective = objective;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                for (var i = 0; i < size; i++)
                {
                    candidate[i] = theta[i] - (scale * step[i]);
                }

                candidateObjective = Objective(rows, positive, candidate, c);
                if (candidateObjective <= objective)
                {
                    break;
                }

                scale /= 2;
            }

            var change = Math.Abs(objective - candidateObjective);
            (theta, candidate) = (candidate, theta);
            objective = candidateObjective;
            if (change < Tolerance * Math.Max(1.0, Math.Abs(objective)))
            {
                break;
            }
        }

        return (theta[..dim], theta[dim]);
    }

    /// <summary>
    ///     Computes <c>w·x + b</c>.
    /// </summary>
    /// <param name="x">The feature vector.</param>
    /// <param name="theta">The weights followed by the bias.</param>
    /// <returns>The activation.</returns>
    public static double Activation(float[] x, double[] theta)
    {
        var sum = theta[^1];
        for (var i = 0; i < x.Length; i++)
        {
            sum += theta[i] * x[i];
        }

        return sum;
    }

    private static double Activation(double[] x, double[] theta)
    {
        var sum = theta[^1];
        for (var i = 0; i < x.Length; i++)
        {
            sum += theta[i] * x[i];
        }

        return sum;
    }

    /// <summary>
    ///     Evaluates the regularized objective.
    /// </summary>
    /// <param name="rows">The samples.</param>
    /// <param name="positive">The class of each sample.</param>
    /// <param name="theta">The weights followed by the bias.</param>
    /// <param name="c">The inverse regularization strength.</param>
    /// <returns>The objective value.</returns>
    private static double Objective(double[][] rows, IReadOnlyList<bool> positive, double[] theta, double c)
    {
        var dim = theta.Length - 1;
        double reg = 0;
        for (var i = 0; i < dim; i++)
        {
            reg += theta[i] * theta[i];
        }

        double loss = 0;
        for (var s = 0; s < rows.Length; s++)
        {
            var margin = Activation(rows[s], theta) * (positive[s] ? 1 : -1);
            loss += Softplus(-margin);
        }

        return (0.5 * reg) + (c * loss);
    }

    /// <summary>
    ///     Computes the gradient and the full Hessian of the objective.
    /// </summary>
    /// <param name="rows">The samples.</param>
    /// <param name="positive">The class of each sample.</param>
    /// <param name="theta">The weights followed by the bias.</param>
    /// <param name="c">The inverse regularization strength.</param>
    /// <param name="n">The sample count.</param>
    /// <param name="dim">The number of coordinates.</param>
    /// <param name="gradient">Receives the gradient.</param>
    /// <param name="hessian">Receives the row-major Hessian, <c>(dim + 1)²</c> numbers.</param>
    private static void GradientAndHessian(double[][] rows, IReadOnlyList<bool> positive, double[] theta, double c, int n, int dim, double[] gradient, double[] hessian)
    {
        var size = dim + 1;
        Array.Clear(gradient);
        Array.Clear(hessian);
        for (var i = 0; i < dim; i++)
        {
            gradient[i] = theta[i];
            hessian[(i * size) + i] = 1.0;
        }

        var width = Vector<double>.Count;
        for (var s = 0; s < n; s++)
        {
            var x = rows[s];
            var y = positive[s] ? 1.0 : -1.0;
            var p = Sigmoid(y * Activation(x, theta));
            var g = -c * y * (1 - p);
            var h = c * p * (1 - p);
            for (var i = 0; i < dim; i++)
            {
                gradient[i] += g * x[i];
                var hx = h * x[i];
                var row = i * size;

                // The upper triangle, cell by cell: each cell is updated on its own, so the vector width never matters
                var j = i;
                for (; j + width <= dim; j += width)
                {
                    var update = new Vector<double>(hessian, row + j) + (new Vector<double>(hx) * new Vector<double>(x, j));
                    update.CopyTo(hessian, row + j);
                }

                for (; j < dim; j++)
                {
                    hessian[row + j] += hx * x[j];
                }

                hessian[row + dim] += hx;
            }

            gradient[dim] += g;
            hessian[(dim * size) + dim] += h;
        }

        // Mirror the upper triangle
        for (var i = 0; i < size; i++)
        {
            for (var j = 0; j < i; j++)
            {
                hessian[(i * size) + j] = hessian[(j * size) + i];
            }
        }
    }

    /// <summary>
    ///     Solves <c>H·x = g</c> for a symmetric positive definite matrix using Cholesky decomposition.
    /// </summary>
    /// <param name="a">The row-major matrix; overwritten.</param>
    /// <param name="b">The right-hand side.</param>
    /// <param name="n">The dimension.</param>
    /// <returns>The solution.</returns>
    private static double[] SolveSpd(double[] a, double[] b, int n)
    {
        // Factor A = L·Lᵀ in place, with a tiny jitter guarding against round-off
        for (var j = 0; j < n; j++)
        {
            var rowJ = j * n;
            var diagonal = a[rowJ + j] + 1e-12;
            for (var k = 0; k < j; k++)
            {
                diagonal -= a[rowJ + k] * a[rowJ + k];
            }

            diagonal = Math.Sqrt(Math.Max(diagonal, 1e-12));
            a[rowJ + j] = diagonal;
            for (var i = j + 1; i < n; i++)
            {
                var rowI = i * n;
                var sum = a[rowI + j];
                for (var k = 0; k < j; k++)
                {
                    sum -= a[rowI + k] * a[rowJ + k];
                }

                a[rowI + j] = sum / diagonal;
            }
        }

        // Forward then backward substitution
        var y = new double[n];
        for (var i = 0; i < n; i++)
        {
            var sum = b[i];
            for (var k = 0; k < i; k++)
            {
                sum -= a[(i * n) + k] * y[k];
            }

            y[i] = sum / a[(i * n) + i];
        }

        var x = new double[n];
        for (var i = n - 1; i >= 0; i--)
        {
            var sum = y[i];
            for (var k = i + 1; k < n; k++)
            {
                sum -= a[(k * n) + i] * x[k];
            }

            x[i] = sum / a[(i * n) + i];
        }

        return x;
    }

    /// <summary>
    ///     Computes <c>log(1 + exp(x))</c> without overflow.
    /// </summary>
    /// <param name="x">The argument.</param>
    /// <returns>The softplus value.</returns>
    private static double Softplus(double x) => x > 30 ? x : Math.Log(1 + Math.Exp(x));

    /// <summary>
    ///     Computes the logistic function without overflow.
    /// </summary>
    /// <param name="x">The argument.</param>
    /// <returns>The sigmoid value.</returns>
    private static double Sigmoid(double x) => x >= 0 ? 1 / (1 + Math.Exp(-x)) : Math.Exp(x) / (1 + Math.Exp(x));
}
