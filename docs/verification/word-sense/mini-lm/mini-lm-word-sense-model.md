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
- **Model is downloadable** (`MiniLMWordSenseModel_IsDownloadable`): the model, held as `IWordSenseModel`, is also an
  `IDownloadableWordSenseModel`, so the optional `EnsureModelAsync` is available through the capability interface (the
  composition forwards it to the encoder model only, the Basic model needs none).
- **Model directory is verified**: a directory with `model_int8.onnx` and `vocab.txt` passes `EnsureModelAsync`
  (`MiniLMWordSenseModel_EnsureModelAsync_ModelDirectory_VerifiesPinnedFiles`); a missing or altered file is refused,
  verified for every model through this class in `OnnxWordSenseModelTests`, and a directory that does not exist throws
  `ModelUnavailableException` through the composition, carrying the original exception as inner exception
  (`MiniLMWordSenseModel_EnsureModelAsync_MissingDirectory_ThrowsModelUnavailable`).
- **Embedded weights resolve against the word image** (`EmbeddedWeights_MiniLM_MatchesWordImage`, in the Onnx test
  project): the weights embedded in the assembly load for the model id and hidden size, carry the inventory hash of the
  shipped word image and every sense of every lemma resolves.
- **Classification** (`Classify_AmbiguousWord_ReturnsDeterministicResults`,
  `Classify_TrainedLemma_AnswersAtASenseOrGroupLevel`): the
  committed model classifies the words of "They will record the record." with the verb analysis for the first `record`
  and the noun analysis for the second, each at a level that the result states, with a group id only where the level and
  the sense carry one, deterministically; a trained lemma is answered by the encoder at a sense, group or
  part-of-speech level.
- **Untrained ambiguous word is answered by Basic** (`Classify_UntrainedAmbiguousWord_AnswersFromBasicFallback`): a word
  with several senses that the encoder does not score gets the complete result of the Basic model (the same analyses,
  probabilities, selected analysis, level and confidence as `BasicWordSenseModel` gives for the same text; the level is
  the part of speech, or the sense when the selected analysis has exactly one sense), never a
  mixture, with no ensure of the model files.
- **Untrained polysemous word lists its senses by usage** (`MiniLMWordSenseModel_Classify_UntrainedPolysemousWord_ListsSensesRankedByUsagePrior`,
  `..._AnswersPartOfSpeechLevelWithoutSenseOrGroup`, `..._KeyCarriesTheLabelOfTheTopRankedSense`, in
  `MiniLMWordSenseModelTests.cs`, with no model files): the second `shelter` of "They will shelter the shelter." (a noun,
  so a word that the encoder has no weights for, with two or more senses) is answered at the part-of-speech level with no
  sense id and no group id, the senses of its selected analysis are those of `UsagePrior.RankOf` for its lemma and part
  of speech (two or more), and its key ends with the label of the first of them.
- **End to end** (`MiniLM_Classifier_EndToEnd_UntrainedWord_RanksItsSensesByUsage`, in `EndToEndTests.cs`, with the
  committed model files): the same three properties for the second `garden` of "They will garden the garden." (a lemma
  without trained weights) through the composite model of the public package; this is the system-level evidence.
- **Same-spelling verbs take their tense from Basic** (`MiniLM_Classifier_EndToEnd_SameSpellingVerb_TenseFollowsTheContext`,
  in `EndToEndTests.cs`, theory): for `I will read that tomorrow`, `I have read that already`, `Please put the books on
  the shelf` and `She had put the keys in her bag before she left` the composite selects a non-past analysis in the first
  and third sentence and a past one (the past tense or the participle) in the second and fourth, the selected analysis is
  the first analysis, and the probabilities of the word sum to one. The same test compares the composite with the
  encoder model on its own (`Encoder.ClassifyAsync`): where the encoder named a sense or a group for the verb the level,
  the confidence, the group id and the sense id are equal and the probability of the slot of the verb (the sum of its
  variants) is equal to within 1e-9, so only the split inside the slot moved; where the encoder scored senses but stayed
  at the part-of-speech level (`read` in some sentences) the answer stays at that level with the ranked senses of the
  encoder and the probability of the selected slot as the confidence (the slot probabilities are combined with those of
  Basic); where it scored none (`put`) the composite gives the complete result of Basic, analysis by analysis.
- **Composition references** (`Assembly_ReferencesCoreAndOnnx`): the assembly references the core and Onnx
  assemblies (the Basic model is in the core).
- **Part-of-speech answer is combined with Basic**
  (`MiniLMWordSenseModel_Classify_VerbUse_IsAnsweredWithTheCombinedSlotProbabilities`): for "Do not use these guys ."
  the encoder model on its own selects the noun analysis of `use`, as measured on the treebank, and the model selects
  the verb analysis, at the part-of-speech level, with a verb slot probability above the encoder's, the probability of
  the verb slot as the confidence and the probabilities summing to one (the scenario needs the model files).
- **Sense and group answers are the encoder's**
  (`MiniLMWordSenseModel_Classify_SenseOrGroupAnswer_IsTheEncodersAnswer`): for "They will record the record ." at least
  one word is named by the encoder at the sense or the group level, and for each such word the model's level,
  confidence, group id and sense id equal the encoder's (the scenario needs the model files).
- **Weight (no requirement)** (`MiniLMWordSenseModel_PrimaryWeight_IsTunedAndReachesTheComposition`,
  `MiniLMWordSenseModel_Classify_VerbUse_FollowsThePrimaryWeight`): the weight is 0.6 and reaches the composition
  (`PrimaryWeight`, internal), the internal constructor sets another one and rejects a weight above 1; for "Do not use
  these guys ." the model with the weight 1 (the encoder alone) selects the noun analysis of `use` and the model with the
  weight 0 (the part of speech of the Basic tagger) selects the verb analysis, at the part-of-speech level, with the
  probability of the verb slot as the confidence, the probabilities summing to one and the ranked senses of the encoder
  kept (the second scenario needs the model files; the extremes are used so that the test does not depend on a near tie
  of the tuned weight).
- **Constructor defaults and validation** (`MiniLMWordSenseModel_Constructor_Defaults_UseStoreAnd128`,
  `MiniLMWordSenseModel_Constructor_SequenceLengthOutOfRange_Throws`): the default maximum sequence length is 128, read
  from the encoder model (`MaxSequenceLength`, internal), and the limits 8 and 512 reach it unchanged; a maximum
  sequence length outside 8 to 512 throws `ArgumentOutOfRangeException`.
- **Disposal** (`Dispose_DisposesEncoderAndFallback`): disposing twice is harmless, disposes the encoder model and the
  Basic model, and `ClassifyAsync` after disposal throws `ObjectDisposedException`; the model contract is `IDisposable`,
  so `using` disposes it.
