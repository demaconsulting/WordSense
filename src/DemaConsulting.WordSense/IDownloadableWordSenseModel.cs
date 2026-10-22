namespace DemaConsulting.WordSense;

/// <summary>
///     A word-sense model that needs files (for example an encoder model and its ONNX file) and can make them available
///     up front: it can check, and when necessary download, those files before the first classification.
/// </summary>
/// <remarks>
///     <para>
///     This is an optional capability of <see cref="IWordSenseModel"/>. A model that needs no files, such as
///     <c>BasicWordSenseModel</c>, does not implement it. Code that holds only an <see cref="IWordSenseModel"/> asks
///     for the capability with a type check, and code that holds a concrete model type calls
///     <see cref="EnsureModelAsync"/> directly. The explicit call is optional: <see cref="IWordSenseModel.ClassifyAsync"/>
///     ensures the model lazily when a word needs it. Call it to control when the download happens (at startup, before
///     going offline, through a mirror) and to handle the failures up front, with the typed exceptions
///     <see cref="ModelUnavailableException"/>, <see cref="ModelIntegrityException"/> and
///     <see cref="DownloadConfigurationException"/> (all <see cref="WordSenseException"/>).
///     </para>
///     <para>
///     The contract of <see cref="EnsureModelAsync"/> is idempotent (calling it again after it succeeded is cheap and
///     harmless), thread-safe (concurrent calls share one ensure) and retryable (a failed call can be repeated). The
///     ensured state belongs to one model instance (a new instance verifies again). Disposal: after the model is
///     disposed, the call throws <see cref="ObjectDisposedException"/>; a download that is still running when the model
///     is disposed is cancelled. Downloaded files are never deleted by disposal and are reused by the next instance.
///     </para>
///     <para>
///     An implementation must honor the rules above and the rules of <see cref="IWordSenseModel"/>. A composite such as
///     <see cref="FallbackWordSenseModel"/> forwards the call to the members that have the capability and skips the
///     others. Third parties may implement the interface to add their own downloadable engine.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // Code that holds only an IWordSenseModel asks for the capability
///     static async Task PrepareAsync(IWordSenseModel model)
///     {
///         if (model is IDownloadableWordSenseModel downloadable)
///         {
///             await downloadable.EnsureModelAsync();
///         }
///
///         ClassifyResult[] results = await model.ClassifyAsync(["to", "record", "the", "record"]);
///         Console.WriteLine(results[1].Details?.PartOfSpeech.ToString() ?? "unknown");
///     }
///
///     // With a mirror, when the public download host is blocked
///     static Task PrepareThroughMirrorAsync(IDownloadableWordSenseModel model) =>
///         model.EnsureModelAsync(new DownloadOptions(new Uri("https://models.example.com/wordsense")));
///     </code>
/// </example>
public interface IDownloadableWordSenseModel : IWordSenseModel
{
    /// <summary>
    ///     Makes sure every file the model needs is present locally and verified, downloading what is missing.
    /// </summary>
    /// <param name="options">
    ///     The download options (a mirror to try before the public source, the store location and the timeout), or
    ///     <see langword="null"/> to use the mirror configured by the <c>WORDSENSE_MIRROR_URL</c> environment variable
    ///     (and its credential variables), if any. Ignored by a model that was given a model directory.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token that cancels the wait of this call: the returned task is cancelled (awaiting it throws
    ///     <see cref="OperationCanceledException"/>); the exception is not thrown from the call itself, also not for a
    ///     token that is already cancelled. The work that is shared with other callers (a download in progress) is not
    ///     cancelled by a token: it continues, also when no caller is waiting any more; only disposing the model stops it.
    /// </param>
    /// <returns>A task that completes when the model files are ready.</returns>
    /// <remarks>
    ///     <para>
    ///     Files that are already present and valid are never downloaded again. Concurrent calls share one ensure; the
    ///     <see cref="DownloadOptions"/> passed while another call's ensure is still running are ignored. The rule for
    ///     cancellation, the same for every caller: when the only waiting caller cancels, the shared ensure keeps
    ///     running (it is not cancelled), and the next call joins it if it is still running (its
    ///     <see cref="DownloadOptions"/> are ignored then), returns at once if it has succeeded in the meantime, and
    ///     starts a new ensure with its own <see cref="DownloadOptions"/> only if the earlier one failed. A failed call can be
    ///     retried. The ensure is optional: <see cref="IWordSenseModel.ClassifyAsync"/> ensures lazily with the defaults
    ///     (no explicit options; the <c>WORDSENSE_MIRROR_*</c> environment variables apply) and may throw the same
    ///     exceptions; once a call succeeded, the instance keeps the directory it verified, including the
    ///     <see cref="DownloadOptions.StoreRoot"/> of that call, and later lazy ensures reuse it.
    ///     </para>
    ///     <para>
    ///     Already-cancelled token: the call does not throw for it. It returns a cancelled task (awaiting it throws
    ///     <see cref="OperationCanceledException"/>); the only exception is that an encoder model whose ensure has
    ///     already completed returns that completed task as it is (and, when an ensure is still running or has to
    ///     start, the shared ensure is started or joined and keeps running although the task returned to this caller
    ///     is cancelled). A composite without a member that has the capability behaves the same way. A disposed model
    ///     throws <see cref="ObjectDisposedException"/> first, even for an already cancelled token.
    ///     </para>
    ///     <para>
    ///     When an encoder model was constructed with a model directory, this call is optional but is the way to verify
    ///     it up front (otherwise <see cref="IWordSenseModel.ClassifyAsync"/> verifies lazily and may throw the same
    ///     exceptions): it checks that every file exists in that directory (directly in it, not in a subfolder) and
    ///     matches its pinned SHA-256, and it never touches the network. Without a directory it verifies the per-user
    ///     store, or the store beneath <see cref="DownloadOptions.StoreRoot"/>, and downloads what is missing or invalid.
    ///     </para>
    ///     <para>
    ///     Failures, all of them <see cref="WordSenseException"/>s that carry the original exception as
    ///     <see cref="Exception.InnerException"/>: a model directory that does not exist or lacks a file, or a file that
    ///     no source could deliver verified (a download that exceeds the timeout is not an exception of its own: that
    ///     attempt is discarded and the next source is tried) is a <see cref="ModelUnavailableException"/>; a file of a
    ///     supplied model directory that does not match its pinned SHA-256, or an embedded resource that is damaged,
    ///     is a <see cref="ModelIntegrityException"/>; an invalid mirror address, credential, timeout (zero, negative
    ///     or longer than about 49 days; infinite is allowed), store location or environment setting is a
    ///     <see cref="DownloadConfigurationException"/> (not when a model directory was supplied, because the options
    ///     are then ignored, valid or not). An unwritable store reports the file system exception of the platform
    ///     (for example <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>) unchanged. Not changed
    ///     by the typed exceptions: a disposed model throws <see cref="ObjectDisposedException"/> from the call; a
    ///     cancelled token cancels the returned task (<see cref="OperationCanceledException"/> when awaited), and so
    ///     does a download that is still running when the model is disposed. A model may throw
    ///     <see cref="ObjectDisposedException"/> from the call and report everything else through the returned task, so
    ///     catch the exceptions with an <c>await</c> in a <c>try</c>.
    ///     </para>
    /// </remarks>
    /// <exception cref="ModelUnavailableException">Reported through the returned task when a file could not be obtained and verified from any source, or a supplied model directory does not exist or lacks a model file.</exception>
    /// <exception cref="ModelIntegrityException">Reported through the returned task when a file of a supplied model directory does not match its pinned SHA-256, or an embedded resource is damaged.</exception>
    /// <exception cref="DownloadConfigurationException">Reported through the returned task when the download options or the <c>WORDSENSE_MIRROR_*</c> environment variables are invalid.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the model was already disposed when the call started.</exception>
    /// <exception cref="OperationCanceledException">Reported through the returned task (not thrown from the call) when <paramref name="cancellationToken"/> is cancelled, or when the model is disposed while the call is downloading.</exception>
    Task EnsureModelAsync(DownloadOptions? options = null, CancellationToken cancellationToken = default);
}
