### MiniLMWordSenseModel Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.MiniLM.Tests/MiniLMWordSenseModelTests.cs` use the public type with the committed
`all-MiniLM-L6-v2-int8` files, the embedded weights and the embedded lexicon. The model is a composition
(`FallbackWordSenseModel`) of a thin subclass of `OnnxWordSenseModel` and the Basic model; the shared behavior is
verified in the Onnx and Basic unit documents, so these tests check the pinned identity, the composition and one real
classification path.

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
- **Classification** (`Classify_AmbiguousWord_ReturnsDeterministicResults`,
  `Classify_TrainedLemma_AnswersAtASenseOrGroupLevel`): the
  committed model classifies the words of "They will record the record." with the verb analysis for the first `record`
  and the noun analysis for the second, each at a level that the result states, with a group id only where the level and
  the sense carry one, deterministically; once the encoder scores fine senses, a trained lemma is answered at a sense,
  group or part-of-speech level.
- **Untrained ambiguous word is answered by Basic** (`Classify_UntrainedAmbiguousWord_AnswersFromBasicFallback`): a word
  with several senses that the encoder does not score gets the complete result of the Basic model (the same analyses,
  probabilities, selected analysis, level and confidence as `BasicWordSenseModel` gives for the same text), never a
  mixture, with no ensure of the model files.
- **Composition references** (`Assembly_ReferencesCoreOnnxAndBasic`): the assembly references the core, Onnx and Basic
  assemblies.
- **Constructor validation**: a maximum sequence length outside 8 to 512 throws `ArgumentOutOfRangeException`.
- **Disposal** (`Dispose_DisposesEncoderAndFallback`): disposing twice is harmless, disposes the encoder model and the
  Basic model, and `ClassifyAsync` after disposal throws `ObjectDisposedException`; the model contract is `IDisposable`,
  so `using` disposes it.
