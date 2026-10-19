### BertBaseCasedWordSenseModel Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.BertBaseCased.Tests/BertBaseCasedWordSenseModelTests.cs` use the public type with
the committed `bert-base-cased-int8` files, the embedded weights and the embedded lexicon. The model is a thin subclass
of `OnnxWordSenseModel`; the shared behavior is verified in the Onnx unit documents, so these tests check the pinned identity
and one real classification path.

#### Test Environment

Standard xUnit v3 runner. `model/bert-base-cased-int8/` with `model_int8.onnx`, `vocab.txt`, `config.json` and
`tokenizer.json` (Git LFS); the tests are skipped when the LFS content is absent. No network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Pinned identity**: `ModelId` is `bert-base-cased-int8`, `HiddenSize` is 768 and the vocabulary is cased before any
  setup; the four files are pinned by SHA-256 digests.
- **Model directory is verified**: a directory with all four files passes `EnsureModelAsync`; a missing or altered file
  is refused (verified for every model in `OnnxWordSenseModelTests`).
- **Embedded weights resolve against the word image** (`EmbeddedWeights_BertBaseCased_MatchesWordImage`, in the Onnx test
  project): the weights embedded in the assembly load for the model id and hidden size, carry the inventory hash of the
  shipped word image and every sense of every lemma resolves.
- **Cased classification at a sense or group level**
  (`Classify_AmbiguousWord_ReturnsBestSense`, `Classify_TrainedLemma_AnswersAtASenseOrGroupLevel`): the committed model
  classifies the words of "They will record the record." with the verb analysis for the first `record` and the noun
  analysis for the second, each at the sense or group level with the group id set.
- **Disposal**: `EnsureModelAsync` and `ClassifyAsync` after disposal throw `ObjectDisposedException`.
- **Constructor validation**: a maximum sequence length outside 8 to 512 throws `ArgumentOutOfRangeException`.
