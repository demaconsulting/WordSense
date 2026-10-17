### MiniLMWordSenseModel Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.MiniLM.Tests/MiniLMWordSenseModelTests.cs` use the public type with the committed
`all-MiniLM-L6-v2-int8` files, the embedded weights and the embedded lexicon. The model is a thin subclass of
`OnnxWordSenseModel`; the shared behavior is verified in the Onnx unit documents, so these tests check the pinned identity
and one real classification path.

#### Test Environment

Standard xUnit v3 runner. `model/all-MiniLM-L6-v2-int8/` with `model_int8.onnx` and `vocab.txt` (Git LFS); the tests are
skipped when the LFS content is absent. No network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Pinned identity**: `ModelId` is `all-MiniLM-L6-v2-int8` and `HiddenSize` is 384 before any setup.
- **Model directory is verified**: a directory with `model_int8.onnx` and `vocab.txt` passes `EnsureModelAsync`; a missing
  or altered file is refused.
- **Embedded weights load with zero dropped lemmas**: the weights embedded in the assembly load for the model id and hidden
  size and match the embedded lexicon with no lemma dropped.
- **Uncased classification**: the committed model classifies the first `record` of "The record was broken last year, so
  they will record the new album." as a noun and the second as a verb.
- **Constructor validation**: a maximum sequence length outside 8 to 512 throws `ArgumentOutOfRangeException`.
- **Disposal**: disposing twice is harmless and `ClassifyAsync` after disposal throws `ObjectDisposedException`; the model
  contract is `IDisposable`, so `using` disposes it.
