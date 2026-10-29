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
        var path = Path.Join(_dir.Root, "cache", "set.bin");
        var words = PosVectorCache.HashWords([["a", "b"], ["c"]]);
        PosVectorCache.Write(path, Digest(), words, Table());

        var read = PosVectorCache.TryRead(path, Digest(), words, 3, 3);

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
        var path = Path.Join(_dir.Root, "set.bin");
        var words = PosVectorCache.HashWords([["a", "b"], ["c"]]);
        PosVectorCache.Write(path, Digest(), words, Table());
        var bytes = File.ReadAllBytes(path);

        var flipped = (byte[])bytes.Clone();
        flipped[bytes.Length / 2] ^= 1;
        File.WriteAllBytes(path, flipped);
        Assert.Null(PosVectorCache.TryRead(path, Digest(), words, 3, 3));

        File.WriteAllBytes(path, bytes[..^3]);
        Assert.Null(PosVectorCache.TryRead(path, Digest(), words, 3, 3));

        var magic = (byte[])bytes.Clone();
        magic[0] = (byte)'X';
        File.WriteAllBytes(path, magic);
        Assert.Null(PosVectorCache.TryRead(path, Digest(), words, 3, 3));
    }

    /// <summary>
    ///     The digest depends on every part of the encoder identity, and the word hash on the sentence boundaries.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_PosVectorCache_Hashes_DependOnTheirInputs()
    {
        Assert.NotEqual(PosVectorCache.ComputeDigest("aa", "bb", true, "1"), PosVectorCache.ComputeDigest("ab", "bb", true, "1"));
        Assert.NotEqual(PosVectorCache.ComputeDigest("aa", "bb", true, "1"), PosVectorCache.ComputeDigest("aa", "bc", true, "1"));
        Assert.NotEqual(PosVectorCache.ComputeDigest("aa", "bb", true, "1"), PosVectorCache.ComputeDigest("aa", "bb", false, "1"));
        Assert.Equal(PosVectorCache.ComputeDigest("AA", "BB", true, "1"), PosVectorCache.ComputeDigest("aa", "bb", true, "1"));
        Assert.NotEqual(PosVectorCache.HashWords([["a", "b"]]), PosVectorCache.HashWords([["a"], ["b"]]));
        Assert.Equal(SHA256.HashSizeInBytes, PosVectorCache.HashWords([]).Length);
    }
}
