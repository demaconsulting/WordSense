namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The shared ONNX Runtime implementation behind the encoder model packages of DemaConsulting.WordSense.
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense.Onnx</c>, which targets <c>net8.0</c>, <c>net9.0</c> and
///     <c>net10.0</c>; it depends on <c>DemaConsulting.WordSense</c> and ONNX Runtime. You normally do not reference it
///     directly: <c>DemaConsulting.WordSense.MiniLM</c> and <c>DemaConsulting.WordSense.BertBaseCased</c> bring it in.
///     Its single public type is <see cref="OnnxWordSenseModel"/>, the base class of those models. It owns the model files
///     (found in a directory you supply, in the per-user store, on a mirror, or on GitHub, each verified against a
///     pinned SHA-256), the WordPiece tokenizer and the ONNX session. It is extended only by the packages of this
///     repository (<c>MiniLM</c> and <c>BertBaseCased</c>): its constructor is not accessible to other assemblies, so
///     it is not an extension point for third parties. Contributors who add a new ONNX model follow the note "Adding a
///     New ONNX Model" in the design documentation. See
///     https://github.com/demaconsulting/WordSense/tree/main/src/DemaConsulting.WordSense.Onnx.
///     </para>
///     <para>
///     Offline and air-gapped use: fetch the model files of a model package (listed in the model folder of the repository,
///     https://github.com/demaconsulting/WordSense/tree/main/model) on a machine with network access, copy them to the
///     target machine and pass their directory to the model's constructor. The directory is flat: the files sit
///     directly in it (no <c>{model-id}</c> subfolder), and the model's <c>EnsureModelAsync</c> must still be called, to
///     verify every file against its pinned SHA-256 (no network access). Missing files and wrong digests are reported
///     as <see cref="FileNotFoundException"/> and <see cref="InvalidDataException"/>. For a corporate mirror pass a
///     <see cref="MirrorOptions"/> to <see cref="OnnxWordSenseModel.EnsureModelAsync"/>, or set the environment variables
///     <c>WORDSENSE_MIRROR_URL</c>, <c>WORDSENSE_MIRROR_USER</c>, <c>WORDSENSE_MIRROR_PASSWORD</c> and
///     <c>WORDSENSE_MIRROR_BEARER_TOKEN</c>; files are requested at <c>{mirror}/{model-id}/{file}</c>. No environment
///     variable sets the store location (the downloaded copies live in <c>{StoreRoot}/{model-id}/</c>, by default beneath
///     the per-user local application data folder: <c>%LOCALAPPDATA%\DemaConsulting\WordSense\models</c> on Windows and
///     <c>~/.local/share/DemaConsulting/WordSense/models</c> on Linux and macOS); only <see cref="MirrorOptions.StoreRoot"/> changes it, and that type
///     requires a mirror base URI.
///     </para>
/// </remarks>
internal static class NamespaceDoc;
