// Copyright (c) DEMA Consulting
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the persistent embedding cache and the caching encoder; a stub encoder replaces the ONNX model.
/// </summary>
public sealed class EmbeddingCacheTests : IDisposable
{
    private static readonly byte[] Digest = EmbeddingCache.ComputeDigest("aa", "bb", true, "1.0");
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static double[] Vector(int seed) => [seed + 0.1, -seed / 3.0, double.Epsilon * seed, Math.PI * seed];

    private static bool SameBits(double[] a, double[] b) =>
        a.Length == b.Length && a.Zip(b).All(p => BitConverter.DoubleToInt64Bits(p.First) == BitConverter.DoubleToInt64Bits(p.Second));

    private EmbeddingCache Filled(int count)
    {
        var cache = EmbeddingCache.Open(_dir.Root, Digest);
        for (var i = 0; i < count; i++)
        {
            cache.Add(EmbeddingCache.KeyOf(["a", "b", i.ToString(System.Globalization.CultureInfo.InvariantCulture)], 1), Vector(i));
        }

        cache.Save();
        return cache;
    }

    /// <summary>
    ///     The key depends on every word, on the word boundaries and on the target index.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_EmbeddingCache_KeyOf_DependsOnWordsAndTarget()
    {
        // Arrange
        string[] words = ["The", "bank", "is", "open"];

        // Act
        var key = EmbeddingCache.KeyOf(words, 1);

        // Assert
        Assert.Equal(key, EmbeddingCache.KeyOf(["The", "bank", "is", "open"], 1));
        Assert.NotEqual(key, EmbeddingCache.KeyOf(words, 2));
        Assert.NotEqual(key, EmbeddingCache.KeyOf(["the", "bank", "is", "open"], 1));
        Assert.NotEqual(EmbeddingCache.KeyOf(["ab", "c"], 0), EmbeddingCache.KeyOf(["a", "bc"], 0));
        Assert.NotEqual(EmbeddingCache.KeyOf(["a"], 0), EmbeddingCache.KeyOf(["a", string.Empty], 0));
    }

    /// <summary>
    ///     The digest changes with each model file and with the casing.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_EmbeddingCache_ComputeDigest_DependsOnEveryInput()
    {
        // Arrange
        var digest = EmbeddingCache.ComputeDigest("aa", "bb", true, "1.0");

        // Act and Assert
        Assert.Equal(32, digest.Length);
        Assert.Equal(digest, EmbeddingCache.ComputeDigest("AA", "BB", true, "1.0"));
        Assert.NotEqual(digest, EmbeddingCache.ComputeDigest("ab", "bb", true, "1.0"));
        Assert.NotEqual(digest, EmbeddingCache.ComputeDigest("aa", "bc", true, "1.0"));
        Assert.NotEqual(digest, EmbeddingCache.ComputeDigest("aa", "bb", false, "1.0"));
        Assert.NotEqual(digest, EmbeddingCache.ComputeDigest("aa", "bb", true, "1.1"));
    }

    /// <summary>
    ///     Saved vectors come back bit-identical, and nothing but the cache file is left behind.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_EmbeddingCache_SaveAndOpen_RoundTripsBitIdentical()
    {
        // Arrange
        Filled(5);

        // Act
        var reopened = EmbeddingCache.Open(_dir.Root, Digest);

        // Assert
        Assert.Equal(5, reopened.Count);
        Assert.Equal(5, reopened.Loaded);
        Assert.Equal(0, reopened.Discarded);
        Assert.False(reopened.Invalidated);
        for (var i = 0; i < 5; i++)
        {
            Assert.True(reopened.TryGet(EmbeddingCache.KeyOf(["a", "b", i.ToString(System.Globalization.CultureInfo.InvariantCulture)], 1), out var vector));
            Assert.True(SameBits(Vector(i), vector));
        }

        Assert.Equal([EmbeddingCache.FileName], Directory.GetFiles(_dir.Root).Select(Path.GetFileName));
    }

    /// <summary>
    ///     A missing cache file gives an empty cache.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_EmbeddingCache_Open_MissingFile_IsEmpty()
    {
        // Act
        var cache = EmbeddingCache.Open(Path.Join(_dir.Root, "none"), Digest);

        // Assert
        Assert.Equal(0, cache.Count);
        Assert.False(cache.Invalidated);
        Assert.False(cache.TryGet("00", out _));
    }

    /// <summary>
    ///     A cache written for other model files is dropped as a whole and replaced on the next save.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_EmbeddingCache_Open_OtherModelDigest_IsInvalidated()
    {
        // Arrange
        var written = Filled(3);

        // Act
        var other = EmbeddingCache.Open(_dir.Root, EmbeddingCache.ComputeDigest("cc", "bb", true, "1.0"));
        other.Add(EmbeddingCache.KeyOf(["x"], 0), Vector(9));
        other.Save();
        var original = EmbeddingCache.Open(_dir.Root, Digest);

        // Assert
        Assert.Equal(3, written.Count);
        Assert.True(other.Invalidated);
        Assert.Equal(0, other.Loaded);
        Assert.True(original.Invalidated);
        Assert.Equal(0, original.Count);
    }

    /// <summary>
    ///     A damaged entry is dropped and the others survive.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_EmbeddingCache_Open_CorruptEntry_IsDiscardedAndOthersSurvive()
    {
        // Arrange
        var cache = Filled(4);
        var bytes = File.ReadAllBytes(cache.Path);
        var recordSize = 32 + (4 * 8) + 8;
        var headerSize = bytes.Length - (4 * recordSize);
        bytes[headerSize + recordSize + 40] ^= 0x01;
        File.WriteAllBytes(cache.Path, bytes);

        // Act
        var reopened = EmbeddingCache.Open(_dir.Root, Digest);

        // Assert
        Assert.Equal(3, reopened.Count);
        Assert.Equal(1, reopened.Discarded);
        Assert.False(reopened.Invalidated);
    }

    /// <summary>
    ///     A cut-off file keeps the complete entries and counts the missing ones as discarded.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_EmbeddingCache_Open_TruncatedFile_KeepsCompleteEntries()
    {
        // Arrange
        var cache = Filled(4);
        var bytes = File.ReadAllBytes(cache.Path);
        File.WriteAllBytes(cache.Path, bytes[..(bytes.Length - 10)]);

        // Act
        var reopened = EmbeddingCache.Open(_dir.Root, Digest);

        // Assert
        Assert.Equal(3, reopened.Count);
        Assert.Equal(1, reopened.Discarded);
    }

    /// <summary>
    ///     A damaged header or arbitrary bytes never throw; the cache is empty and invalidated.
    /// </summary>
    /// <param name="mode">How the file is damaged.</param>
    [Theory]
    [InlineData("garbage")]
    [InlineData("empty")]
    [InlineData("header")]
    public void WordSenseBuilder_EmbeddingCache_Open_DamagedHeader_IsEmptyAndInvalidated(string mode)
    {
        // Arrange
        var cache = Filled(2);
        var bytes = File.ReadAllBytes(cache.Path);
        switch (mode)
        {
            case "garbage":
                bytes = [.. "this is not a cache"u8];
                break;
            case "empty":
                bytes = [];
                break;
            default:
                bytes[45] ^= 0x7F;
                break;
        }

        File.WriteAllBytes(cache.Path, bytes);

        // Act
        var reopened = EmbeddingCache.Open(_dir.Root, Digest);

        // Assert
        Assert.Equal(0, reopened.Count);
        Assert.True(reopened.Invalidated);
    }

    /// <summary>
    ///     A vector of another length than the cached ones is refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_EmbeddingCache_Add_OtherDimension_Throws()
    {
        // Arrange
        var cache = EmbeddingCache.Open(_dir.Root, Digest);
        cache.Add("01", [1.0, 2.0]);

        // Act and Assert
        Assert.Throws<InvalidDataException>(() => cache.Add("02", [1.0]));
    }

    /// <summary>
    ///     A save that fails leaves no temporary file behind and the cache stays dirty.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_EmbeddingCache_Save_Failure_LeavesNoTemporaryFile()
    {
        // Arrange
        Directory.CreateDirectory(Path.Join(_dir.Root, EmbeddingCache.FileName));
        var cache = EmbeddingCache.Open(_dir.Root, Digest);
        cache.Add(EmbeddingCache.KeyOf(["new"], 0), Vector(7));

        // Act
        Assert.ThrowsAny<Exception>(cache.Save);

        // Assert
        Assert.Empty(Directory.GetFiles(_dir.Root));
        Assert.True(cache.IsDirty);
    }

    /// <summary>
    ///     The caching encoder encodes each sentence once and returns the exact vectors afterwards.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_CachingEncoder_EncodeAsync_SecondCallIsAHitAndBitIdentical()
    {
        // Arrange
        var calls = 0;
        var encoder = new CachingEncoder(
            (words, targets, _) =>
            {
                calls++;
                return ValueTask.FromResult<double[]?[]>([Vector(words.Count + targets[0])]);
            },
            EmbeddingCache.Open(_dir.Root, Digest));
        string[] words = ["a", "b", "c"];

        // Act
        var first = await encoder.EncodeAsync(words, 1, TestContext.Current.CancellationToken);
        var second = await encoder.EncodeAsync(words, 1, TestContext.Current.CancellationToken);
        var other = await encoder.EncodeAsync(words, 2, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(other);
        Assert.True(SameBits(first, second));
        Assert.Equal(2, calls);
        Assert.Equal(1, encoder.Hits);
        Assert.Equal(2, encoder.Misses);
    }

    /// <summary>
    ///     Without a cache every sentence is encoded; a target without tokens is never cached.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_CachingEncoder_EncodeAsync_NoCacheOrNoTokens_AlwaysEncodes()
    {
        // Arrange
        var calls = 0;
        var plain = new CachingEncoder((_, _, _) =>
        {
            calls++;
            return ValueTask.FromResult<double[]?[]>([Vector(1)]);
        }, null);
        var cache = EmbeddingCache.Open(_dir.Root, Digest);
        var empty = new CachingEncoder((_, _, _) => ValueTask.FromResult<double[]?[]>([null]), cache);

        // Act
        await plain.EncodeAsync(["a"], 0, TestContext.Current.CancellationToken);
        await plain.EncodeAsync(["a"], 0, TestContext.Current.CancellationToken);
        var none = await empty.EncodeAsync(["a"], 0, TestContext.Current.CancellationToken);
        await empty.EncodeAsync(["a"], 0, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, calls);
        Assert.Equal(0, plain.Hits);
        Assert.Null(none);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, empty.Hits);
    }
}
