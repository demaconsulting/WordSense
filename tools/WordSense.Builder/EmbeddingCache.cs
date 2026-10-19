using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     A persistent cache of the pooled target-word vectors an encoder returned, so that training encodes only the
///     sentences it has not seen before.
/// </summary>
/// <remarks>
///     <para>
///     The encoder is deterministic for a given set of model files and inference runtime, so an entry is valid for exactly
///     the digest the cache was opened with (the model file, the vocabulary, the casing and the version of ONNX Runtime,
///     whose kernels change the low digits of the output). The key of an entry is the SHA-256 of the exact word sequence
///     and the target word index. The vectors are stored as the exact 64-bit floating-point numbers the encoder returned,
///     so a hit is bit-identical to a fresh encode.
///     </para>
///     <para>
///     One file holds the whole cache of one model. Format (little-endian): the the four magic bytes (ASCII W, S, E, C) and a version byte;
///     the 32-byte digest of the model files; the vector dimension and the entry count as 32-bit integers; the first 8
///     bytes of the SHA-256 of all these header bytes; then per entry the 32-byte key, the vector and the first 8 bytes of
///     the SHA-256 of the key and the vector. An entry whose checksum does not match, or that is cut off by the end of the
///     file, is discarded and encoded again; a header that is corrupt or names another digest discards the whole file.
///     The file is replaced atomically (written beside the target, then renamed), so a failed save never damages the
///     previous cache.
///     </para>
///     <para>
///     The cache holds nothing but the outputs of the encoder, so a change of the encoder code (pooling, tokenization)
///     that keeps the model files requires <c>--no-cache</c> once, or deleting the cache folder; changing the version
///     constant here has the same effect.
///     </para>
/// </remarks>
internal sealed class EmbeddingCache
{
    /// <summary>
    ///     The name of the cache file in the cache directory.
    /// </summary>
    public const string FileName = "embeddings.bin";

    private const byte Version = 1;
    private const int DigestSize = 32;
    private const int KeySize = 32;
    private const int ChecksumSize = 8;
    private const int MaxDimension = 65536;
    private static readonly byte[] Magic = [(byte)'W', (byte)'S', (byte)'E', (byte)'C'];
    private static readonly int HeaderSize = Magic.Length + 1 + DigestSize + 4 + 4 + ChecksumSize;

    private readonly Dictionary<string, double[]> _entries = new(StringComparer.Ordinal);
    private readonly byte[] _digest;
    private int _dimension;

    private EmbeddingCache(string directory, byte[] digest)
    {
        Directory = directory;
        _digest = digest;
    }

    /// <summary>
    ///     Gets the directory of the cache.
    /// </summary>
    public string Directory { get; }

    /// <summary>
    ///     Gets the path of the cache file.
    /// </summary>
    public string Path => System.IO.Path.Join(Directory, FileName);

    /// <summary>
    ///     Gets the number of entries.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    ///     Gets the number of entries that were valid when the file was loaded.
    /// </summary>
    public int Loaded { get; private set; }

    /// <summary>
    ///     Gets the number of damaged entries that were dropped when the file was loaded.
    /// </summary>
    public int Discarded { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether an existing file was dropped as a whole, because it was unreadable or was
    ///     written for other model files.
    /// </summary>
    public bool Invalidated { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether entries were added since the last load or save.
    /// </summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    ///     Computes the digest that identifies the files and the runtime an encoder runs on.
    /// </summary>
    /// <param name="onnxSha256">The SHA-256 of the ONNX model file, as hexadecimal text.</param>
    /// <param name="vocabularySha256">The SHA-256 of the vocabulary file, as hexadecimal text.</param>
    /// <param name="lowerCase">True when the model lower-cases its input.</param>
    /// <param name="runtime">The version of the inference runtime; its kernels decide the exact numbers of the output.</param>
    /// <returns>The 32-byte digest.</returns>
    public static byte[] ComputeDigest(string onnxSha256, string vocabularySha256, bool lowerCase, string runtime) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"onnx={onnxSha256.ToLowerInvariant()}\nvocabulary={vocabularySha256.ToLowerInvariant()}\nlowercase={lowerCase}\nruntime={runtime}\n"));

    /// <summary>
    ///     Computes the key of a word sequence and a target word.
    /// </summary>
    /// <param name="words">The exact words of the sentence.</param>
    /// <param name="target">The index of the target word.</param>
    /// <returns>The key as hexadecimal text.</returns>
    public static string KeyOf(IReadOnlyList<string> words, int target)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(number, words.Count);
        hash.AppendData(number);
        foreach (var word in words)
        {
            var bytes = Encoding.UTF8.GetBytes(word);
            BinaryPrimitives.WriteInt32LittleEndian(number, bytes.Length);
            hash.AppendData(number);
            hash.AppendData(bytes);
        }

        BinaryPrimitives.WriteInt32LittleEndian(number, target);
        hash.AppendData(number);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>
    ///     Opens the cache of a directory, loading every valid entry; a missing, stale or damaged file gives an empty
    ///     cache and never throws.
    /// </summary>
    /// <param name="directory">The cache directory of one model.</param>
    /// <param name="digest">The digest of the model files.</param>
    /// <returns>The cache.</returns>
    public static EmbeddingCache Open(string directory, byte[] digest)
    {
        var cache = new EmbeddingCache(directory, digest);
        try
        {
            if (File.Exists(cache.Path))
            {
                cache.Load(File.ReadAllBytes(cache.Path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            cache.Reset();
            cache.Invalidated = true;
        }

        return cache;
    }

    /// <summary>
    ///     Looks up the vector of a key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="vector">Receives the vector.</param>
    /// <returns>True when the key is cached.</returns>
    public bool TryGet(string key, out double[] vector) => _entries.TryGetValue(key, out vector!);

    /// <summary>
    ///     Adds or replaces the vector of a key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="vector">The vector; all vectors of a cache have one length.</param>
    /// <exception cref="InvalidDataException">Thrown when the vector length differs from the other entries.</exception>
    public void Add(string key, double[] vector)
    {
        if (_dimension == 0)
        {
            _dimension = vector.Length;
        }

        if (vector.Length != _dimension || vector.Length == 0 || vector.Length > MaxDimension)
        {
            throw new InvalidDataException($"A vector of {vector.Length} numbers does not fit a cache of {_dimension}-number vectors.");
        }

        _entries[key] = vector;
        IsDirty = true;
    }

    /// <summary>
    ///     Writes the cache atomically: to a temporary file beside the cache file, then renamed over it.
    /// </summary>
    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var temporary = System.IO.Path.Join(Directory, $"{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20))
            {
                Write(stream);
            }

            File.Move(temporary, Path, true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }

        IsDirty = false;
    }

    private static byte[] Checksum(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(first);
        hash.AppendData(second);
        return hash.GetHashAndReset().AsSpan(0, ChecksumSize).ToArray();
    }

    private void Write(Stream stream)
    {
        var header = new byte[HeaderSize - ChecksumSize];
        Magic.CopyTo(header, 0);
        header[Magic.Length] = Version;
        _digest.CopyTo(header, Magic.Length + 1);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(Magic.Length + 1 + DigestSize), _dimension);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(Magic.Length + 1 + DigestSize + 4), _entries.Count);
        stream.Write(header);
        stream.Write(Checksum(header));

        var record = new byte[KeySize + (_dimension * sizeof(double))];
        foreach (var pair in _entries.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            Convert.FromHexString(pair.Key).CopyTo(record, 0);
            for (var d = 0; d < _dimension; d++)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(record.AsSpan(KeySize + (d * sizeof(double))), pair.Value[d]);
            }

            stream.Write(record);
            stream.Write(Checksum(record));
        }
    }

    private void Reset()
    {
        _entries.Clear();
        _dimension = 0;
        Loaded = 0;
        Discarded = 0;
    }

    private void Load(byte[] file)
    {
        var span = file.AsSpan();
        if (span.Length < HeaderSize
            || !span[..Magic.Length].SequenceEqual(Magic)
            || span[Magic.Length] != Version
            || !Checksum(span[..(HeaderSize - ChecksumSize)]).AsSpan().SequenceEqual(span.Slice(HeaderSize - ChecksumSize, ChecksumSize))
            || !span.Slice(Magic.Length + 1, DigestSize).SequenceEqual(_digest))
        {
            Invalidated = true;
            return;
        }

        var dimension = BinaryPrimitives.ReadInt32LittleEndian(span[(Magic.Length + 1 + DigestSize)..]);
        var count = BinaryPrimitives.ReadInt32LittleEndian(span[(Magic.Length + 1 + DigestSize + 4)..]);
        if (dimension < 0 || dimension > MaxDimension || count < 0 || (count > 0 && dimension == 0))
        {
            Invalidated = true;
            return;
        }

        _dimension = dimension;
        var recordSize = KeySize + (dimension * sizeof(double)) + ChecksumSize;
        var position = HeaderSize;
        var read = 0;
        while (read < count && position + recordSize <= span.Length)
        {
            var body = span.Slice(position, recordSize - ChecksumSize);
            if (Checksum(body).AsSpan().SequenceEqual(span.Slice(position + recordSize - ChecksumSize, ChecksumSize)))
            {
                var vector = new double[dimension];
                for (var d = 0; d < dimension; d++)
                {
                    vector[d] = BinaryPrimitives.ReadDoubleLittleEndian(body[(KeySize + (d * sizeof(double)))..]);
                }

                _entries[Convert.ToHexString(body[..KeySize])] = vector;
            }
            else
            {
                Discarded++;
            }

            position += recordSize;
            read++;
        }

        // Entries the header promises but the file lacks (a cut-off file) are lost as well
        Discarded += count - read;
        Loaded = _entries.Count;
    }
}
