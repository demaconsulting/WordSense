// Copyright (c) DEMA Consulting
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using DemaConsulting.WordSense.Onnx;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the persistent cache of lemma fits.
/// </summary>
public sealed class FitCacheTests : IDisposable
{
    private static readonly TrainingOptions Options = new();
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static LemmaFit Fit(int seed) =>
        new([seed * 0.1f, -seed / 3f, float.Epsilon * seed, 1f, 2f, 3f], [seed + 0.5f, -1f], seed * 0.25f, seed % 2 == 0 ? LemmaWeights.Never : 0.5f, 3);

    private static string K(char c) => new(c, 64);

    private static float[][] Features(float offset) => [[1f + offset, 0f, 0.5f], [0f, 1f, -0.5f + offset]];

    private FitCache Filled()
    {
        var cache = FitCache.Open(_dir.Root);
        cache.Add(K('A'), Fit(1));
        cache.Add(K('B'), Fit(2));
        cache.Add(K('C'), Fit(3));
        cache.Save();
        return cache;
    }

    /// <summary>
    ///     The key depends on the training settings, the lemma, the sense ids, the groups of the senses, the labels and
    ///     every feature number, and not on the number of threads.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FitCache_KeyOf_DependsOnEverythingTheFitDependsOn()
    {
        // Arrange
        string[] senses = ["bank#noun", "bank#verb"];
        int[] groups = [-1, -1];
        int[] labels = [0, 1];
        var key = FitCache.KeyOf(Options, "bank", senses, groups, labels, Features(0));

        // Act and Assert
        Assert.Equal(key, FitCache.KeyOf(new TrainingOptions(Parallelism: 7), "bank", senses, groups, labels, Features(0)));
        Assert.NotEqual(key, FitCache.KeyOf(Options with { C = 10 }, "bank", senses, groups, labels, Features(0)));
        Assert.NotEqual(key, FitCache.KeyOf(Options with { Folds = 3 }, "bank", senses, groups, labels, Features(0)));
        Assert.NotEqual(key, FitCache.KeyOf(Options, "bat", senses, groups, labels, Features(0)));
        Assert.NotEqual(key, FitCache.KeyOf(Options, "bank", ["bank#noun", "bank#adj"], groups, labels, Features(0)));
        Assert.NotEqual(key, FitCache.KeyOf(Options, "bank", senses, [0, 0], labels, Features(0)));
        Assert.NotEqual(key, FitCache.KeyOf(Options, "bank", senses, groups, [1, 0], Features(0)));
        Assert.NotEqual(key, FitCache.KeyOf(Options, "bank", senses, groups, labels, Features(0.001f)));
    }

    /// <summary>
    ///     The key depends on the settings that choose the thresholds: the precision target and the minimum number of answers.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FitCache_KeyOf_DependsOnTheThresholdSettings()
    {
        // Arrange
        string[] senses = ["bank#noun", "bank#verb"];
        int[] groups = [-1, -1];
        int[] labels = [0, 1];
        var key = FitCache.KeyOf(Options, "bank", senses, groups, labels, Features(0));

        // Act and Assert
        Assert.NotEqual(key, FitCache.KeyOf(Options with { TargetPrecision = 0.9 }, "bank", senses, groups, labels, Features(0)));
        Assert.NotEqual(key, FitCache.KeyOf(Options with { MinAnswered = 5 }, "bank", senses, groups, labels, Features(0)));
        Assert.Equal(key, FitCache.KeyOf(Options with { TargetPrecision = 0.95, MinAnswered = 3 }, "bank", senses, groups, labels, Features(0)));
    }

    /// <summary>
    ///     Saved fits come back bit-identical with both thresholds, and only the cache file is left behind.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FitCache_SaveAndOpen_RoundTripsBitIdentical()
    {
        // Arrange
        Filled();

        // Act
        var reopened = FitCache.Open(_dir.Root);
        var found = reopened.TryGet(K('B'), out var never);
        var other = reopened.TryGet(K('A'), out var first);

        // Assert
        Assert.Equal(3, reopened.Loaded);
        Assert.Equal(0, reopened.Discarded);
        Assert.True(found);
        Assert.True(other);
        Assert.Equal(Fit(2).Weights, never.Weights);
        Assert.Equal(Fit(2).Bias, never.Bias);
        Assert.Equal(LemmaWeights.Never, never.GroupThreshold);
        Assert.Equal(Fit(2).FineThreshold, never.FineThreshold);
        Assert.Equal(BitConverter.SingleToInt32Bits(Fit(1).Weights[2]), BitConverter.SingleToInt32Bits(first.Weights[2]));
        Assert.Equal(Fit(1).FineThreshold, first.FineThreshold);
        Assert.Equal(0.5f, first.GroupThreshold);
        Assert.Equal(3, first.Dimension);
        Assert.Equal([FitCache.FileName], Directory.GetFiles(_dir.Root).Select(Path.GetFileName));
    }

    /// <summary>
    ///     A save writes only the fits the run used, so the file does not grow with stale entries.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FitCache_Save_WritesOnlyUsedFits()
    {
        // Arrange
        Filled();
        var cache = FitCache.Open(_dir.Root);
        Assert.True(cache.TryGet(K('A'), out _));
        Assert.False(cache.TryGet(K('Z'), out _));

        // Act
        cache.Save();
        var reopened = FitCache.Open(_dir.Root);

        // Assert
        Assert.Equal(1, cache.Hits);
        Assert.Equal(1, cache.Misses);
        Assert.Equal(1, reopened.Loaded);
    }

    /// <summary>
    ///     A damaged entry and everything after it is dropped; the entries before it survive.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FitCache_Open_CorruptEntry_DropsItAndTheRest()
    {
        // Arrange
        var cache = Filled();
        var bytes = File.ReadAllBytes(cache.Path);
        bytes[^30] ^= 0x01;
        File.WriteAllBytes(cache.Path, bytes);

        // Act
        var reopened = FitCache.Open(_dir.Root);

        // Assert
        Assert.Equal(2, reopened.Loaded);
        Assert.Equal(1, reopened.Discarded);
        Assert.True(reopened.TryGet(K('A'), out _));
        Assert.False(reopened.TryGet(K('C'), out _));
    }

    /// <summary>
    ///     A cut-off file, a damaged header or arbitrary bytes never throw.
    /// </summary>
    /// <param name="mode">How the file is damaged.</param>
    [Theory]
    [InlineData("truncated")]
    [InlineData("garbage")]
    [InlineData("empty")]
    [InlineData("header")]
    [InlineData("version")]
    public void WordSenseBuilder_FitCache_Open_DamagedFile_IsHandled(string mode)
    {
        // Arrange
        var cache = Filled();
        var bytes = File.ReadAllBytes(cache.Path);
        bytes = mode switch
        {
            "truncated" => bytes[..(bytes.Length - 10)],
            "garbage" => [.. "this is not a cache"u8],
            "empty" => [],
            "version" => [.. bytes.Select((b, i) => i == 4 ? (byte)1 : b)],
            _ => [.. bytes.Select((b, i) => i == 6 ? (byte)(b ^ 0x7F) : b)],
        };
        File.WriteAllBytes(cache.Path, bytes);

        // Act
        var reopened = FitCache.Open(_dir.Root);

        // Assert
        Assert.Equal(mode == "truncated" ? 2 : 0, reopened.Loaded);
    }

    /// <summary>
    ///     A failed save leaves no temporary file behind.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FitCache_Save_Failure_LeavesNoTemporaryFile()
    {
        // Arrange
        Directory.CreateDirectory(Path.Join(_dir.Root, FitCache.FileName));
        var cache = FitCache.Open(_dir.Root);
        cache.Add(K('A'), Fit(1));

        // Act
        Assert.ThrowsAny<Exception>(cache.Save);

        // Assert
        Assert.Empty(Directory.GetFiles(_dir.Root));
    }
}
