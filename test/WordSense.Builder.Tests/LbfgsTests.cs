namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the L-BFGS minimizer.
/// </summary>
public sealed class LbfgsTests
{
    private static double Quadratic(double[] x, double[] g)
    {
        // f = sum (i + 1) (x_i - i)^2
        var f = 0.0;
        for (var i = 0; i < x.Length; i++)
        {
            var d = x[i] - i;
            f += (i + 1) * d * d;
            g[i] = 2 * (i + 1) * d;
        }

        return f;
    }

    private static double Rosenbrock(double[] x, double[] g)
    {
        var a = 1 - x[0];
        var b = x[1] - (x[0] * x[0]);
        g[0] = (-2 * a) - (400 * x[0] * b);
        g[1] = 200 * b;
        return (a * a) + (100 * b * b);
    }

    /// <summary>
    ///     A convex quadratic is minimized to its known minimum.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Lbfgs_Minimize_Quadratic_FindsTheMinimum()
    {
        var result = Lbfgs.Minimize(Quadratic, new double[6], 1e-9, 200);

        Assert.True(result.Converged);
        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(i, result.X[i], 6);
        }

        Assert.True(result.Value < 1e-12);
    }

    /// <summary>
    ///     The Rosenbrock valley, which needs the line search, is minimized.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Lbfgs_Minimize_Rosenbrock_FindsTheMinimum()
    {
        var result = Lbfgs.Minimize(Rosenbrock, [-1.2, 1.0], 1e-8, 500);

        Assert.True(result.Converged);
        Assert.Equal(1.0, result.X[0], 5);
        Assert.Equal(1.0, result.X[1], 5);
    }

    /// <summary>
    ///     The same objective and start give the same point bit for bit, and the start is not changed.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Lbfgs_Minimize_IsDeterministicAndKeepsTheStart()
    {
        double[] start = [-1.2, 1.0];

        var first = Lbfgs.Minimize(Rosenbrock, start, 1e-8, 500);
        var second = Lbfgs.Minimize(Rosenbrock, start, 1e-8, 500);

        Assert.Equal(first.X, second.X);
        Assert.Equal(first.Value, second.Value);
        Assert.Equal(first.Evaluations, second.Evaluations);
        Assert.Equal([-1.2, 1.0], start);
    }

    /// <summary>
    ///     The iteration cap stops the run and says it did not converge; a start at the minimum converges at once.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Lbfgs_Minimize_IterationCapAndStartAtMinimum()
    {
        var capped = Lbfgs.Minimize(Rosenbrock, [-1.2, 1.0], 1e-12, 3);
        var atMinimum = Lbfgs.Minimize(Quadratic, [0, 1, 2], 1e-9, 10);

        Assert.False(capped.Converged);
        Assert.Equal(3, capped.Iterations);
        Assert.True(atMinimum.Converged);
        Assert.Equal(0, atMinimum.Iterations);
    }

    /// <summary>
    ///     Null arguments and non-positive limits are rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Lbfgs_Minimize_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Lbfgs.Minimize(null!, [0.0], 1e-6, 10));
        Assert.Throws<ArgumentNullException>(() => Lbfgs.Minimize(Quadratic, null!, 1e-6, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => Lbfgs.Minimize(Quadratic, [0.0], 1e-6, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Lbfgs.Minimize(Quadratic, [0.0], 1e-6, 10, 0));
    }
}
