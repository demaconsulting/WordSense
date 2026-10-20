using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     The result of fitting the classifier of one lemma.
/// </summary>
/// <param name="Weights">The row-major weights, one row per sense.</param>
/// <param name="Bias">One bias per row.</param>
/// <param name="FineThreshold">The fine threshold chosen by cross-validation, or <c>LemmaWeights.Never</c>.</param>
/// <param name="GroupThreshold">The group threshold chosen by cross-validation, or <c>LemmaWeights.Never</c>.</param>
/// <param name="Dimension">The feature dimension.</param>
internal sealed record LemmaFit(float[] Weights, float[] Bias, float FineThreshold, float GroupThreshold, int Dimension);

/// <summary>
///     A persistent cache of the fitted classifier of each lemma, so that a retraining fits only the lemmas whose
///     features, labels or training settings changed.
/// </summary>
/// <remarks>
///     <para>
///     The key of a fit is the SHA-256 of everything the fit and its thresholds depend on: the training and threshold
///     settings (C, the folds, the precision target and the minimum number of answers), the lemma (which seeds the
///     folds), its sense ids, the group index of each sense, the label of each sentence and the exact feature vectors
///     (which already include the global mean). A lemma whose anchors, mean and settings are unchanged therefore always
///     finds its previous fit, and any change of the features, such as another mean, another encoder or another
///     runtime, simply finds nothing. The fit and the thresholds are deterministic, so a cached fit is bit-identical to
///     a fresh one.
///     </para>
///     <para>
///     One file holds the fits of the last run (entries not used by a run are not written again, so the file does not
///     grow). Format version 2 (little-endian): the four magic bytes (ASCII W, S, F, C) and a version byte, the entry
///     count as a 32-bit integer and the first 8 bytes of the SHA-256 of these bytes; then per entry the 32-byte key,
///     the dimension and the row count as 32-bit integers, the fine and the group threshold, the biases and the weights
///     as 32-bit floats, and the first 8 bytes of the SHA-256 of the entry. The first damaged entry ends the reading
///     (the entries are of variable length), and everything after it is fitted again. A file of another version is
///     ignored. The file is replaced atomically. The members are safe to call from several threads.
///     </para>
/// </remarks>
internal sealed class FitCache
{
    /// <summary>
    ///     The name of the cache file in the cache directory.
    /// </summary>
    public const string FileName = "fits.bin";

    private const byte Version = 2;
    private const int KeySize = 32;
    private const int ChecksumSize = 8;
    private const int EntryFixedSize = KeySize + 4 + 4 + 4 + 4;
    private const int MaxRows = 4096;
    private const int MaxDimension = 65536;
    private static readonly byte[] Magic = [(byte)'W', (byte)'S', (byte)'F', (byte)'C'];
    private static readonly int HeaderSize = Magic.Length + 1 + 4 + ChecksumSize;

    private readonly Dictionary<string, LemmaFit> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _hits;
    private int _misses;

    private FitCache(string directory)
    {
        Directory = directory;
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
    ///     Gets the number of fits that were valid when the file was loaded.
    /// </summary>
    public int Loaded { get; private set; }

    /// <summary>
    ///     Gets the number of damaged or missing entries that were dropped when the file was loaded.
    /// </summary>
    public int Discarded { get; private set; }

    /// <summary>
    ///     Gets the number of lemmas answered from the cache.
    /// </summary>
    public int Hits => Volatile.Read(ref _hits);

    /// <summary>
    ///     Gets the number of lemmas that were fitted.
    /// </summary>
    public int Misses => Volatile.Read(ref _misses);

    /// <summary>
    ///     Computes the key of one lemma fit.
    /// </summary>
    /// <param name="options">The training and threshold settings that influence the fit.</param>
    /// <param name="lemma">The lemma.</param>
    /// <param name="senseIds">The sense ids of the lemma, in column order.</param>
    /// <param name="groups">The group index of each column, or -1 for a sense that is in no group.</param>
    /// <param name="labels">The column index of each feature.</param>
    /// <param name="features">The features.</param>
    /// <returns>The key as hexadecimal text.</returns>
    public static string KeyOf(TrainingOptions options, string lemma, IReadOnlyList<string> senseIds, IReadOnlyList<int> groups, int[] labels, float[][] features)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var number = new byte[8];
        void Append(long value)
        {
            BinaryPrimitives.WriteInt64LittleEndian(number, value);
            hash.AppendData(number);
        }

        void AppendText(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            Append(bytes.Length);
            hash.AppendData(bytes);
        }

        Append(Version);
        Append(BitConverter.DoubleToInt64Bits(options.C));
        Append(options.Folds);
        Append(BitConverter.DoubleToInt64Bits(options.TargetPrecision));
        Append(options.MinAnswered);
        AppendText(lemma);
        Append(senseIds.Count);
        foreach (var senseId in senseIds)
        {
            AppendText(senseId);
        }

        Append(groups.Count);
        foreach (var group in groups)
        {
            Append(group);
        }

        Append(labels.Length);
        foreach (var label in labels)
        {
            Append(label);
        }

        Append(features.Length);
        foreach (var feature in features)
        {
            Append(feature.Length);
            hash.AppendData(MemoryMarshal.AsBytes(feature.AsSpan()));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>
    ///     Opens the cache of a directory, loading every valid entry; a missing or damaged file gives an empty cache and
    ///     never throws.
    /// </summary>
    /// <param name="directory">The cache directory of one model.</param>
    /// <returns>The cache.</returns>
    public static FitCache Open(string directory)
    {
        var cache = new FitCache(directory);
        try
        {
            if (File.Exists(cache.Path))
            {
                cache.Load(File.ReadAllBytes(cache.Path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            cache._entries.Clear();
            cache.Loaded = 0;
        }

        return cache;
    }

    /// <summary>
    ///     Looks up the fit of a key and counts a hit or, when it is absent, a miss.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="fit">Receives the fit.</param>
    /// <returns>True when the key is cached.</returns>
    public bool TryGet(string key, out LemmaFit fit)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out fit!))
            {
                _used.Add(key);
                _hits++;
                return true;
            }

            _misses++;
            return false;
        }
    }

    /// <summary>
    ///     Adds the fit of a key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="fit">The fit.</param>
    public void Add(string key, LemmaFit fit)
    {
        lock (_gate)
        {
            _entries[key] = fit;
            _used.Add(key);
        }
    }

    /// <summary>
    ///     Writes the fits used in this run atomically: to a temporary file beside the cache file, then renamed over it.
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
    }

    private static byte[] Checksum(ReadOnlySpan<byte> data) => SHA256.HashData(data)[..ChecksumSize];

    private void Write(Stream stream)
    {
        List<KeyValuePair<string, LemmaFit>> entries;
        lock (_gate)
        {
            entries = _entries.Where(p => _used.Contains(p.Key)).OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        }

        var header = new byte[HeaderSize - ChecksumSize];
        Magic.CopyTo(header, 0);
        header[Magic.Length] = Version;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(Magic.Length + 1), entries.Count);
        stream.Write(header);
        stream.Write(Checksum(header));
        foreach (var (key, fit) in entries)
        {
            var rows = fit.Bias.Length;
            var record = new byte[EntryFixedSize + (rows * sizeof(float)) + (fit.Weights.Length * sizeof(float))];
            Convert.FromHexString(key).CopyTo(record, 0);
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(KeySize), fit.Dimension);
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(KeySize + 4), rows);
            BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(KeySize + 8), fit.FineThreshold);
            BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(KeySize + 12), fit.GroupThreshold);
            var position = EntryFixedSize;
            foreach (var value in fit.Bias.Concat(fit.Weights))
            {
                BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(position), value);
                position += sizeof(float);
            }

            stream.Write(record);
            stream.Write(Checksum(record));
        }
    }

    private void Load(byte[] file)
    {
        var span = file.AsSpan();
        if (span.Length < HeaderSize
            || !span[..Magic.Length].SequenceEqual(Magic)
            || span[Magic.Length] != Version
            || !Checksum(span[..(HeaderSize - ChecksumSize)]).AsSpan().SequenceEqual(span.Slice(HeaderSize - ChecksumSize, ChecksumSize)))
        {
            return;
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(span[(Magic.Length + 1)..]);
        var position = HeaderSize;
        var read = 0;
        while (read < count && TryReadEntry(span, ref position, out var key, out var fit))
        {
            _entries[key] = fit;
            read++;
        }

        // The entries after a damaged one cannot be located, so they are fitted again
        Discarded = Math.Max(0, count) - read;
        Loaded = read;
    }

    private static bool TryReadEntry(ReadOnlySpan<byte> span, ref int position, out string key, out LemmaFit fit)
    {
        key = string.Empty;
        fit = null!;
        if (position + EntryFixedSize > span.Length)
        {
            return false;
        }

        var entry = span[position..];
        var dimension = BinaryPrimitives.ReadInt32LittleEndian(entry[KeySize..]);
        var rows = BinaryPrimitives.ReadInt32LittleEndian(entry[(KeySize + 4)..]);
        if (dimension is < 1 or > MaxDimension || rows is < 2 or > MaxRows)
        {
            return false;
        }

        var size = EntryFixedSize + (((long)rows + ((long)rows * dimension)) * sizeof(float));
        if (position + size + ChecksumSize > span.Length)
        {
            return false;
        }

        var body = entry[..(int)size];
        if (!Checksum(body).AsSpan().SequenceEqual(entry.Slice((int)size, ChecksumSize)))
        {
            return false;
        }

        var bias = new float[rows];
        var weights = new float[rows * dimension];
        var offset = EntryFixedSize;
        for (var i = 0; i < bias.Length; i++, offset += sizeof(float))
        {
            bias[i] = BinaryPrimitives.ReadSingleLittleEndian(body[offset..]);
        }

        for (var i = 0; i < weights.Length; i++, offset += sizeof(float))
        {
            weights[i] = BinaryPrimitives.ReadSingleLittleEndian(body[offset..]);
        }

        key = Convert.ToHexString(body[..KeySize]);
        var fine = BinaryPrimitives.ReadSingleLittleEndian(body[(KeySize + 8)..]);
        var group = BinaryPrimitives.ReadSingleLittleEndian(body[(KeySize + 12)..]);

        // A threshold that the weights reader would refuse makes the entry damaged
        if (!float.IsFinite(fine) || fine < 0 || !float.IsFinite(group) || group < 0 || weights.Any(w => !float.IsFinite(w)) || bias.Any(b => !float.IsFinite(b)))
        {
            return false;
        }

        fit = new LemmaFit(weights, bias, fine, group, dimension);
        position += (int)size + ChecksumSize;
        return true;
    }
}
