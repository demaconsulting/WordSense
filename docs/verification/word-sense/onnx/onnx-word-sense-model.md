### OnnxWordSenseModel Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Onnx.Tests/OnnxWordSenseModelTests.cs` exercise the abstract class through the
MiniLM package (its constructor is `private protected`, so only the WordSense assemblies can derive from it). Files
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
- **Explicit directory is verified, not downloaded**: with a model directory, `EnsureModelAsync` checks every file's
  SHA-256 and makes no network request; a missing file throws `FileNotFoundException` and an altered file throws
  `InvalidDataException`.
- **Store is used without a directory**: without a model directory the files are obtained through the downloader into
  `<store>/<model-id>/`.
- **Lexicon-only words never touch model files** (`LexiconOnlyWords_NeverTouchesModelFiles`): `ClassifyAsync` on words
  with at most one sense, on a model whose directory does not exist, returns the lexicon answers without any ensure.
- **Untrained ambiguous words need no model**
  (`UntrainedAmbiguousWords_AnswerPartOfSpeechWithoutModelFiles`): `ClassifyAsync(["so", "will"])` on a model whose
  directory does not exist returns for each word the analysis of the fallback model at the part of speech level, with no
  group or sense id and the fallback probability as confidence, with no ensure and no first-sense guess; a word with
  several senses and weights (`record`) still ensures the files (`MissingDirectory_ThrowsFileNotFound`).
- **Lazy ensure**: `ClassifyAsync` on an ambiguous word without a prior `EnsureModelAsync` ensures the files by itself
  (default settings) and classifies; a missing directory throws `FileNotFoundException`.
- **Concurrent EnsureModelAsync calls share one download**: parallel calls produce one set of requests; parallel
  `ClassifyAsync` calls share one lazy ensure.
- **Failed EnsureModelAsync can be retried**: after all sources fail, a second call (or `ClassifyAsync`) starts a new
  attempt and succeeds when a source works.
- **A caller can stop waiting**: a cancelled token ends the wait of that caller with `OperationCanceledException`
  without cancelling the shared work; a later call succeeds.
- **Canceled token before any work**: `ClassifyAsync` with an already canceled token faults with
  `OperationCanceledException` for an empty list and for a list that needs no model, as the Basic model does.
- **Weights, session and fallback are shared**: repeated `ClassifyAsync` calls share one encoder, one weights table and
  one fallback model.
- **Disposal**: `Dispose` can be called twice and from many threads at once; `EnsureModelAsync` and `ClassifyAsync`
  after disposal throw `ObjectDisposedException`, even for words that never need the encoder.
- **Dispose releases the fallback model** (`Dispose_ReleasesTheFallbackModel`): after disposal the owned fallback model
  refuses calls; with a classification running, the release of the encoder and of the fallback model both wait for the
  last classification to finish.
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
