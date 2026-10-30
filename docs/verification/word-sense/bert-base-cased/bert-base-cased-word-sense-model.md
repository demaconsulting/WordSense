### BertBaseCasedWordSenseModel Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.BertBaseCased.Tests/BertBaseCasedWordSenseModelTests.cs` use the public type with
the committed `bert-base-cased-int8` files, the embedded weights and head and the embedded lexicon. The model is a
composition
(`FallbackWordSenseModel`) of a thin subclass of `OnnxWordSenseModel` and the Basic model; the shared behavior is
verified in the Onnx and Basic unit documents, so these tests check the pinned identity, the composition, the
reweighing of the head against Basic and one real classification path. A scenario that classifies a word of several
parts of speech needs the model files, because the head reads the encoder.

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
- **Embedded head** (`BertBaseCasedWordSenseModel_EmbeddedHead_MatchesEncoderAndClasses`, in `EmbeddedHeadTests.cs` of
  the BertBaseCased test project; no model file): the encoder model loads the head embedded in the assembly, which
  carries the model id `bert-base-cased-int8`, hidden size 768, the feature layout 1, the variant `word+-1`, the classes
  of `PartOfSpeech` in order and the SHA-256 of the pinned ONNX file; its posterior for a zero vector sums to one and its
  restriction to the noun class gives that class probability one.
- **Embedded head is bound** (`BertBaseCasedWordSenseModel_EmbeddedHead_AgainstAnotherEncoderFile_IsRejected`): the head
  read against the digest of another file, or against another model id, throws `InvalidDataException`.
- **Classification** (`Classify_AmbiguousWord_ReturnsBestSense`, `Classify_TrainedLemma_AnswersAtASenseOrGroupLevel`):
  the committed model
  classifies the words of "They will record the record." with the verb analysis for the first `record` and the noun
  analysis for the second, each at a level that the result states, with a group id only where the level and the sense
  carry one; a trained lemma is answered by the encoder at a sense, group or part-of-speech level.
- **Untrained ambiguous word is answered at the part-of-speech level**
  (`Classify_UntrainedAmbiguousWord_AnswersAtThePartOfSpeechLevel`, with the committed model files): in "They will record
  the record ." a word with several senses that the encoder does not score gets, for each `record`, the part of speech
  that the context decides (a verb after `will`, a noun after `the`), at the part-of-speech level, with no sense id and
  no
  group id, probabilities that sum to one, more than one analysis, and the senses of the selected analysis ranked by
  usage (two or more). The word needs the encoder for its head, so the scenario needs the model files.
- **Untrained polysemous word lists its senses by usage** (`BertBaseCasedWordSenseModel_Classify_UntrainedPolysemousWord_ListsSensesRankedByUsagePrior`,
  `..._AnswersPartOfSpeechLevelWithoutSenseOrGroup`, `..._KeyCarriesTheLabelOfTheTopRankedSense`, in
  `BertBaseCasedWordSenseModelTests.cs`, with the model files): the second `shelter` of "They will shelter the shelter."
  (a noun, so a word that the encoder has no weights for, with two or more senses) is answered at the part-of-speech
  level
  with no sense id and no group id, the senses of its selected analysis are those of `UsagePrior.RankOf` for its lemma and
  part of speech (two or more), and its key ends with the label of the first of them.
- **End to end** (`BertBaseCased_Classifier_EndToEnd_UntrainedWord_RanksItsSensesByUsage`, in `EndToEndTests.cs`, with the
  committed model files): the same three properties for the second `garden` of "They will garden the garden." (a lemma
  without trained weights) through the composite model of the public package; this is the system-level evidence.
- **Same-spelling verbs take their tense from Basic**
  (`BertBaseCased_Classifier_EndToEnd_SameSpellingVerb_TenseFollowsTheContext`, in `EndToEndTests.cs`, theory): for
  `I will read that tomorrow`, `I have read that already`, `Please put the books on the shelf` and `She had put the keys
  in her bag before she left` the composite selects a non-past analysis in the first
  and third sentence and a past one (the past tense or the participle) in the second and fourth, the selected analysis is
  the first analysis, and the probabilities of the word sum to one. The same test compares the composite with the
  encoder model on its own (`Encoder.ClassifyAsync`): where the encoder scored senses for the verb (`read`) and the level
  is kept, the group id and the sense id are equal and the probability of the answer within its slot (the confidence
  divided by the probability of the slot) is equal to within 1e-9, so only the probability of the slot and the split
  inside it moved; where it did not (`put`) the variants of the slot of the verb have the proportions of Basic. The
  re-split itself is verified in the `FallbackWordSenseModel` scenarios of the
  `WordSenseContracts` unit document.
- **Composition references** (`Assembly_ReferencesCoreAndOnnx`): the assembly references the core and Onnx
  assemblies (the Basic model is in the core).
- **Part-of-speech probabilities are reweighed** (`BertBaseCasedWordSenseModel_PartOfSpeechAnswer_ReweighsTheHeadAgainstBasic`):
  for "Do not use these guys ." the model answers `use` with the verb analysis, the probabilities of the analyses sum to
  one, and the probability of every part-of-speech slot equals (to within 1e-9) the normalized
  `p_head^w × p_basic^(1−w)` of the slot probabilities of the encoder model on its own (`Encoder.ClassifyAsync`) and
  of the
  Basic model (`Fallback.ClassifyAsync`) with the weight `w` of the model (the scenario needs the model files).
- **Weight** (`BertBaseCasedWordSenseModel_PrimaryWeight_DefaultIsTunedAndAnExplicitWeightReachesTheComposition`):
  the weight is 0.8 by default (`PartOfSpeechHeadWeight`, internal) and reaches the composition (`PrimaryWeight`), the
  internal constructor sets another one (0.25) and rejects a weight below 0; it needs no model file.
- **Lexicon-only text needs no ensure** (`BertBaseCasedWordSenseModel_ClassifyAsync_LexiconOnly_NeedsNoEnsure`): a text
  of unknown words and of known words of one part of speech with no scored senses is answered on a model that was never
  ensured, without any model
  file; a text with a word of several parts of speech or a word with scored senses ensures the files lazily (verified
  for the shared base class in
  `OnnxWordSenseModel_ClassifyAsync_WordOfSeveralPartsOfSpeech_NeedsTheEncoder`).
- **Disposal** (`Dispose_DisposesEncoderAndFallback`): disposing disposes the encoder model and the Basic model, and
  `EnsureModelAsync` and `ClassifyAsync` after disposal throw `ObjectDisposedException`.
- **Constructor defaults and validation** (`BertBaseCasedWordSenseModel_Constructor_Defaults_UseStoreAnd128`,
  `BertBaseCasedWordSenseModel_Constructor_SequenceLengthOutOfRange_Throws`): the default maximum sequence length is
  128, read from the encoder model (`MaxSequenceLength`, internal), and the limits 8 and 512 reach it unchanged; a
  maximum sequence length outside 8 to 512 throws `ArgumentOutOfRangeException`.
