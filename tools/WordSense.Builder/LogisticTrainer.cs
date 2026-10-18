namespace WordSense.Builder;

/// <summary>
///     Trains an L2-regularized binary logistic regression with a deterministic Newton method.
/// </summary>
/// <remarks>
///     Minimizes <c>0.5·|w|² + C·Σ log(1 + exp(−y·(w·x + b)))</c> with an unregularized bias. The method
///     starts from zero, uses fixed iteration rules and a backtracking line search, and works in double
///     precision, so the same input always produces the same weights on the same platform.
/// </remarks>
internal static class LogisticTrainer
{
    private const int MaxIterations = 60;
    private const double Tolerance = 1e-9;

    /// <summary>
    ///     Trains one binary classifier.
    /// </summary>
    /// <param name="features">One feature vector per sample.</param>
    /// <param name="positive">Whether each sample belongs to the positive class.</param>
    /// <param name="c">The inverse regularization strength.</param>
    /// <returns>The weight vector and the bias.</returns>
    public static (double[] Weights, double Bias) Train(IReadOnlyList<float[]> features, IReadOnlyList<bool> positive, double c)
    {
        var n = features.Count;
        var dim = features[0].Length;
        var size = dim + 1;
        var theta = new double[size];
        var objective = Objective(features, positive, theta, c);

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var (gradient, hessian) = GradientAndHessian(features, positive, theta, c, n, dim);
            var step = SolveSpd(hessian, gradient, size);

            // Backtrack until the objective decreases
            var scale = 1.0;
            double[] candidate = theta;
            var candidateObjective = objective;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                candidate = new double[size];
                for (var i = 0; i < size; i++)
                {
                    candidate[i] = theta[i] - (scale * step[i]);
                }

                candidateObjective = Objective(features, positive, candidate, c);
                if (candidateObjective <= objective)
                {
                    break;
                }

                scale /= 2;
            }

            var change = Math.Abs(objective - candidateObjective);
            theta = candidate;
            objective = candidateObjective;
            if (change < Tolerance * Math.Max(1.0, Math.Abs(objective)))
            {
                break;
            }
        }

        return (theta[..dim], theta[dim]);
    }

    /// <summary>
    ///     Evaluates the regularized objective.
    /// </summary>
    /// <param name="features">The samples.</param>
    /// <param name="positive">The class of each sample.</param>
    /// <param name="theta">The weights followed by the bias.</param>
    /// <param name="c">The inverse regularization strength.</param>
    /// <returns>The objective value.</returns>
    private static double Objective(IReadOnlyList<float[]> features, IReadOnlyList<bool> positive, double[] theta, double c)
    {
        var dim = theta.Length - 1;
        double reg = 0;
        for (var i = 0; i < dim; i++)
        {
            reg += theta[i] * theta[i];
        }

        double loss = 0;
        for (var s = 0; s < features.Count; s++)
        {
            var margin = Activation(features[s], theta) * (positive[s] ? 1 : -1);
            loss += Softplus(-margin);
        }

        return (0.5 * reg) + (c * loss);
    }

    /// <summary>
    ///     Computes the gradient and Hessian of the objective.
    /// </summary>
    /// <param name="features">The samples.</param>
    /// <param name="positive">The class of each sample.</param>
    /// <param name="theta">The weights followed by the bias.</param>
    /// <param name="c">The inverse regularization strength.</param>
    /// <param name="n">The sample count.</param>
    /// <param name="dim">The feature dimension.</param>
    /// <returns>The gradient and the full Hessian.</returns>
    private static (double[] Gradient, double[,] Hessian) GradientAndHessian(
        IReadOnlyList<float[]> features, IReadOnlyList<bool> positive, double[] theta, double c, int n, int dim)
    {
        var size = dim + 1;
        var gradient = new double[size];
        var hessian = new double[size, size];
        for (var i = 0; i < dim; i++)
        {
            gradient[i] = theta[i];
            hessian[i, i] = 1.0;
        }

        for (var s = 0; s < n; s++)
        {
            var x = features[s];
            var y = positive[s] ? 1.0 : -1.0;
            var p = Sigmoid(y * Activation(x, theta));
            var g = -c * y * (1 - p);
            var h = c * p * (1 - p);
            for (var i = 0; i < dim; i++)
            {
                gradient[i] += g * x[i];
                var hx = h * x[i];
                for (var j = i; j < dim; j++)
                {
                    hessian[i, j] += hx * x[j];
                }

                hessian[i, dim] += hx;
            }

            gradient[dim] += g;
            hessian[dim, dim] += h;
        }

        // Mirror the upper triangle
        for (var i = 0; i < size; i++)
        {
            for (var j = 0; j < i; j++)
            {
                hessian[i, j] = hessian[j, i];
            }
        }

        return (gradient, hessian);
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

    /// <summary>
    ///     Solves <c>H·x = g</c> for a symmetric positive definite matrix using Cholesky decomposition.
    /// </summary>
    /// <param name="a">The matrix; overwritten.</param>
    /// <param name="b">The right-hand side.</param>
    /// <param name="n">The dimension.</param>
    /// <returns>The solution.</returns>
    private static double[] SolveSpd(double[,] a, double[] b, int n)
    {
        // Factor A = L·Lᵀ in place, with a tiny jitter guarding against round-off
        for (var j = 0; j < n; j++)
        {
            var diagonal = a[j, j] + 1e-12;
            for (var k = 0; k < j; k++)
            {
                diagonal -= a[j, k] * a[j, k];
            }

            diagonal = Math.Sqrt(Math.Max(diagonal, 1e-12));
            a[j, j] = diagonal;
            for (var i = j + 1; i < n; i++)
            {
                var sum = a[i, j];
                for (var k = 0; k < j; k++)
                {
                    sum -= a[i, k] * a[j, k];
                }

                a[i, j] = sum / diagonal;
            }
        }

        // Forward then backward substitution
        var y = new double[n];
        for (var i = 0; i < n; i++)
        {
            var sum = b[i];
            for (var k = 0; k < i; k++)
            {
                sum -= a[i, k] * y[k];
            }

            y[i] = sum / a[i, i];
        }

        var x = new double[n];
        for (var i = n - 1; i >= 0; i--)
        {
            var sum = y[i];
            for (var k = i + 1; k < n; k++)
            {
                sum -= a[k, i] * x[k];
            }

            x[i] = sum / a[i, i];
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
