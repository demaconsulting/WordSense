namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The shared ONNX Runtime implementation behind the encoder model packages of DemaConsulting.WordSense.
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense.Onnx</c>, which targets <c>net8.0</c>, <c>net9.0</c> and
///     <c>net10.0</c>; it depends only on <c>DemaConsulting.WordSense</c> and ONNX Runtime. You normally do not reference it
///     directly: <c>DemaConsulting.WordSense.MiniLM</c> and <c>DemaConsulting.WordSense.BertBaseCased</c> bring it in.
///     Its single public type is <see cref="OnnxWordSenseModel"/>, the base class of those models. It owns the model files
///     (found in a directory you supply, in the per-user store, on a mirror, or on GitHub, each verified against a
///     pinned SHA-256), the WordPiece tokenizer and the ONNX session. It is extended only by the packages of this
///     repository (<c>MiniLM</c> and <c>BertBaseCased</c>): its constructor is not accessible to other assemblies, so
///     it is not an extension point for third parties. Contributors who add a new ONNX model follow the section "Adding a
///     New ONNX Model" of
///     https://github.com/demaconsulting/WordSense/blob/main/docs/design/word-sense/onnx.md. See
///     https://github.com/demaconsulting/WordSense/tree/main/src/DemaConsulting.WordSense.Onnx.
///     </para>
///     <para>
///     Offline and air-gapped use: fetch the model files of a model package (listed in the model folder of the repository,
///     https://github.com/demaconsulting/WordSense/tree/main/model) on a machine with network access, copy them to the
///     target machine and pass their directory to the model's constructor. The directory is flat: the files sit
///     directly in it (no <c>{model-id}</c> subfolder). Calling the model's <c>EnsureModelAsync</c> (the
///     <see cref="IDownloadableWordSenseModel"/> capability that <see cref="OnnxWordSenseModel"/> implements) is optional but
///     recommended: it verifies every file against its pinned SHA-256 up front (no network access). Missing files are reported
///     as <see cref="ModelUnavailableException"/> and wrong digests as <see cref="ModelIntegrityException"/> (both
///     <see cref="WordSenseException"/>s that keep the original exception as inner exception). For a corporate mirror pass a
///     <see cref="DownloadOptions"/> to <see cref="OnnxWordSenseModel.EnsureModelAsync"/>, or set the environment variables
///     <c>WORDSENSE_MIRROR_URL</c>, <c>WORDSENSE_MIRROR_USER</c>, <c>WORDSENSE_MIRROR_PASSWORD</c> and
///     <c>WORDSENSE_MIRROR_BEARER_TOKEN</c>; files are requested at <c>{mirror}/{model-id}/{file}</c>. A blank variable
///     counts as unset; a user without a password (or the reverse), or a user, password or token without
///     <c>WORDSENSE_MIRROR_URL</c>, throws <see cref="DownloadConfigurationException"/>; the variables are never read when a model
///     directory was supplied (an invalid setting is then ignored without an exception) or when a
///     <see cref="DownloadOptions"/> is passed, and a <see cref="DownloadOptions.Timeout"/> that is zero, negative or longer
///     than about 49 days throws <see cref="DownloadConfigurationException"/> (<c>Timeout.InfiniteTimeSpan</c> means no
///     limit). A <c>WORDSENSE_MIRROR_URL</c> or <see cref="DownloadOptions.BaseUri"/> that is relative, not http or https,
///     or has a query, a fragment or user-info throws <see cref="DownloadConfigurationException"/> as well. No environment
///     variable sets the store location (the downloaded copies live in <c>{StoreRoot}/{model-id}/</c>, by default beneath
///     the per-user local application data folder: <c>%LOCALAPPDATA%\DemaConsulting\WordSense\models</c> on Windows and
///     <c>~/.local/share/DemaConsulting/WordSense/models</c> on Linux and macOS); only <see cref="DownloadOptions.StoreRoot"/> changes it, and that type
///     requires a mirror base URI. Network notes: downloads use the default .NET proxy behavior (system proxy; the
///     <c>HTTP_PROXY</c>, <c>HTTPS_PROXY</c> and <c>NO_PROXY</c> variables on Linux and macOS), with no proxy
///     credentials of its own; allow-list your mirror host and <c>media.githubusercontent.com</c> (and the hosts it
///     redirects to). Cancelling the token of <c>EnsureModelAsync</c> only cancels that caller's wait (an already
///     cancelled token gives a cancelled task, not a synchronous exception); the shared ensure keeps running and the next call joins it.
///     Disposal is safe while calls are running, and cancels a download in progress.
///     </para>
/// </remarks>
internal static class NamespaceDoc;
