namespace WordSense.Builder;

/// <summary>
///     The outcome of a minimization.
/// </summary>
/// <param name="X">The final point.</param>
/// <param name="Value">The objective at the final point.</param>
/// <param name="Iterations">The number of iterations.</param>
/// <param name="Evaluations">The number of evaluations of the objective and its gradient.</param>
/// <param name="Converged">True when the largest gradient component fell to the tolerance or the objective stopped decreasing; false when the iteration cap was reached.</param>
internal sealed record LbfgsResult(double[] X, double Value, int Iterations, int Evaluations, bool Converged);

/// <summary>
///     A limited-memory BFGS minimizer in double precision with a strong-Wolfe line search.
/// </summary>
/// <remarks>
///     The minimizer is deterministic: it starts from the point it is given, runs plain sequential arithmetic in a fixed
///     order and has no random choice, so the same objective gives the same point bit for bit. The stopping rules are
///     those of the usual implementations: the largest absolute gradient component at most the tolerance, a relative
///     decrease of the objective of at most <see cref="FunctionTolerance"/>, or the iteration cap.
/// </remarks>
internal static class Lbfgs
{
    /// <summary>
    ///     The relative decrease of the objective below which the minimizer stops: 64 times the machine epsilon.
    /// </summary>
    public const double FunctionTolerance = 64 * 2.220446049250313e-16;

    private const double SufficientDecrease = 1e-4;
    private const double Curvature = 0.9;
    private const int MaxLineSearchEvaluations = 30;

    /// <summary>
    ///     Minimizes a smooth function.
    /// </summary>
    /// <param name="objective">Computes the value at a point and writes the gradient into the second argument.</param>
    /// <param name="start">The starting point; it is not changed.</param>
    /// <param name="gradientTolerance">The largest absolute gradient component at which the minimizer stops.</param>
    /// <param name="maxIterations">The iteration cap.</param>
    /// <param name="history">The number of correction pairs kept.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a limit is not positive.</exception>
    public static LbfgsResult Minimize(Func<double[], double[], double> objective, double[] start, double gradientTolerance, int maxIterations, int history = 10)
    {
        ArgumentNullException.ThrowIfNull(objective);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxIterations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(history);
        var n = start.Length;
        var x = (double[])start.Clone();
        var g = new double[n];
        var evaluations = 0;
        var f = objective(x, g);
        evaluations++;
        var sList = new List<double[]>();
        var yList = new List<double[]>();
        var rho = new List<double>();
        var direction = new double[n];
        var xNew = new double[n];
        var gNew = new double[n];
        var iteration = 0;
        var converged = MaxAbs(g) <= gradientTolerance;
        while (!converged && iteration < maxIterations)
        {
            Direction(g, sList, yList, rho, direction);
            var slope = Dot(g, direction);
            if (slope >= 0)
            {
                // Not a descent direction (a numerical breakdown): restart from steepest descent
                sList.Clear();
                yList.Clear();
                rho.Clear();
                for (var i = 0; i < n; i++)
                {
                    direction[i] = -g[i];
                }

                slope = Dot(g, direction);
            }

            var step = iteration == 0 && sList.Count == 0 ? Math.Min(1.0, 1.0 / Math.Sqrt(Dot(g, g))) : 1.0;
            var (alpha, fNew, searchEvaluations) = LineSearch(objective, x, f, g, direction, slope, step, xNew, gNew);
            evaluations += searchEvaluations;
            iteration++;
            if (alpha <= 0)
            {
                // The line search found no decrease: the point is as good as this arithmetic gets
                converged = true;
                break;
            }

            // Store the correction pair if its curvature is positive, dropping the oldest at the memory limit
            var s = new double[n];
            var y = new double[n];
            for (var i = 0; i < n; i++)
            {
                s[i] = xNew[i] - x[i];
                y[i] = gNew[i] - g[i];
            }

            var sy = Dot(s, y);
            if (sy > 1e-10 * Math.Sqrt(Dot(s, s) * Dot(y, y)))
            {
                if (sList.Count == history)
                {
                    sList.RemoveAt(0);
                    yList.RemoveAt(0);
                    rho.RemoveAt(0);
                }

                sList.Add(s);
                yList.Add(y);
                rho.Add(1.0 / sy);
            }

            var decrease = (f - fNew) / Math.Max(Math.Max(Math.Abs(f), Math.Abs(fNew)), 1.0);
            Array.Copy(xNew, x, n);
            Array.Copy(gNew, g, n);
            f = fNew;
            converged = MaxAbs(g) <= gradientTolerance || decrease <= FunctionTolerance;
        }

        return new LbfgsResult(x, f, iteration, evaluations, converged);
    }

    /// <summary>
    ///     Computes the quasi-Newton direction with the two-loop recursion.
    /// </summary>
    /// <param name="g">The gradient.</param>
    /// <param name="sList">The position differences.</param>
    /// <param name="yList">The gradient differences.</param>
    /// <param name="rho">The reciprocals of the curvatures.</param>
    /// <param name="direction">Receives the direction.</param>
    private static void Direction(double[] g, List<double[]> sList, List<double[]> yList, List<double> rho, double[] direction)
    {
        var n = g.Length;
        var m = sList.Count;
        Array.Copy(g, direction, n);
        var alpha = new double[m];
        for (var i = m - 1; i >= 0; i--)
        {
            alpha[i] = rho[i] * Dot(sList[i], direction);
            for (var j = 0; j < n; j++)
            {
                direction[j] -= alpha[i] * yList[i][j];
            }
        }

        // Scale by the curvature of the newest pair; without a pair the direction is the steepest descent
        var scale = m == 0 ? 1.0 : Dot(sList[m - 1], yList[m - 1]) / Dot(yList[m - 1], yList[m - 1]);
        for (var j = 0; j < n; j++)
        {
            direction[j] *= scale;
        }

        for (var i = 0; i < m; i++)
        {
            var beta = rho[i] * Dot(yList[i], direction);
            for (var j = 0; j < n; j++)
            {
                direction[j] += (alpha[i] - beta) * sList[i][j];
            }
        }

        for (var j = 0; j < n; j++)
        {
            direction[j] = -direction[j];
        }
    }

    /// <summary>
    ///     Searches a step along a descent direction that satisfies the strong Wolfe conditions.
    /// </summary>
    /// <param name="objective">The objective.</param>
    /// <param name="x">The current point.</param>
    /// <param name="f0">The objective at <paramref name="x"/>.</param>
    /// <param name="g0">The gradient at <paramref name="x"/>.</param>
    /// <param name="direction">The descent direction.</param>
    /// <param name="slope0">The directional derivative at <paramref name="x"/>; negative.</param>
    /// <param name="initial">The first step to try.</param>
    /// <param name="xNew">Receives the new point.</param>
    /// <param name="gNew">Receives the gradient at the new point.</param>
    /// <returns>The step (0 when none was found), the objective at the new point and the number of evaluations.</returns>
    private static (double Alpha, double Value, int Evaluations) LineSearch(Func<double[], double[], double> objective, double[] x, double f0, double[] g0, double[] direction, double slope0, double initial, double[] xNew, double[] gNew)
    {
        _ = g0;
        var evaluations = 0;
        double Evaluate(double alpha, out double slope)
        {
            for (var i = 0; i < x.Length; i++)
            {
                xNew[i] = x[i] + (alpha * direction[i]);
            }

            var value = objective(xNew, gNew);
            evaluations++;
            slope = Dot(gNew, direction);
            return value;
        }

        double previous = 0, fPrevious = f0, slopePrevious = slope0;
        var current = initial;
        for (var i = 0; i < MaxLineSearchEvaluations; i++)
        {
            var f = Evaluate(current, out var slope);
            if (!double.IsFinite(f))
            {
                // Too far: shrink the step and try again
                current = (previous + current) / 2;
                continue;
            }

            if (f > f0 + (SufficientDecrease * current * slope0) || (i > 0 && f >= fPrevious))
            {
                return Zoom(Evaluate, f0, slope0, previous, fPrevious, slopePrevious, current, f, slope, evaluations);
            }

            if (Math.Abs(slope) <= -Curvature * slope0)
            {
                return (current, f, evaluations);
            }

            if (slope >= 0)
            {
                return Zoom(Evaluate, f0, slope0, current, f, slope, previous, fPrevious, slopePrevious, evaluations);
            }

            previous = current;
            fPrevious = f;
            slopePrevious = slope;
            current *= 2;
        }

        return (0, f0, evaluations);
    }

    private delegate double Evaluator(double alpha, out double slope);

    /// <summary>
    ///     Narrows a bracket of acceptable steps until a step satisfies the strong Wolfe conditions.
    /// </summary>
    /// <param name="evaluate">Evaluates the objective at a step; leaves the new point and gradient in the caller's buffers.</param>
    /// <param name="f0">The objective at step 0.</param>
    /// <param name="slope0">The directional derivative at step 0.</param>
    /// <param name="lo">The step with the lower objective of the bracket.</param>
    /// <param name="fLo">The objective at <paramref name="lo"/>.</param>
    /// <param name="slopeLo">The directional derivative at <paramref name="lo"/>.</param>
    /// <param name="hi">The other end of the bracket.</param>
    /// <param name="fHi">The objective at <paramref name="hi"/>.</param>
    /// <param name="slopeHi">The directional derivative at <paramref name="hi"/>.</param>
    /// <param name="evaluations">The evaluations used so far.</param>
    /// <returns>The step (0 when none was found), its objective and the total evaluations.</returns>
    private static (double Alpha, double Value, int Evaluations) Zoom(Evaluator evaluate, double f0, double slope0, double lo, double fLo, double slopeLo, double hi, double fHi, double slopeHi, int evaluations)
    {
        for (var i = 0; i < MaxLineSearchEvaluations; i++)
        {
            // Cubic interpolation, kept away from the ends of the bracket; bisection when it is not usable
            var width = hi - lo;
            var alpha = Cubic(lo, fLo, slopeLo, hi, fHi, slopeHi);
            if (!double.IsFinite(alpha) || alpha < Math.Min(lo, hi) + (0.1 * Math.Abs(width)) || alpha > Math.Max(lo, hi) - (0.1 * Math.Abs(width)))
            {
                alpha = (lo + hi) / 2;
            }

            var f = evaluate(alpha, out var slope);
            evaluations++;
            if (f > f0 + (SufficientDecrease * alpha * slope0) || f >= fLo)
            {
                hi = alpha;
                fHi = f;
                slopeHi = slope;
                continue;
            }

            if (Math.Abs(slope) <= -Curvature * slope0)
            {
                return (alpha, f, evaluations);
            }

            if (slope * (hi - lo) >= 0)
            {
                hi = lo;
                fHi = fLo;
                slopeHi = slopeLo;
            }

            lo = alpha;
            fLo = f;
            slopeLo = slope;
        }

        // The evaluation buffers hold the last trial, so report the failure instead of a point that was not accepted
        return (0, f0, evaluations);
    }

    private static double Cubic(double a, double fa, double da, double b, double fb, double db)
    {
        var d1 = da + db - (3 * (fa - fb) / (a - b));
        var square = (d1 * d1) - (da * db);
        if (square < 0)
        {
            return double.NaN;
        }

        var d2 = Math.Sign(b - a) * Math.Sqrt(square);
        return b - ((b - a) * ((db + d2 - d1) / (db - da + (2 * d2))));
    }

    private static double Dot(double[] a, double[] b)
    {
        var sum = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }

    private static double MaxAbs(double[] v)
    {
        var max = 0.0;
        foreach (var x in v)
        {
            max = Math.Max(max, Math.Abs(x));
        }

        return max;
    }
}
