using System.Security.Cryptography;

namespace DemaConsulting.WordSense.MiniLM;

/// <summary>
///     One file of the encoder model, with the SHA-256 digest every copy must match.
/// </summary>
/// <param name="Name">The file name, which is also its name beneath every source's base location.</param>
/// <param name="Sha256">The pinned lower-case hexadecimal SHA-256 digest.</param>
/// <param name="HuggingFace">
///     Whether the Hugging Face repository is a candidate source for this file. It is false for files whose
///     committed bytes differ from the upstream repository's.
/// </param>
internal sealed record ModelFile(string Name, string Sha256, bool HuggingFace);

/// <summary>
///     The pinned description of the encoder model the embedded classifier table was trained with.
/// </summary>
/// <remarks>
///     The digests here are the contract between the downloaded files, the embedded table and the
///     committed copies under <c>model/</c>; a copy that does not match is rejected wherever it came from.
/// </remarks>
internal static class WordSenseModelCatalog
{
    /// <summary>
    ///     The identifier of the model, used as the folder name of the model everywhere: beneath <c>model/</c>, beneath a
    ///     mirror base, beneath the store root, and in its weights table.
    /// </summary>
    public const string ModelId = "all-MiniLM-L6-v2-int8";

    /// <summary>
    ///     The name of the ONNX encoder file.
    /// </summary>
    public const string OnnxFileName = "model_int8.onnx";

    /// <summary>
    ///     The name of the vocabulary file.
    /// </summary>
    public const string VocabularyFileName = "vocab.txt";

    /// <summary>
    ///     The pinned SHA-256 of the ONNX encoder file.
    /// </summary>
    public const string OnnxSha256 = "afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1";

    /// <summary>
    ///     The pinned SHA-256 of the vocabulary file.
    /// </summary>
    public const string VocabularySha256 = "07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3";

    /// <summary>
    ///     Gets the files that make up the model. The ONNX file is not published upstream in this
    ///     exact form, so it is never requested from Hugging Face.
    /// </summary>
    public static IReadOnlyList<ModelFile> Files { get; } =
    [
        new ModelFile(OnnxFileName, OnnxSha256, false),
        new ModelFile(VocabularyFileName, VocabularySha256, true),
    ];

    /// <summary>
    ///     Gets the folder name used for this model beneath the store root.
    /// </summary>
    public static string DirectoryName => ModelId;

    /// <summary>
    ///     Computes the lower-case hexadecimal SHA-256 of a file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The digest.</returns>
    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
