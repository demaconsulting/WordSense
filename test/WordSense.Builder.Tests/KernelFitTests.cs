// Copyright (c) DEMA Consulting
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the kernel form of the fit: it finds the same classifiers and thresholds as the primal form, which is
///     the reference, and its basis keeps the inner products of the samples.
/// </summary>
public sealed class KernelFitTests
{
    private const int Dimension = 40;

    // Unit-length features of three classes around three directions; the last two samples of the set are copies of
    // earlier ones, so the Gram matrix is rank deficient
    private static (float[][] Features, int[] Labels) Samples(int perClass, bool duplicates)
    {
        var random = new Random(20260101);
        var features = new List<float[]>();
        var labels = new List<int>();
        for (var i = 0; i < perClass; i++)
        {
            for (var label = 0; label < 3; label++)
            {
                var x = new double[Dimension];
                for (var d = 0; d < Dimension; d++)
                {
                    x[d] = (random.NextDouble() - 0.5) * 0.6;
                }

                x[label] += 1.0;
                var length = Math.Sqrt(x.Sum(v => v * v));
                features.Add([.. x.Select(v => (float)(v / length))]);
                labels.Add(label);
            }
        }

        if (duplicates)
        {
            features.Add((float[])features[0].Clone());
            labels.Add(labels[0]);
            features.Add((float[])features[4].Clone());
            labels.Add(labels[4]);
        }

        return ([.. features], [.. labels]);
    }

    /// <summary>
    ///     The coordinates of the basis have the inner products of the samples, a duplicate adds no coordinate, and the
    ///     expansion gives the same activations.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_KernelBasis_Create_KeepsInnerProductsAndDropsDuplicates()
    {
        // Arrange
        var (features, _) = Samples(8, true);
        var members = Enumerable.Range(0, features.Length).ToArray();
        var gram = KernelBasis.Gram(features);

        // Act
        var basis = KernelBasis.Create(gram, members);

        // Assert: two duplicates, so two fewer coordinates than samples
        Assert.Equal(features.Length - 2, basis.Dimension);
        for (var i = 0; i < features.Length; i++)
        {
            for (var j = 0; j <= i; j++)
            {
                var product = basis.Rows[i].Zip(basis.Rows[j], (a, b) => a * b).Sum();
                Assert.Equal(gram[i][j], product, 9);
            }
        }

        // An arbitrary coefficient vector in the coordinates is the same weight vector as its expansion over the samples
        var coefficients = Enumerable.Range(0, basis.Dimension).Select(c => Math.Sin(c)).ToArray();
        var beta = basis.Expand(coefficients);
        for (var k = 0; k < features.Length; k++)
        {
            var inBasis = basis.Rows[k].Zip(coefficients, (a, b) => a * b).Sum();
            var inFeatures = Enumerable.Range(0, features.Length).Sum(i => beta[i] * gram[Math.Max(i, k)][Math.Min(i, k)]);
            Assert.Equal(inBasis, inFeatures, 9);
        }
    }

    /// <summary>
    ///     A subset of the samples gets the basis of that subset, from the Gram matrix of all of them.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_KernelBasis_Create_Subset_UsesTheSubsetOnly()
    {
        // Arrange
        var (features, _) = Samples(6, false);
        var gram = KernelBasis.Gram(features);
        int[] members = [1, 4, 7, 10, 13];

        // Act
        var basis = KernelBasis.Create(gram, members);

        // Assert
        Assert.Equal(members.Length, basis.Rows.Length);
        Assert.Equal(members.Length, basis.Dimension);
        for (var a = 0; a < members.Length; a++)
        {
            for (var b = 0; b < members.Length; b++)
            {
                var expected = gram[Math.Max(members[a], members[b])][Math.Min(members[a], members[b])];
                Assert.Equal(expected, basis.Rows[a].Zip(basis.Rows[b], (x, y) => x * y).Sum(), 9);
            }
        }
    }

    /// <summary>
    ///     The kernel solver finds the classifiers of the primal solver (the same optimum), also when two samples are
    ///     duplicates.
    /// </summary>
    /// <param name="duplicates">Whether the set contains duplicates.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WordSenseBuilder_WeightsTrainer_Fit_KernelSolver_FindsThePrimalOptimum(bool duplicates)
    {
        // Arrange: fewer samples than dimensions, so the kernel form is used
        var (features, labels) = Samples(8, duplicates);
        Assert.True(features.Length < Dimension);

        // Act
        var kernel = WeightsTrainer.Fit(features, labels, 3, 100, FitSolver.Kernel);
        var primal = WeightsTrainer.Fit(features, labels, 3, 100, FitSolver.Primal);

        // Assert: the weights and the biases agree to well below the half-precision resolution of the stored weights
        Assert.Equal(primal.Weights.Length, kernel.Weights.Length);
        for (var i = 0; i < primal.Weights.Length; i++)
        {
            Assert.Equal(primal.Weights[i], kernel.Weights[i], 1e-3f);
        }

        for (var row = 0; row < 3; row++)
        {
            Assert.Equal(primal.Bias[row], kernel.Bias[row], 1e-3f);
        }
    }

    /// <summary>
    ///     The kernel solver is deterministic: the same input gives the same weights bit for bit.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_Fit_KernelSolver_IsDeterministic()
    {
        // Arrange
        var (features, labels) = Samples(8, true);

        // Act
        var first = WeightsTrainer.Fit(features, labels, 3, 100);
        var second = WeightsTrainer.Fit(features, labels, 3, 100);

        // Assert
        Assert.Equal(first.Weights, second.Weights);
        Assert.Equal(first.Bias, second.Bias);
    }

    /// <summary>
    ///     The result does not depend on the number of threads that fit the classes: one thread and many give the same
    ///     weights bit for bit.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LemmaFitter_Fit_IsIndependentOfTheThreadCount()
    {
        // Arrange
        var (features, labels) = Samples(8, true);
        var members = Enumerable.Range(0, features.Length).ToArray();

        // Act
        var single = new LemmaFitter(features, FitSolver.Kernel, 1).Fit(members, labels, 3, 100);
        var many = new LemmaFitter(features, FitSolver.Kernel, 8).Fit(members, labels, 3, 100);

        // Assert
        Assert.Equal(single.Weights, many.Weights);
        Assert.Equal(single.Bias, many.Bias);
    }

    /// <summary>
    ///     With at least as many samples as dimensions the kernel solver works in the feature space, so it is the primal
    ///     solver.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_WeightsTrainer_Fit_MoreSamplesThanDimensions_IsThePrimalSolver()
    {
        // Arrange
        var (features, labels) = Samples(20, false);
        Assert.True(features.Length >= Dimension);

        // Act
        var kernel = WeightsTrainer.Fit(features, labels, 3, 100, FitSolver.Kernel);
        var primal = WeightsTrainer.Fit(features, labels, 3, 100, FitSolver.Primal);

        // Assert
        Assert.Equal(primal.Weights, kernel.Weights);
        Assert.Equal(primal.Bias, kernel.Bias);
    }

    /// <summary>
    ///     The thresholds that cross-validation chooses with the kernel solver are those of the primal solver.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Thresholds_Choose_KernelSolver_FindsThePrimalThresholds()
    {
        // Arrange
        var (features, labels) = Samples(10, false);
        var kernelOptions = new TrainingOptions(Parallelism: 1, Solver: FitSolver.Kernel);
        var primalOptions = kernelOptions with { Solver = FitSolver.Primal };

        // Act
        var kernel = Thresholds.Choose("sample", features, labels, 3, [-1, -1, -1], kernelOptions);
        var primal = Thresholds.Choose("sample", features, labels, 3, [-1, -1, -1], primalOptions);

        // Assert
        Assert.Equal(primal.Fine, kernel.Fine, 1e-3f);
        Assert.Equal(primal.Group, kernel.Group);
    }
}
