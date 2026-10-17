### OnnxWordSenseModel Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Onnx.Tests/OnnxWordSenseModelTests.cs` derive a small test model from the abstract
class inside the test assembly (the constructor is `private protected`, so only the WordSense assemblies and their
friend test projects can do this) with a hand-made `ModelSpec`. Files come from a temporary model directory or the
loopback `LocalServer`; the pinned digests are those of the test files. Where a session is needed the committed MiniLM
int8 model is used through `ModelAssets` (skipped when the Git LFS content is absent).

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
- **Lexicon-only words never touch model files**: `ClassifyAsync` on words with at most one sense, on a model whose
  directory does not exist, returns the lexicon answers without any ensure.
- **Lazy ensure**: `ClassifyAsync` on an ambiguous word without a prior `EnsureModelAsync` ensures the files by itself
  (default settings) and classifies; a missing directory throws `FileNotFoundException`.
- **Concurrent EnsureModelAsync calls share one download**: parallel calls produce one set of requests; parallel
  `ClassifyAsync` calls share one lazy ensure.
- **Failed EnsureModelAsync can be retried**: after all sources fail, a second call (or `ClassifyAsync`) starts a new
  attempt and succeeds when a source works.
- **A caller can stop waiting**: a cancelled token ends the wait of that caller with `OperationCanceledException`
  without cancelling the shared download.
- **Weights and session are shared**: repeated `ClassifyAsync` calls share one encoder and one weights table.
- **Disposal**: `Dispose` can be called twice and from many threads at once; `EnsureModelAsync` and `ClassifyAsync`
  after disposal throw `ObjectDisposedException`, even for words that never need the encoder.
- **Dispose during EnsureModelAsync**: a local server starts a response and stalls; disposing the model cancels the
  download, every pending `EnsureModelAsync` task ends with `OperationCanceledException` without hanging, later calls
  throw `ObjectDisposedException`, and no `.part` or model file remains in the store.
- **Argument validation**: a null spec and a maximum sequence length below 8 or above 512 are rejected.
