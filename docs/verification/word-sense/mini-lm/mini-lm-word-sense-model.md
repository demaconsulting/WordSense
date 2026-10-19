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

- **Pinned identity**: `ModelId` is `all-MiniLM-L6-v2-int8`, `HiddenSize` is 384 and the vocabulary is uncased before any
  setup; the two files are pinned by SHA-256 digests.
- **Model directory is verified**: a directory with `model_int8.onnx` and `vocab.txt` passes `EnsureModelAsync`; a missing
  or altered file is refused (verified for every model through this class in `OnnxWordSenseModelTests`).
- **Embedded weights resolve against the word image** (`EmbeddedWeights_MiniLM_MatchesWordImage`, in the Onnx test
  project): the weights embedded in the assembly load for the model id and hidden size, carry the inventory hash of the
  shipped word image and every sense of every lemma resolves.
- **Uncased classification at a sense or group level**
  (`Classify_AmbiguousWord_ReturnsDeterministicResults`, `Classify_TrainedLemma_AnswersAtASenseOrGroupLevel`): the
  committed model classifies the words of "They will record the record." with the verb analysis for the first `record`
  and the noun analysis for the second, each at the sense or group level with the group id set, deterministically.
- **Constructor validation**: a maximum sequence length outside 8 to 512 throws `ArgumentOutOfRangeException`.
- **Disposal**: disposing twice is harmless and `ClassifyAsync` after disposal throws `ObjectDisposedException`; the model
  contract is `IDisposable`, so `using` disposes it.
