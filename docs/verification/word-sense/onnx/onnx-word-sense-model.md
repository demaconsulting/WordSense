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
- **Create before EnsureModelAsync fails**: `Create` throws `InvalidOperationException` until the files are ready.
- **Concurrent EnsureModelAsync calls share one download**: parallel calls produce one set of requests.
- **Failed EnsureModelAsync can be retried**: after all sources fail, a second call starts a new attempt and succeeds when
  a source works.
- **A caller can stop waiting**: a cancelled token ends the wait of that caller with `OperationCanceledException`
  without cancelling the shared download.
- **Weights and session are shared**: classifiers from two `Create` calls share one encoder and one weights table.
- **Disposal**: `Dispose` can be called twice; `EnsureModelAsync` and `Create` after disposal throw
  `ObjectDisposedException`.
- **Argument validation**: a null spec and a maximum sequence length below 8 or above 512 are rejected.
