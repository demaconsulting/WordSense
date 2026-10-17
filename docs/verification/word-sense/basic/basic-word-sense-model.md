### BasicWordSenseModel Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Basic.Tests/BasicWordSenseModelTests.cs` use the public type with the embedded
lexicon and the embedded statistics table. The model has no files and no state, so nothing is mocked.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **EnsureModelAsync is a no-op**: the returned task completes without touching the file system or the network, and a
  mirror passed in (even an unusual one) is ignored.
- **Create returns a classifier without setup**: `Create` works without calling `EnsureModelAsync` first and returns
  a classifier that answers a word list.
- **Created classifiers are independent and thread-safe**: classifiers from two `Create` calls give identical results,
  and parallel calls on one classifier give the same results as sequential calls.
- **Cancelled token is honored**: `EnsureModelAsync` with an already cancelled token returns a cancelled task.
- **Disposal**: `Dispose` is idempotent, thread-safe and releases nothing. After it, `EnsureModelAsync` and `Create`
  throw `ObjectDisposedException`, and so does every call of a classifier created earlier, whatever the words
  (including an empty list).
