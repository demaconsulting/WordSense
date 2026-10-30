### OnnxClassifier Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Onnx.Tests/OnnxClassifierTests.cs` call the classifier with word lists and the
real embedded lexicon, so the expected analyses, senses and levels follow from the lexicon. The lexicon-only scenarios
need no model, no encoder and no other model package. The scoring scenarios build the classifier over a fake encoder
(a delegate that returns designed vectors and counts its calls) and hand-made weights over the real senses of the
shipped catalog, so the expected sense, group and confidence follow from the arithmetic; the thresholds of the weights
are chosen so that a specific level is reached. The part-of-speech evidence scenarios are in
`OnnxClassifierPosTests.cs`: they build the classifier over a hand-made head, whose weights make the vector of a word
point to a designed part of speech, and a fake view encoder that splits the possessives like the real one, returns
designed vectors and records what it was asked, so the expected probabilities follow from the arithmetic of the head
(`PosHead.Restrict`) and of the shares. Real-model behavior is verified in the MiniLM and BertBaseCased
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
  never holds all the senses of its lemma and part of speech) and confidence 1, and the encoder is not asked for it. This
  lexical singleton sense is listed with probability 1 but is not encoder-scored; it is the only sense that a result
  lists without a scored lemma.
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
  `OnlyAmbiguousWordsAreEncoded`, `NeedsEncoder_PendingWordsAndWordsOfSeveralPartsOfSpeech_AreTrue`,
  `WordsOfOnePartOfSpeech_DoNotRunTheEncoder`; scoring increment): the encoder runs once for a text in which at least one
  word has several senses and usable weights, only for those words, and never for a text in which no word does.
  `NeedsEncoder` is true for a pending word, and, with a head, for a known word of two or more distinct parts of
  speech whatever its lemma, and false for unknown words, words of one sense and known words of one part of speech that
  are not pending; a text of such words does not run the encoder even with a head.
- **Part of speech from the head** (`UntrainedWordOfSeveralPartsOfSpeech_TakesTheRestrictedPosterior`,
  `UntrainedWord_FollowsTheHead`, `SeveralSlotsOfOnePartOfSpeech_ShareItsProbabilityEqually`; part-of-speech
  increment): a word of two parts of speech whose lemma has no weights gets the posterior of the head restricted to the
  parts of speech of the lexicon and renormalized, at the `PartOfSpeech` level with no scored senses, the slots listed
  most probable first; the same word gets the other part of speech when the vector says so; slots of one part of speech
  without scored senses share its probability equally.
- **Closed-class slots with a head** (`ClosedClassSlots_GetPartOfSpeechMassAndNoSenses`): a closed-class slot of a word
  of several parts of speech has the head's probability of its part of speech and lists no senses.
- **Head and senses agree or disagree** (`TrainedLemma_HeadAgrees_KeepsTheSenseLevelWithTheHeadProbability`,
  `TrainedLemma_HeadDisagrees_StepsDownToThePartOfSpeech`, `TrainedLemma_SensesInsideASlotDoNotDependOnTheHead`): when
  the head ranks the slot of the best sense first the answer keeps its level and its confidence is the probability of
  the part of speech times the probability of the sense within the slot; when it ranks another slot first the answer is
  at the `PartOfSpeech` level with the probability of that slot, and the senses of every slot are still listed; the ranked
  senses of every analysis equal those of a classifier without a head.
- **Neighbors and the possessive view** (`Neighbors_AreThoseOfThePossessiveView`, `NextWord_ReachesTheHead`): the head
  reads the vectors of the previous and the next token of the view, the first word has no previous vector, and across a
  possessive written as one token the marker is the neighbor of the stem and of the next word.
- **Vector of a pending word** (`PendingPossessive_UsesTheJoinedVector`, `PendingWordKeptWhole_UsesTheViewVector`): a
  possessive of a trained lemma is scored with its vector pooled over all its pieces from a second pass for that
  target; a word that the view keeps whole is scored with the vector of the view and needs no second pass.
- **No usable vector for the head** (`WordWithoutVector_IsAnsweredFromTheLexicon`,
  `VectorOfAnotherSize_IsAnsweredFromTheLexicon`): a word without a vector, or with a vector that does not have the
  hidden size of the head, keeps the equal split of the lexicon.
- **Invariants** (`ManyWords_KeepTheInvariants`): over many words and designed vectors the probabilities sum to one,
  the analyses are ordered by their slot, the selected analysis is the first one, a closed-class slot lists no senses,
  an answer at the part-of-speech level has the probability of its slot as confidence and an answer at a finer level
  lies in the selected slot with a confidence not above the probability of the slot.
- **Head without encoders** (`Constructor_HeadWithoutEncoders_Throws`): a classifier needs both the head and its view
  encoder.
- **Senses of every analysis** (`WordWithSeveralAnalyses_RanksTheSensesOfEveryAnalysis`,
  `ClosedClassUseOfLike_IsNotScored`, `ClosedClassSlotOfAnUnscoredLemma_HasNoProbability`,
  `ClosedOnlyChooserLabel_GivesItsProbabilityToNoSlot`, `EveryLabelClosedOnly_SharesEquallyAndIsFinite`,
  `InflectedForm_RanksTheSensesOfItsLemma`; scoring increment): for a classifier without a head, a chooser label that
  stands for the auxiliary `be` only
  (`there's`) gives its probability to no slot, so the adverb takes all of it and the auxiliary stays at 0; a form whose
  two labels are both closed-only (`that's`) shares equally and stays finite; the determiner of `more` (a lemma without
  weights) has probability 0 while the open-class slots of untrained lemmas still share the mass; a word with
  several analyses ranks the fine senses of all its open-class analyses; the preposition use of `like` is listed with
  probability 0 and without senses, even when its sense has the highest score; an inflected form ranks the senses of its
  lemma; no sense of another lemma competes; the probability of a lemma and part of speech is the mass of its senses.
- **Shared candidate selection** (`CandidatesOf_FormColumnsAndPlan_AreThoseOfInference`): for the lemma `leave` and the
  form `left` (a form of two lemmas) the encoder runs only when the table has the chooser of the form; the columns are the
  verb senses of `leave` in both cases; an unknown word has no columns and a lemma that the table does not have has none.
  `EncoderRuns_FormOfSeveralLemmas_NeedsItsChooser` gives the same answer from the chooser forms alone (no table): a
  form of several lemmas runs the encoder only with a chooser, a form of one lemma with two candidates runs it, and an
  unknown word, or one with fewer than two candidates, does not.
- **Outcome of the scoring** (`ScoreForm_ChooserBelowItsThreshold_TheLemmaThresholdDoesNotDecide`,
  `ScoreForm_OtherEarlyExits_AreOutcomesThatInferenceShares`): the form `left` with tied chooser scores (a margin below
  the chooser threshold) has the outcome "chooser not confident" and is answered at the part-of-speech level, and
  decides by the threshold of `leave` when the chooser is confident; a chooser that prefers the lemma `left` has the
  outcome "selected analysis of another lemma" for `leave`; a form without a chooser has "the encoder does not run";
  a lemma that is not a lemma of the form is "not scored"; an adjective sense that leads while the adverb analysis is
  selected is "best sense of another part of speech" and is answered at the part-of-speech level.
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
- **Analyses and senses** (`TrainedLemma_ListsRankedCandidatesSummingToOne`, `SingleSenseWord_ListsItsOneUnscoredSense`,
  `LemmaWithoutWeights_ListsNoCandidates`; scoring increment): a trained lemma lists every analysis with its probability
  and, inside each, the senses ranked by their probability given the analysis, the most probable first, with
  probabilities that sum to one within a small tolerance, whatever the level answered; equal probabilities keep
  inventory order; a lemma without weights lists no scored senses, and a single-sense word lists its one sense with
  probability 1 as the one unscored candidate (the encoder is never called for it).
- **Unusable vector** (`DimensionMismatch_FallsBackToPartOfSpeech`; scoring increment): a vector whose dimension differs
  from the weights, or a target without word pieces, gives the lexicon-only answer at the `PartOfSpeech` level, not
  unknown.
- **Form of several lemmas** (`FormOfTwoLemmas_ChoosesTheLemmaFromContext`,
  `ReadPastVersusPresent_ChoosesTheTenseFeatures`, `FormChooserBelowThreshold_AnswersPartOfSpeech`; scoring
  increment): a form such as `left` is answered
  with the senses of the analysis that its chooser picks from the sentence; `read` gets its tense features from the
  chooser, and the verb as a whole (both tenses) is the slot whose probability the confidence is built on; a chooser
  margin below its threshold gives the `PartOfSpeech` level.
- **Form of a trained and an untrained lemma**
  (`FormOfTrainedAndUntrainedLemma_ChooserPicksTrained_AnswersTheSense`,
  `FormOfTrainedAndUntrainedLemma_ChooserPicksUntrained_AnswersPartOfSpeech`): when only one lemma of a form has
  weights, the chooser still splits the probability between the lemmas. If it prefers the trained lemma, the answer is
  at the level of its sense scores and the analyses of the untrained lemma share the rest of the probability and list no
  scored senses. If it prefers the untrained lemma, the answer is at the `PartOfSpeech` level with no sense or group id,
  the confidence is the probability of the selected slot (the label probability shared equally by the slots of the
  untrained lemma) and no analysis lists scored senses.
- **Form of several lemmas without a chooser** (`FormOfSeveralLemmasWithoutChooser_IsAnsweredFromTheLexicon`): the
  encoder is not run and the form is answered from the lexicon alone, at the `PartOfSpeech` level without scored
  senses. The shipped weights carry the choosers of the forms whose chooser rows cover every analysis (the builder
  trains them from `corpus/choosers`); not every form of several lemmas has one yet, which is why this lexicon-only
  answer exists.
- **Words are returned as written**: the result array is parallel to the input, holds each word as given, and is
  case-insensitive in lookup (including the curly apostrophe).
- **Empty list and argument validation** (`EmptyList_ReturnsEmpty`, `NullWords_Throws`, `NullElement_Throws`,
  `Cancelled_Throws`): an empty list gives an empty array; a null word list and a null word throw
  `ArgumentException` or `ArgumentNullException`; a cancelled token throws `OperationCanceledException`.
