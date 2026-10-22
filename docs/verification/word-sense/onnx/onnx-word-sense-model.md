### OnnxWordSenseModel Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Onnx.Tests/OnnxWordSenseModelTests.cs` exercise the abstract class through the
internal encoder model of the MiniLM package (the constructor of the class is `private protected`, so only the WordSense
assemblies can derive from it). Files
come from a temporary model directory or the loopback `LocalServer`; the pinned digests are those of the test files.
Where a session is needed the committed MiniLM int8 model is used through `ModelAssets` (skipped when the Git LFS
content is absent). `InFlightTrackerTests.cs` tests the deferred-release counter with fake release actions and needs
no model files.

#### Test Environment

Standard xUnit v3 runner. A temporary directory per test; the loopback HTTP listener for download scenarios; the
MiniLM model files for session scenarios. No public network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Identity is known before setup**: `ModelId` and `HiddenSize` return the values of the spec before `EnsureModelAsync`
  is called.
- **Model is downloadable** (`OnnxWordSenseModel_IsDownloadable`): the model, held as `IWordSenseModel`, is also an
  `IDownloadableWordSenseModel`, so its `EnsureModelAsync` is reached through the capability interface.
- **Explicit directory is verified, not downloaded**
  (`OnnxWordSenseModel_EnsureModelAsync_MissingFile_ThrowsModelUnavailable`,
  `OnnxWordSenseModel_EnsureModelAsync_AlteredFile_ThrowsModelIntegrity`): with a model directory,
  `EnsureModelAsync` checks every file's SHA-256 and makes no network request; a missing file throws
  `ModelUnavailableException` with the original `FileNotFoundException` as inner exception, and an altered file throws
  `ModelIntegrityException` with the original `InvalidDataException` as inner exception.
- **Store is used without a directory**: without a model directory the files are obtained through the downloader into
  `<store>/<model-id>/`.
- **Lexicon-only words never touch model files** (`LexiconOnlyWords_NeverTouchesModelFiles`): `ClassifyAsync` on words
  with at most one sense, on a model whose directory does not exist, returns the lexicon answers without any ensure.
- **Untrained ambiguous words need no model**
  (`UntrainedAmbiguousWords_AnswerPartOfSpeechWithoutModelFiles`): `ClassifyAsync(["They", "will", "record", "the",
  "record", "."])` on a model whose directory does not exist returns for each `record` (a lemma without weights, with a
  noun and a verb analysis) its lexicon analyses (every lemma and part of speech equally likely, the first analysis
  selected) at the part of speech level, with no scored senses, no group or sense id and the probability of the slot of
  the selected analysis as
  confidence, with no ensure and no first-sense guess; the same holds for every ambiguous word whose lemma has no
  trained weights, while a word with several senses and weights (`back`) ensures the files
  (`OnnxWordSenseModel_ClassifyAsync_MissingDirectory_ThrowsModelUnavailable`).
- **Lazy ensure** (scoring increment): `ClassifyAsync` on an ambiguous word without a prior `EnsureModelAsync` ensures
  the files by itself (default settings) and classifies; a missing directory throws `ModelUnavailableException`.
- **Concurrent EnsureModelAsync calls share one download**: parallel calls produce one set of requests; parallel
  `ClassifyAsync` calls share one lazy ensure (scoring increment).
- **Failed EnsureModelAsync can be retried**
  (`OnnxWordSenseModel_EnsureModelAsync_AllSourcesFail_ThrowsModelUnavailable`): after all sources fail, the call
  throws `ModelUnavailableException` with the original `HttpRequestException` as inner exception, and a second call (or
  `ClassifyAsync`) starts a new attempt and succeeds when a source works.
- **Downloaded bytes with a wrong hash are a failed source**
  (`OnnxWordSenseModel_EnsureModelAsync_DownloadedHashMismatch_ThrowsModelUnavailable`): a source that serves bytes
  with a wrong SHA-256 has its file discarded like any other failed source, and when no source delivers a verified file
  the failure is `ModelUnavailableException`, not `ModelIntegrityException`.
- **Invalid download settings** (`OnnxWordSenseModel_EnsureModelAsync_InvalidOptions_ThrowsDownloadConfiguration`,
  theory over a relative address, another scheme, a query, a token with credentials, credentials over HTTP, a zero and a
  negative timeout, an empty and a whitespace-only store root and a store root with an invalid character;
  `OnnxWordSenseModel_EnsureModelAsync_InvalidEnvironment_ThrowsDownloadConfiguration`;
  `OnnxWordSenseModel_ClassifyAsync_InvalidMirrorEnvironment_ThrowsDownloadConfiguration`): invalid options, or an
  invalid `WORDSENSE_MIRROR_*` setting (a user without a password), throw `DownloadConfigurationException`, from the
  explicit ensure and from the lazy ensure of `ClassifyAsync`, which is not an `ArgumentException` and keeps the
  original `ArgumentException` (or `ArgumentOutOfRangeException` for a timeout) as inner exception; a failed call can be
  retried with valid settings.
- **Model directory ignores the options** (`OnnxWordSenseModel_EnsureModelAsync_ModelDirectory_IgnoresInvalidOptions`):
  with a model directory the download options are not read, so invalid options throw nothing.
- **Damaged model is an integrity failure**
  (`OnnxWordSenseModel_ClassifyAsync_WeightsOfAnotherImage_ThrowsModelIntegrity`,
  `OnnxWordSenseModel_ClassifyAsync_UnreadableOnnxFile_ThrowsModelIntegrity`,
  `OnnxWordSenseModel_ClassifyAsync_VocabularyWithoutRequiredToken_ThrowsModelIntegrity`): weights that do not belong
  to the word image (a sense that does not exist) throw `ModelIntegrityException` with the original
  `InvalidDataException` as inner
  exception, before any model file is needed; an ONNX file that passes its pinned digest but cannot be opened as a model
  throws `ModelIntegrityException` carrying the original exception of the ONNX runtime, and the failed open leaves the
  encoder released; a vocabulary that passes its pinned digest but lacks `[UNK]`, `[CLS]` or `[SEP]` throws
  `ModelIntegrityException` with the tokenizer's `ArgumentException` as inner exception. Cancellation, disposal and
  argument exceptions are not changed by the mapping.
- **A caller can stop waiting**: a cancelled token ends the wait of that caller with `OperationCanceledException`
  without cancelling the shared work; a later call succeeds.
- **Canceled token before any work**: `ClassifyAsync` with an already canceled token faults with
  `OperationCanceledException` for an empty list and for a list that needs no model, as the Basic model does.
- **Weights and session are shared** (scoring increment): repeated `ClassifyAsync` calls share one encoder and one
  weights table.
- **Disposal**: `Dispose` can be called twice and from many threads at once; `EnsureModelAsync` and `ClassifyAsync`
  after disposal throw `ObjectDisposedException`, even for words that never need the encoder.
- **Dispose during classification**: with a classification registered (as `ClassifyAsync` does around its inference),
  `Dispose` marks the model disposed at once (new calls throw `ObjectDisposedException`) but the encoder stays usable;
  the running classification completes with a correct answer, and releasing the last registration releases the
  encoder. With no classification running, `Dispose` releases the encoder immediately. The counter itself
  (`InFlightTracker`) is tested with fake release actions: close when idle, close with running operations (release
  after the last exit), enter after close, close not waiting for a gated operation, and concurrent enter/exit/close
  releasing exactly once.
- **Dispose during EnsureModelAsync**: a local server starts a response and stalls; disposing the model cancels the
  download, every pending `EnsureModelAsync` task ends with `OperationCanceledException` without hanging, later calls
  throw `ObjectDisposedException`, and no `.part` or model file remains in the store. Cancellation applies to the
  download path; with an explicit model directory the verification is synchronous.
- **Argument validation**: a maximum sequence length below 8 or above 512 is rejected. (The constructor that takes
  the spec is not reachable from outside the WordSense assemblies, so a null spec is not tested.)
