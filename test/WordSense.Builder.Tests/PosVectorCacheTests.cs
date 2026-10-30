using System.Security.Cryptography;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the cache of pooled token vectors.
/// </summary>
public sealed class PosVectorCacheTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static PosTokenTable Table() => new(3, [1f, 2f, 3f, 0f, 0f, 0f, 4f, 5f, 6f], [0, 0, 1], [0, -1, 2], [true, false, true]);

    private static byte[] Digest(string runtime = "1") => PosVectorCache.ComputeDigest("aa", "bb", true, runtime);

    /// <summary>
    ///     A written file is read back with its vectors and flags, for the same digest and words only.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_PosVectorCache_WriteAndRead_RoundTripsForTheSameKey()
    {
        // Arrange: a cache file written for a digest and word hash
        var path = Path.Join(_dir.Root, "cache", "set.bin");
        var words = PosVectorCache.HashWords([["a", "b"], ["c"]]);
        PosVectorCache.Write(path, Digest(), words, Table());

        // Act: read it back with the same key
        var read = PosVectorCache.TryRead(path, Digest(), words, 3, 3);

        // Assert: the data round-trips, every mismatched key or missing file gives null, and no temp file remains
        Assert.NotNull(read);
        Assert.Equal(Table().Vectors, read.Value.Vectors);
        Assert.Equal([true, false, true], read.Value.HasVector);
        Assert.Null(PosVectorCache.TryRead(path, Digest("2"), words, 3, 3));
        Assert.Null(PosVectorCache.TryRead(path, Digest(), PosVectorCache.HashWords([["a"], ["b", "c"]]), 3, 3));
        Assert.Null(PosVectorCache.TryRead(path, Digest(), words, 3, 4));
        Assert.Null(PosVectorCache.TryRead(path, Digest(), words, 4, 3));
        Assert.Null(PosVectorCache.TryRead(Path.Join(_dir.Root, "missing.bin"), Digest(), words, 3, 3));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    /// <summary>
    ///     A damaged file, a truncated file and a file with another magic are not used.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_PosVectorCache_TryRead_DamagedFile_ReturnsNull()
    {
        // Arrange: a valid cache file and its bytes
        var path = Path.Join(_dir.Root, "set.bin");
        var words = PosVectorCache.HashWords([["a", "b"], ["c"]]);
        PosVectorCache.Write(path, Digest(), words, Table());
        var bytes = File.ReadAllBytes(path);

        // Act: damage the file by flipping a bit
        var flipped = (byte[])bytes.Clone();
        flipped[bytes.Length / 2] ^= 1;
        File.WriteAllBytes(path, flipped);

        // Assert: the flipped file is not used
        Assert.Null(PosVectorCache.TryRead(path, Digest(), words, 3, 3));

        // Act: truncate the file
        File.WriteAllBytes(path, bytes[..^3]);

        // Assert: the truncated file is not used
        Assert.Null(PosVectorCache.TryRead(path, Digest(), words, 3, 3));

        // Act: replace the magic
        var magic = (byte[])bytes.Clone();
        magic[0] = (byte)'X';
        File.WriteAllBytes(path, magic);

        // Assert: the file with another magic is not used
        Assert.Null(PosVectorCache.TryRead(path, Digest(), words, 3, 3));
    }

    /// <summary>
    ///     The digest depends on every part of the encoder identity, and the word hash on the sentence boundaries.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_PosVectorCache_Hashes_DependOnTheirInputs()
    {
        // Arrange: digest inputs that differ in one part each, and word lists with different sentence boundaries
        // Act / Assert: changed inputs change the hash, and a case-only change in the digest does not
        Assert.NotEqual(PosVectorCache.ComputeDigest("aa", "bb", true, "1"), PosVectorCache.ComputeDigest("ab", "bb", true, "1"));
        Assert.NotEqual(PosVectorCache.ComputeDigest("aa", "bb", true, "1"), PosVectorCache.ComputeDigest("aa", "bc", true, "1"));
        Assert.NotEqual(PosVectorCache.ComputeDigest("aa", "bb", true, "1"), PosVectorCache.ComputeDigest("aa", "bb", false, "1"));
        Assert.Equal(PosVectorCache.ComputeDigest("AA", "BB", true, "1"), PosVectorCache.ComputeDigest("aa", "bb", true, "1"));
        Assert.NotEqual(PosVectorCache.HashWords([["a", "b"]]), PosVectorCache.HashWords([["a"], ["b"]]));
        Assert.Equal(SHA256.HashSizeInBytes, PosVectorCache.HashWords([]).Length);
    }
}
