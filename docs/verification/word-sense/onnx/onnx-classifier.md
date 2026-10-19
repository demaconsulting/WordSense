### OnnxClassifier Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Onnx.Tests/OnnxClassifierTests.cs` build the classifier over the real MiniLM int8
encoder (through `ModelAssets`, skipped when the Git LFS content is absent) with hand-made weights matched against the
real embedded lexicon, so the expected sense, group and confidence follow from the arithmetic; scenarios whose words
need no context do not depend on the model at all. Substitute weights with chosen thresholds are used so that a specific
level is reached, and a test double stands in for the fallback model where the scenario needs to know that the
fallback was asked. Real-model accuracy is verified in the MiniLM and BertBaseCased subsystem documents.

#### Test Environment

Standard xUnit v3 runner. No network access. The real-model scenarios need `model/all-MiniLM-L6-v2-int8/` (Git LFS) and
are skipped when it is absent; the others need no model files.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Unknown word** (`UnknownWord_ReturnsNoDetails`): a word with no lexicon entry gives no details, level `None`,
  no ids and confidence 0, and needs no encoder result.
- **Capitalized word** (`CapitalizedWord_ResolvesLikeLowerCase`): `The` and `the` give the same answer.
- **Single-sense word** (`SingleSenseWord_ReturnsEntryWithFullConfidence`, `SingleSenseWord_DoesNotEncode`): a word
  whose lemma and part of speech have one sense, a determiner such as `the` among them, is answered at the `Sense` level
  with that sense, its group and confidence 1, and the encoder is not asked for it.
- **Encoder only when needed** (`NoAmbiguousWords_DoesNotEncode`, `AmbiguousWords_UseTheEncoder`,
  `OnlyAmbiguousWordsAreEncoded`): the encoder runs once for a text in which at least one word has several candidate
  senses and usable weights, only for those words, and never for a text in which no word does.
- **Candidates of every analysis** (`WordWithSeveralAnalyses_RanksTheSensesOfEveryAnalysis`,
  `PrepositionUseOfLike_IsReachable`, `InflectedForm_RanksTheSensesOfItsLemma`): a word with several analyses ranks the
  fine senses of all of them, so the preposition use of `like` can be the answer beside its verb and noun senses; an
  inflected form ranks the senses of its lemma; no sense of another lemma competes.
- **Confidence is monotonic in the margin** (`LargerMargin_GivesHigherConfidence`,
  `ConfidenceIsWithinZeroAndOne`, `MarginAtThreshold_GivesConfidenceOneHalf`): for a growing margin of the answered
  level the confidence never decreases, is clamped to 0 and 1, and equals 0.5 where the margin equals the lemma's
  threshold for that level.
- **Best candidate is kept** (`LowMargin_ReturnsBestGroupWithLowConfidence`): a low margin still returns the best group
  with its low confidence, not no answer.
- **Fine threshold met** (`MarginAboveFineThreshold_AnswersSense`, `MarginAtFineThreshold_AnswersSense`): a fine margin
  above, and exactly at, the lemma's fine threshold gives the best fine sense at the `Sense` level with the sense id,
  the group id and a confidence of at least one half.
- **Group answered when only the group is sure** (`FineBelowAndGroupAboveThreshold_AnswersGroup`): a fine margin below
  its threshold with a group margin that reaches the group threshold gives the best group at the `Group` level with the
  group id only and a confidence of at least one half.
- **Low-confidence group** (`BothBelowThreshold_AnswersBestGroupBelowOneHalf`): when neither threshold is reached the
  answer is the best group at the `Group` level with a confidence below one half.
- **Untrained lemma falls back to the part of speech** (`LemmaWithoutWeights_AnswersPartOfSpeechFromBasic`,
  `LemmaWithoutWeights_NeverAnswersAFineSense`, `SingleAnalysisWithManySensesAndNoWeights_AnswersPartOfSpeech`): a word
  with several senses whose lemma has no weights is answered with the analysis that the fallback model chooses, at the
  `PartOfSpeech` level, with no group id and no sense id and with the fallback probability as confidence; the same holds
  for a word with a single analysis and many senses, and the encoder is not asked for it.
- **Unusable vector falls back** (`DimensionMismatch_FallsBackToPartOfSpeech`): a vector whose dimension differs from the
  weights, or a target without word pieces, gives the fallback answer at the `PartOfSpeech` level, not no details.
- **Form of several lemmas** (`FormOfTwoLemmas_ChoosesTheLemmaFromContext`,
  `ReadPastVersusPresent_ChoosesTheTenseFeatures`, `FormChooserBelowThreshold_AnswersPartOfSpeech`,
  `EveryMultiLemmaForm_HasAChooser`): a form such as `left` is answered
  with the senses of the lemma that its chooser picks from the sentence; `read` gets its tense features from the
  chooser; a chooser margin below its threshold gives the `PartOfSpeech` level; every form of the lexicon whose analyses
  belong to several lemmas has a chooser in the shipped weights.
- **Words are returned as written**: the result array is parallel to the input, holds each word as given, and is
  case-insensitive in lookup (including the curly apostrophe).
- **Empty list and argument validation** (`EmptyList_ReturnsEmpty`, `NullWords_Throws`, `NullElement_Throws`,
  `Cancelled_Throws`): an empty list gives an empty array; a null word list and a null word throw
  `ArgumentException` or `ArgumentNullException`; a cancelled token throws `OperationCanceledException`.
