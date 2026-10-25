### BertBaseCasedWordSenseModel Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.BertBaseCased.Tests/BertBaseCasedWordSenseModelTests.cs` use the public type with
the committed `bert-base-cased-int8` files, the embedded weights and the embedded lexicon. The model is a composition
(`FallbackWordSenseModel`) of a thin subclass of `OnnxWordSenseModel` and the Basic model; the shared behavior is
verified in the Onnx and Basic unit documents, so these tests check the pinned identity, the composition and one real
classification path.

#### Test Environment

Standard xUnit v3 runner. `model/bert-base-cased-int8/` with `model_int8.onnx`, `vocab.txt`, `config.json` and
`tokenizer.json` (Git LFS); the tests are skipped when the LFS content is absent. No network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Pinned identity**: `ModelId` is `bert-base-cased-int8`, `HiddenSize` is 768 and the vocabulary is cased before any
  setup; the four files are pinned by SHA-256 digests.
- **Model is downloadable** (`BertBaseCasedWordSenseModel_IsDownloadable`): the model, held as `IWordSenseModel`, is
  also an `IDownloadableWordSenseModel`, so the optional `EnsureModelAsync` is available through the capability
  interface (the composition forwards it to the encoder model only, the Basic model needs none).
- **Model directory is verified**: a directory with all four files passes `EnsureModelAsync`
  (`BertBaseCasedWordSenseModel_EnsureModelAsync_ModelDirectory_VerifiesPinnedFiles`); a missing or altered file is
  refused (verified for every model in `OnnxWordSenseModelTests`), and a directory that does not exist throws
  `ModelUnavailableException` through the composition, carrying the original exception as inner exception
  (`BertBaseCasedWordSenseModel_EnsureModelAsync_MissingDirectory_ThrowsModelUnavailable`).
- **Embedded weights resolve against the word image** (`EmbeddedWeights_BertBaseCased_MatchesWordImage`, in the Onnx test
  project): the weights embedded in the assembly load for the model id and hidden size, carry the inventory hash of the
  shipped word image and every sense of every lemma resolves.
- **Classification** (`Classify_AmbiguousWord_ReturnsBestSense`, `Classify_TrainedLemma_AnswersAtASenseOrGroupLevel`):
  the committed model
  classifies the words of "They will record the record." with the verb analysis for the first `record` and the noun
  analysis for the second, each at a level that the result states, with a group id only where the level and the sense
  carry one; a trained lemma is answered by the encoder at a sense, group or part-of-speech level.
- **Untrained ambiguous word is answered by Basic** (`Classify_UntrainedAmbiguousWord_AnswersFromBasicFallback`): a word
  with several senses that the encoder does not score gets the complete result of the Basic model (the same analyses,
  probabilities, selected analysis, level and confidence as `BasicWordSenseModel` gives for the same text; the level is
  the part of speech, or the sense when the selected analysis has exactly one sense), never a
  mixture, with no ensure of the model files.
- **Same-spelling verbs take their tense from Basic**
  (`BertBaseCased_Classifier_EndToEnd_SameSpellingVerb_TenseFollowsTheContext`, in `EndToEndTests.cs`, theory): for
  `I will read that tomorrow`, `I have read that already`, `Please put the books on the shelf` and `She had put the keys
  in her bag before she left` the composite selects a non-past analysis in the first
  and third sentence and a past one (the past tense or the participle) in the second and fourth, the selected analysis is
  the first analysis, and the probabilities of the word sum to one. The same test compares the composite with the
  encoder model on its own (`Encoder.ClassifyAsync`): where the encoder scored senses for the verb (`read`) the level,
  the confidence, the group id and the sense id are equal and the probability of the slot of the verb (the sum of its
  variants) is equal to within 1e-9, so only the split inside the slot moved; where it did not (`put`) the composite
  gives the complete result of Basic, analysis by analysis. The re-split itself is verified in the
  `FallbackWordSenseModel` scenarios of the
  `WordSenseContracts` unit document.
- **Composition references** (`Assembly_ReferencesCoreAndOnnx`): the assembly references the core and Onnx
  assemblies (the Basic model is in the core).
- **Disposal** (`Dispose_DisposesEncoderAndFallback`): disposing disposes the encoder model and the Basic model, and
  `EnsureModelAsync` and `ClassifyAsync` after disposal throw `ObjectDisposedException`.
- **Constructor defaults and validation** (`BertBaseCasedWordSenseModel_Constructor_Defaults_UseStoreAnd128`,
  `BertBaseCasedWordSenseModel_Constructor_SequenceLengthOutOfRange_Throws`): the default maximum sequence length is
  128, read from the encoder model (`MaxSequenceLength`, internal), and the limits 8 and 512 reach it unchanged; a
  maximum sequence length outside 8 to 512 throws `ArgumentOutOfRangeException`.
