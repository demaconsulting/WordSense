using System.Globalization;
using System.Security.Cryptography;
using DemaConsulting.WordSense.Onnx;

#pragma warning disable S6966 // the tool writes small console outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     The outcome of the comparison of this CPU with the reference fingerprint of an encoder.
/// </summary>
internal enum CpuFingerprintStatus
{
    /// <summary>
    ///     The file has no fingerprint for the encoder.
    /// </summary>
    NoReference,

    /// <summary>
    ///     The vectors of this CPU have the reference fingerprint.
    /// </summary>
    Match,

    /// <summary>
    ///     The vectors of this CPU differ from the reference fingerprint.
    /// </summary>
    Differs,
}

/// <summary>
///     The CPU preflight of the commands that write weights and heads: it encodes a fixed list of sentences, hashes the
///     bits of the vectors and compares the hash with the fingerprint recorded for the encoder, and warns when the
///     machine does not reproduce it.
/// </summary>
/// <remarks>
///     <para>
///     The vectors of an int8 encoder depend on the float kernels of the CPU in the last bits (see <see cref="Canary"/>),
///     and trained weights depend on the vectors, so weights that are built on another CPU class are not byte-identical
///     to the committed ones. The preflight never stops a run; it says that the result is not reproducible.
///     </para>
///     <para>
///     The reference fingerprints are in <see cref="FileName"/> beside the sources of the builder: tab-separated lines of the
///     model identity of the weights, the SHA-256 of the encoder file and the fingerprint; a line that starts with
///     <c>#</c> is a comment. The fingerprint is recorded on the CPU class that builds the committed weights.
///     </para>
/// </remarks>
internal static class CpuPreflight
{
    /// <summary>
    ///     The name of the file of the reference fingerprints, in <c>tools/WordSense.Builder</c>.
    /// </summary>
    public const string FileName = "cpu-fingerprints.tsv";

    private static readonly string[][] Sentences =
    [
        ["The", "bank", "approved", "the", "loan", "after", "a", "short", "review", "."],
        ["She", "will", "record", "the", "record", "before", "the", "band", "plays", "."],
        ["They", "found", "the", "river", "bank", "covered", "with", "fresh", "snow", "."],
        ["We", "can", "can", "the", "peaches", "that", "he", "picked", "yesterday", "."],
        ["Nobody", "could", "find", "a", "use", "for", "the", "strange", "brass", "gadget", "."],
        ["The", "technician", "'s", "tool", "broke", "when", "it", "was", "used", "twice", "."],
    ];

    /// <summary>
    ///     Computes the fingerprint of an encoder on this machine: the SHA-256 of the bits of the sense vectors and of the
    ///     last-layer vectors of a fixed list of sentences.
    /// </summary>
    /// <param name="encoder">The encoder.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The fingerprint as lower-case hexadecimal text.</returns>
    public static async Task<string> FingerprintAsync(OnnxEncoder encoder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        var vectors = new List<double[]?>();
        foreach (var words in Sentences)
        {
            vectors.AddRange(await encoder.EncodeAllAsync(words, cancellationToken).ConfigureAwait(false));
            vectors.AddRange(await encoder.EncodeHeadAllAsync(words, cancellationToken).ConfigureAwait(false));
        }

        return Hash(vectors);
    }

    /// <summary>
    ///     Hashes the bits of vectors.
    /// </summary>
    /// <param name="vectors">The vectors; a null entry stands for a word without a vector.</param>
    /// <returns>The SHA-256 of the entries as lower-case hexadecimal text.</returns>
    public static string Hash(IEnumerable<double[]?> vectors)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bits = stackalloc byte[8];
        foreach (var vector in vectors)
        {
            hash.AppendData(vector is null ? [0] : [1]);
            foreach (var value in vector ?? [])
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(bits, BitConverter.DoubleToInt64Bits(value));
                hash.AppendData(bits);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    ///     Compares a fingerprint with the reference of an encoder.
    /// </summary>
    /// <param name="file">The text of the fingerprint file, or null when there is none.</param>
    /// <param name="weightsId">The model identity of the weights.</param>
    /// <param name="encoderSha256">The SHA-256 of the encoder file.</param>
    /// <param name="actual">The fingerprint of this machine.</param>
    /// <returns>The status.</returns>
    public static CpuFingerprintStatus Compare(string? file, string weightsId, string encoderSha256, string actual)
    {
        foreach (var line in (file ?? string.Empty).Split('\n').Select(l => l.TrimEnd('\r')))
        {
            var columns = line.Split('\t');
            if (line.StartsWith('#') || columns.Length != 3 || columns[0] != weightsId || !string.Equals(columns[1], encoderSha256, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return string.Equals(columns[2].Trim(), actual, StringComparison.OrdinalIgnoreCase) ? CpuFingerprintStatus.Match : CpuFingerprintStatus.Differs;
        }

        return CpuFingerprintStatus.NoReference;
    }

    /// <summary>
    ///     Runs the preflight of a model: computes the fingerprint and says whether this machine reproduces the reference.
    /// </summary>
    /// <param name="encoder">The opened encoder of the model.</param>
    /// <param name="spec">The description of the model.</param>
    /// <param name="repository">The repository root.</param>
    /// <param name="output">Receives the message.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when the message is written.</returns>
    public static async Task RunAsync(OnnxEncoder encoder, ModelSpec spec, string repository, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(output);
        var actual = await FingerprintAsync(encoder, cancellationToken).ConfigureAwait(false);
        var sha = ModelTrainer.EncoderShaOf(spec);
        var path = Path.Join(repository, "tools", "WordSense.Builder", FileName);
        var status = Compare(File.Exists(path) ? File.ReadAllText(path) : null, spec.WeightsId, sha, actual);
        switch (status)
        {
            case CpuFingerprintStatus.Match:
                output.WriteLine($"cpu preflight: this CPU reproduces the reference vectors of {spec.WeightsId} ({Canary.Machine()}).");
                break;
            case CpuFingerprintStatus.Differs:
                output.WriteLine($"cpu preflight: warning: the vectors of this CPU ({Canary.Machine()}) differ from the reference fingerprint of {spec.WeightsId}; weights and heads built here are not byte-reproducible. Build committed files on the reference CPU class (x86-64 with AVX2 and AVX-VNNI).");
                break;
            default:
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"cpu preflight: no reference fingerprint of {spec.WeightsId} in {FileName}; to record this machine as the reference add the line: {spec.WeightsId}\t{sha}\t{actual}"));
                break;
        }
    }
}
