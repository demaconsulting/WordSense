### OnnxClassifier Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Onnx.Tests/OnnxClassifierTests.cs` call the classifier with word lists and the
real embedded lexicon, so the expected analyses, senses and levels follow from the lexicon. The lexicon-only scenarios
need no model, no encoder and no other model package. The scoring scenarios build the classifier over a fake encoder
(a delegate that returns designed vectors and counts its calls) and hand-made weights over the real senses of the
shipped catalog, so the expected sense, group and confidence follow from the arithmetic; the thresholds of the weights
are chosen so that a specific level is reached. Real-model behavior is verified in the MiniLM and BertBaseCased
subsystem documents.

#### Test Environment

Standard xUnit v3 runner. No network access. The real-model scenarios need `model/all-MiniLM-L6-v2-int8/` (Git LFS) and
are skipped when it is absent; the others need no model files.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Unknown word** (`UnknownWord_IsAnsweredAsUnknown`): a word with no lexicon entry is not known, has no analyses, no
  details, level `None`, no ids and confidence 0, and needs no encoder result.
- **Capitalized word** (`CapitalizedWord_ResolvesLikeLowerCase`): `The` and `the` give the same answer.
- **Single analysis and single sense** (`SingleAnalysisSingleSense_AnswersTheSense`, `SingleSenseWord_DoesNotEncode`): a
  word with one analysis whose lemma and part of speech have one sense, a determiner such as `the` among them, is
  answered at the `Sense` level with that analysis (probability 1), that sense (probability 1), no group id (a group
  never holds all the senses of its lemma and part of speech) and confidence 1, and the encoder is not asked for it.
- **Single analysis, several senses** (`SingleAnalysisSeveralSenses_AnswersPartOfSpeech`): a word with one analysis
  and several senses and no weights is answered at the `PartOfSpeech` level with that analysis, no scored senses, no ids
  and confidence 1.
- **Several analyses, no weights** (`LemmaWithoutWeights_AnswersAnalysesWithoutScoredSenses`,
  `LemmaWithoutWeights_NeverAnswersAFineSense`): a word with several analyses whose lemma has no weights lists every
  analysis grouped by lemma and part of speech in lexicon order, every lemma and part of speech with the probability
  1/s (s of them) shared equally by its feature variants, selects the first analysis, lists no scored senses, answers
  at the `PartOfSpeech` level with the probability of the slot of the selected analysis (1/s) as confidence and no group
  or sense id, and never names a fine sense.
- **Variants of the only slot** (`VariantsOfTheOnlySlot_AreConfidentAtThePartOfSpeech`): `deer`, a singular and a plural
  of one noun, has the probability 1/2 for each variant and the confidence 1, not 1/2.
- **First analysis with one sense** (`SeveralAnalysesFirstWithOneSense_StaysAtPartOfSpeech`): even when the first
  analysis has exactly one sense, nothing was scored, so no sense is listed or named and the answer stays at the
  `PartOfSpeech` level with the confidence of its slot.
- **Later analysis with one sense** (`SeveralAnalysesLaterWithOneSense_StaysAtPartOfSpeech`): likewise when only a later
  analysis has exactly one sense: no analysis lists scored senses and the answer stays at the `PartOfSpeech` level.
- **Encoder only when needed** (`NoAmbiguousWords_DoesNotEncode`, `AmbiguousWords_UseTheEncoder`,
  `OnlyAmbiguousWordsAreEncoded`; scoring increment): the encoder runs once for a text in which at least one word has
  several senses and usable weights, only for those words, and never for a text in which no word does.
- **Senses of every analysis** (`WordWithSeveralAnalyses_RanksTheSensesOfEveryAnalysis`,
  `PrepositionUseOfLike_IsReachable`, `InflectedForm_RanksTheSensesOfItsLemma`; scoring increment): a word with
  several analyses ranks the fine senses of all of them, so the preposition use of `like` can be the answer beside its
  verb and noun senses; an inflected form ranks the senses of its lemma; no sense of another lemma competes; the
  probability of a lemma and part of speech is the mass of its senses.
- **Slots** (`FeaturesOfOneLemmaAndPartOfSpeech_ShareTheSlotProbability`,
  `SlotWithSeveralVariants_IsSelectedByItsTotalProbability`): the two verb analyses of `read` split the probability of
  the verb equally, list the same senses, and the confidence of a sense answer is the probability of the sense (the
  probability of the verb times its probability within the verb), not half of it; when the noun is more probable than
  each verb variant but less than the verb as a whole, the verb is listed first and selected, and the answer is the
  part of speech with the probability of the verb as a whole.
- **Confidence is monotonic in the margin** (`LargerMargin_GivesHigherConfidence`, `ConfidenceIsWithinZeroAndOne`;
  scoring increment): for a growing margin of the best fine score the confidence, the probability of the answer at its
  level, never decreases and stays between 0 and 1.
- **Best senses are kept** (`LowMargin_KeepsTheBestSenseAndGroupVisibleInTheRankedSenses`; scoring increment): a low
  margin still lists the ranked senses, the best sense and the best group of the lemma first among them, whatever
  coarser level is answered, not no answer.
- **Fine threshold met** (`MarginAboveFineThreshold_AnswersSense`, `MarginAtFineThreshold_AnswersSense`; scoring
  increment): a fine margin above, and exactly at, the lemma's fine threshold gives the best fine sense at the `Sense`
  level with the sense id, the group id when the sense is in a group, and the sense lying in the selected analysis.
- **Group answered when only the group is sure** (`FineBelowAndGroupAboveThreshold_AnswersGroup`; scoring increment): for
  a lemma that has groups, a fine margin below its threshold with a group margin that reaches the group threshold gives
  the best group at the `Group` level with the group id only; the group's member senses are listed in the selected
  analysis.
- **Neither threshold met** (`BothBelowThreshold_AnswersPartOfSpeech`; scoring increment): when neither threshold is
  reached (or the group threshold is "never"), the answer is the selected analysis at the `PartOfSpeech` level with the
  probability of its slot as the confidence, and the ranked senses of the analysis still show the best sense and its group.
- **Step to the part of speech** (`LemmaWithoutGroups_StepsFromSenseToPartOfSpeech`; scoring increment): for a lemma that
  has no groups, a fine margin below the fine threshold gives the selected analysis at the `PartOfSpeech` level with the
  probability of its slot as the confidence, never a `Group` answer and never level `None`.
- **Analyses and senses** (`TrainedLemma_ListsRankedCandidatesSummingToOne`, `SingleSenseWord_ListsNoCandidates`,
  `LemmaWithoutWeights_ListsNoCandidates`; scoring increment): a trained lemma lists every analysis with its probability
  and, inside each, the senses ranked by their probability given the analysis, the most probable first, with
  probabilities that sum to one within a small tolerance, whatever the level answered; equal probabilities keep
  inventory order; a single-sense word and a lemma without weights list no scored senses.
- **Unusable vector** (`DimensionMismatch_FallsBackToPartOfSpeech`; scoring increment): a vector whose dimension differs
  from the weights, or a target without word pieces, gives the lexicon-only answer at the `PartOfSpeech` level, not
  unknown.
- **Form of several lemmas** (`FormOfTwoLemmas_ChoosesTheLemmaFromContext`,
  `ReadPastVersusPresent_ChoosesTheTenseFeatures`, `FormChooserBelowThreshold_AnswersPartOfSpeech`; scoring
  increment): a form such as `left` is answered
  with the senses of the analysis that its chooser picks from the sentence; `read` gets its tense features from the
  chooser, and the verb as a whole (both tenses) is the slot whose probability the confidence is built on; a chooser
  margin below its threshold gives the `PartOfSpeech` level. The shipped weights carry the choosers of the forms whose
  chooser rows cover every analysis (the builder trains them from `corpus/choosers`); the test that every form of the
  lexicon whose analyses belong to several lemmas has a chooser (`EveryMultiLemmaForm_HasAChooser`) stays deferred until
  the quota filling of the corpus gives every such form rows for each of its lemmas; until then a form without a
  chooser is answered from the lexicon.
- **Words are returned as written**: the result array is parallel to the input, holds each word as given, and is
  case-insensitive in lookup (including the curly apostrophe).
- **Empty list and argument validation** (`EmptyList_ReturnsEmpty`, `NullWords_Throws`, `NullElement_Throws`,
  `Cancelled_Throws`): an empty list gives an empty array; a null word list and a null word throw
  `ArgumentException` or `ArgumentNullException`; a cancelled token throws `OperationCanceledException`.
