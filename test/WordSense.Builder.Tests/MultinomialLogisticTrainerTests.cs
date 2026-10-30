namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the multinomial logistic regression of the part-of-speech head.
/// </summary>
public sealed class MultinomialLogisticTrainerTests
{
    private const int Dimension = 4;

    /// <summary>
    ///     Builds a table whose class depends on the word and on the previous word, with some missing vectors, some
    ///     excluded tokens and a little label noise.
    /// </summary>
    private static PosTokenTable Table(int sentences, int length, int seed)
    {
        var random = new Random(seed);
        var n = sentences * length;
        var vectors = new float[n * Dimension];
        var sentence = new int[n];
        var gold = new int[n];
        var has = new bool[n];
        for (var t = 0; t < n; t++)
        {
            sentence[t] = t / length;
            has[t] = t % 17 != 5;
            for (var j = 0; j < Dimension; j++)
            {
                vectors[(t * Dimension) + j] = has[t] ? (float)((random.NextDouble() * 2) - 1 + (t % 3)) : 0f;
            }
        }

        for (var t = 0; t < n; t++)
        {
            var previous = t > 0 && sentence[t - 1] == sentence[t] ? vectors[(t - 1) * Dimension] : 0f;
            var cls = (vectors[t * Dimension] > 1 ? 1 : 0) + (2 * (previous > 1 ? 1 : 0));
            if (random.NextDouble() < 0.05)
            {
                cls = (cls + 1) % 4;
            }

            gold[t] = t % 11 == 3 ? -1 : cls;
        }

        return new PosTokenTable(Dimension, vectors, sentence, gold, has);
    }

    private static double[] Logits(PosTokenTable table, int row, float[] weights, float[] biases)
    {
        var d = Dimension;
        var z = new double[MultinomialLogisticTrainer.Classes];
        for (var c = 0; c < z.Length; c++)
        {
            z[c] = biases[c];
            for (var j = 0; j < d; j++)
            {
                z[c] += (table.HasPrevious(row) ? weights[(c * 3 * d) + j] * table.Vectors[((row - 1) * d) + j] : 0)
                    + (weights[(c * 3 * d) + d + j] * table.Vectors[(row * d) + j])
                    + (table.HasNext(row) ? weights[(c * 3 * d) + (2 * d) + j] * table.Vectors[((row + 1) * d) + j] : 0);
            }
        }

        return z;
    }

    private static int Predict(PosTokenTable table, int row, PosFit fit) => Array.IndexOf(Logits(table, row, fit.Weights, fit.Biases), Logits(table, row, fit.Weights, fit.Biases).Max());

    /// <summary>
    ///     At the optimum the gradient of an independent, dense implementation of the objective vanishes, so the
    ///     chunked objective and the folding of the standardization are right.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_MultinomialLogisticTrainer_Fit_Optimum_HasZeroGradientOfTheDenseObjective()
    {
        // Arrange
        var table = Table(60, 7, 1);
        const double C = 0.5;

        // Act
        var fit = MultinomialLogisticTrainer.Fit(table, new PosFitOptions(C, 1e-11, 20000, 2));

        // Assert: dense features, standardized as the trainer documents, and the gradient of mean loss + |W|^2 / (2 C n)
        var rows = table.TrainingRows();
        var d = Dimension;
        double[] Features(int r)
        {
            var f = new double[3 * d];
            for (var j = 0; j < d; j++)
            {
                f[j] = table.HasPrevious(r) ? table.Vectors[((r - 1) * d) + j] : 0;
                f[d + j] = table.Vectors[(r * d) + j];
                f[(2 * d) + j] = table.HasNext(r) ? table.Vectors[((r + 1) * d) + j] : 0;
            }

            return f;
        }
        var mean = new double[3 * d];
        var std = new double[3 * d];
        foreach (var r in rows)
        {
            var f = Features(r);
            for (var q = 0; q < f.Length; q++)
            {
                mean[q] += f[q] / rows.Length;
            }
        }

        foreach (var r in rows)
        {
            var f = Features(r);
            for (var q = 0; q < f.Length; q++)
            {
                std[q] += (f[q] - mean[q]) * (f[q] - mean[q]) / rows.Length;
            }
        }

        std = [.. std.Select(v => Math.Sqrt(v) + 1e-6)];
        var k = MultinomialLogisticTrainer.Classes;
        var wStd = new double[k * 3 * d];
        var bStd = new double[k];
        for (var c = 0; c < k; c++)
        {
            bStd[c] = fit.Biases[c];
            for (var q = 0; q < 3 * d; q++)
            {
                wStd[(c * 3 * d) + q] = (double)fit.Weights[(c * 3 * d) + q] * std[q];
                bStd[c] += (double)fit.Weights[(c * 3 * d) + q] * mean[q];
            }
        }

        var gradient = new double[(k * 3 * d) + k];
        foreach (var r in rows)
        {
            var f = Features(r).Select((x, q) => (x - mean[q]) / std[q]).ToArray();
            var z = Enumerable.Range(0, k).Select(c => bStd[c] + Enumerable.Range(0, 3 * d).Sum(q => wStd[(c * 3 * d) + q] * f[q])).ToArray();
            var max = z.Max();
            var total = z.Sum(v => Math.Exp(v - max));
            for (var c = 0; c < k; c++)
            {
                var g = ((Math.Exp(z[c] - max) / total) - (c == table.Gold[r] ? 1 : 0)) / rows.Length;
                gradient[(k * 3 * d) + c] += g;
                for (var q = 0; q < 3 * d; q++)
                {
                    gradient[(c * 3 * d) + q] += g * f[q];
                }
            }
        }

        for (var i = 0; i < k * 3 * d; i++)
        {
            gradient[i] += wStd[i] / (C * rows.Length);
        }

        Assert.True(fit.Converged);
        Assert.True(gradient.Max(Math.Abs) < 1e-5, $"The largest gradient component of the dense objective is {gradient.Max(Math.Abs)}.");
    }

    /// <summary>
    ///     A rule that depends on the neighbor is learned, and a larger C fits the training data more tightly.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_MultinomialLogisticTrainer_Fit_LearnsTheNeighborRule()
    {
        // Arrange: a table with a neighbor-dependent rule, and the loose and tight regularization strengths
        var table = Table(80, 8, 2);
        var rows = table.TrainingRows();

        // Act: fit with a large C and with a very small C
        var loose = MultinomialLogisticTrainer.Fit(table, new PosFitOptions(100));
        var tight = MultinomialLogisticTrainer.Fit(table, new PosFitOptions(0.0001));

        // Assert: the large C predicts over 90 percent right and better than the small C
        var looseRight = rows.Count(r => Predict(table, r, loose) == table.Gold[r]);
        var tightRight = rows.Count(r => Predict(table, r, tight) == table.Gold[r]);
        Assert.True(looseRight > 0.9 * rows.Length, $"{looseRight} of {rows.Length} right.");
        Assert.True(looseRight > tightRight);
    }

    /// <summary>
    ///     The fit is the same bit for bit on every run and for every number of threads.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_MultinomialLogisticTrainer_Fit_IsDeterministicAcrossThreadCounts()
    {
        // Arrange: one table fitted with one thread twice and with eight threads
        var table = Table(120, 9, 3);

        // Act: fit all three
        var one = MultinomialLogisticTrainer.Fit(table, new PosFitOptions(0.3, Parallelism: 1));
        var again = MultinomialLogisticTrainer.Fit(table, new PosFitOptions(0.3, Parallelism: 1));
        var many = MultinomialLogisticTrainer.Fit(table, new PosFitOptions(0.3, Parallelism: 8));

        // Assert: weights, biases and iteration count are identical
        Assert.Equal(one.Weights, again.Weights);
        Assert.Equal(one.Weights, many.Weights);
        Assert.Equal(one.Biases, many.Biases);
        Assert.Equal(one.Iterations, many.Iterations);
    }

    /// <summary>
    ///     An empty table, a class outside the parts of speech and a bad C are rejected.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_MultinomialLogisticTrainer_Fit_InvalidInput_Throws()
    {
        // Arrange: a table, one variant with no trainable rows and one with an out-of-range class
        var table = Table(5, 5, 4);
        var empty = table with { Gold = [.. table.Gold.Select(_ => -1)] };
        var outside = table with { Gold = [.. table.Gold.Select(_ => 10)] };

        // Act / Assert: each invalid table, C or null argument is rejected
        Assert.Throws<ArgumentException>(() => MultinomialLogisticTrainer.Fit(empty, new PosFitOptions(1)));
        Assert.Throws<ArgumentException>(() => MultinomialLogisticTrainer.Fit(outside, new PosFitOptions(1)));
        Assert.Throws<ArgumentException>(() => MultinomialLogisticTrainer.Fit(table, new PosFitOptions(0)));
        Assert.Throws<ArgumentException>(() => MultinomialLogisticTrainer.Fit(table, new PosFitOptions(double.NaN)));
        Assert.Throws<ArgumentNullException>(() => MultinomialLogisticTrainer.Fit(null!, new PosFitOptions(1)));
        Assert.Throws<ArgumentNullException>(() => MultinomialLogisticTrainer.Fit(table, null!));
    }

    /// <summary>
    ///     The table finds the neighbors inside a sentence only and lists the tokens with a vector and a class.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_PosTokenTable_NeighborsAndRows()
    {
        // Arrange: five tokens in two sentences, one without a vector and one without a class
        var table = new PosTokenTable(1, [1, 2, 0, 4, 5], [0, 0, 0, 1, 1], [0, -1, 2, 3, 3], [true, true, false, true, true]);

        // Act / Assert: neighbors stop at sentence edges and the training rows skip the unusable tokens
        Assert.False(table.HasPrevious(0));
        Assert.True(table.HasPrevious(1));
        Assert.False(table.HasPrevious(3));
        Assert.True(table.HasNext(0));
        Assert.False(table.HasNext(2));
        Assert.False(table.HasNext(4));
        Assert.Equal([0, 3, 4], table.TrainingRows());
        Assert.Equal(5, table.Count);
    }
}
