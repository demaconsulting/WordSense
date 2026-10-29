using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     A persistent cache of the pooled encoder vectors of every token of one set of sentences, so that training a
///     part-of-speech head and selecting its settings encode each set once.
/// </summary>
/// <remarks>
///     <para>
///     The vectors are the output of <c>OnnxEncoder.EncodeAllAsync</c>, which is deterministic for given model files and
///     inference runtime, so a file is valid for exactly the digest it was written with (the encoder file, the
///     vocabulary, the casing, the version of ONNX Runtime and <see cref="FeatureVersion"/>) and for the exact word
///     sequence of the set. A file that does not match is encoded again. The vectors are stored as 32-bit floats, which
///     is the precision the head reads them in.
///     </para>
///     <para>
///     Format (little-endian): the magic <c>WSPV</c>, the digest (32 bytes), the word hash (32 bytes, the SHA-256 of the
///     words of all sentences with separators), the hidden size and the token count as 32-bit integers, the vectors
///     (<c>count × size</c> floats), one byte per token that says whether it has a vector, and the SHA-256 of everything
///     before it. The file is written beside the target and renamed, so a failed save never damages an older file.
///     </para>
/// </remarks>
internal static class PosVectorCache
{
    /// <summary>
    ///     The version of the feature definition (the pooling, the window rule and the possessive view of the encoder);
    ///     changing it invalidates every cache file.
    /// </summary>
    public const int FeatureVersion = 1;

    private const int HashSize = 32;
    private static readonly byte[] Magic = "WSPV"u8.ToArray();

    /// <summary>
    ///     Computes the digest that identifies the encoder files and the runtime of the vectors.
    /// </summary>
    /// <param name="onnxSha256">The SHA-256 of the ONNX file, as hexadecimal text.</param>
    /// <param name="vocabularySha256">The SHA-256 of the vocabulary file, as hexadecimal text.</param>
    /// <param name="lowerCase">True when the model lower-cases its input.</param>
    /// <param name="runtime">The version of the inference runtime.</param>
    /// <returns>The 32-byte digest.</returns>
    public static byte[] ComputeDigest(string onnxSha256, string vocabularySha256, bool lowerCase, string runtime) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"pos-vectors={FeatureVersion}\nonnx={onnxSha256.ToLowerInvariant()}\nvocabulary={vocabularySha256.ToLowerInvariant()}\nlowercase={lowerCase}\nruntime={runtime}\n"));

    /// <summary>
    ///     Computes the hash of the word sequence of a set.
    /// </summary>
    /// <param name="sentences">The words of every sentence.</param>
    /// <returns>The 32-byte hash.</returns>
    public static byte[] HashWords(IEnumerable<string[]> sentences)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var sentence in sentences)
        {
            foreach (var word in sentence)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(word));
                hash.AppendData([0]);
            }

            hash.AppendData([1]);
        }

        return hash.GetHashAndReset();
    }

    /// <summary>
    ///     Reads the vectors of a set from the cache file.
    /// </summary>
    /// <param name="path">The cache file.</param>
    /// <param name="digest">The digest of the encoder files.</param>
    /// <param name="wordHash">The hash of the word sequence of the set.</param>
    /// <param name="dimension">The hidden size.</param>
    /// <param name="count">The number of tokens of the set.</param>
    /// <returns>The vectors and the vector flags, or null when the file does not exist or does not match.</returns>
    public static (float[] Vectors, bool[] HasVector)? TryRead(string path, byte[] digest, byte[] wordHash, int dimension, int count)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        var headerSize = Magic.Length + HashSize + HashSize + 8;
        var expected = headerSize + ((long)count * dimension * sizeof(float)) + count + HashSize;
        if (bytes.Length != expected || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            return null;
        }

        var span = bytes.AsSpan();
        if (!SHA256.HashData(span[..^HashSize]).AsSpan().SequenceEqual(span[^HashSize..])
            || !span.Slice(Magic.Length, HashSize).SequenceEqual(digest)
            || !span.Slice(Magic.Length + HashSize, HashSize).SequenceEqual(wordHash)
            || BinaryPrimitives.ReadInt32LittleEndian(span.Slice(Magic.Length + (2 * HashSize), 4)) != dimension
            || BinaryPrimitives.ReadInt32LittleEndian(span.Slice(Magic.Length + (2 * HashSize) + 4, 4)) != count)
        {
            return null;
        }

        var vectors = new float[count * dimension];
        var floats = span.Slice(headerSize, vectors.Length * sizeof(float));
        for (var i = 0; i < vectors.Length; i++)
        {
            vectors[i] = BinaryPrimitives.ReadSingleLittleEndian(floats.Slice(i * sizeof(float), sizeof(float)));
        }

        var flags = span.Slice(headerSize + floats.Length, count);
        var has = new bool[count];
        for (var i = 0; i < count; i++)
        {
            has[i] = flags[i] != 0;
        }

        return (vectors, has);
    }

    /// <summary>
    ///     Writes the vectors of a set to the cache file.
    /// </summary>
    /// <param name="path">The cache file.</param>
    /// <param name="digest">The digest of the encoder files.</param>
    /// <param name="wordHash">The hash of the word sequence of the set.</param>
    /// <param name="table">The vectors.</param>
    public static void Write(string path, byte[] digest, byte[] wordHash, PosTokenTable table)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(table);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                void Emit(ReadOnlySpan<byte> data)
                {
                    hash.AppendData(data);
                    file.Write(data);
                }

                Emit(Magic);
                Emit(digest);
                Emit(wordHash);
                var number = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(number, table.Dimension);
                Emit(number);
                BinaryPrimitives.WriteInt32LittleEndian(number, table.Count);
                Emit(number);
                var buffer = new byte[table.Dimension * sizeof(float)];
                for (var t = 0; t < table.Count; t++)
                {
                    for (var j = 0; j < table.Dimension; j++)
                    {
                        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(j * sizeof(float), sizeof(float)), table.Vectors[(t * table.Dimension) + j]);
                    }

                    Emit(buffer);
                }

                Emit([.. table.HasVector.Select(h => h ? (byte)1 : (byte)0)]);
                file.Write(hash.GetHashAndReset());
            }

            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
