### WordSenseContracts Verification

#### Verification Approach

The unit groups the public contract types `IWordSenseModel`, `IDownloadableWordSenseModel`, `FallbackWordSenseModel`,
`DownloadOptions`, the typed exceptions `WordSenseException`, `ModelUnavailableException`, `ModelIntegrityException` and
`DownloadConfigurationException`, `ClassifyResult`, `AnalysisCandidate`, `SenseCandidate`, `SenseLevel`, `WordDetails`,
`PartOfSpeech`, `SenseInfo` and `SenseGroup`. Tests in `test/DemaConsulting.WordSense.Tests` (`ContractsTests.cs`,
`WordSenseExceptionTests.cs` and the composite tests of `FallbackWordSenseModel`) exercise the records, the enums and
the exceptions directly and implement the interfaces with small test doubles to show they are usable without a model
package and to drive the composite: a double returns prepared `ClassifyResult` arrays, counts its ensure and dispose
calls (a double of the downloadable capability also records the options and the token it receives) and can throw or
return an array of the wrong length. Nothing else is mocked.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **ClassifyResult is a value** (`ClassifyResult_SameValues_AreEqual`): two `ClassifyResult` values with equal word,
  `IsKnown`, details, level, ids, confidence and analyses are equal, and `ClassifyResult` is a struct, so an array of
  results has no null element.
- **Level decides which ids are set** (`ClassifyResult_LevelDecidesWhichIdsAreSet`): a result at the sense level carries
  a sense id and carries a group id only when the sense is in a group, at the group level only a group id, and at the
  part of speech and none levels neither; the confidence is the probability of the slot (lemma and part of speech) of the
  selected analysis at the part of
  speech level, that probability times the member sense mass at the group level and that probability times the sense
  probability at the sense level; details are null only for an unknown word, which is not known, has no analyses, level
  none and confidence 0 (`ClassifyResult_NoDetails_HasNullDetails`).
- **Default result is valid** (`ClassifyResult_Default_HasEmptyArraysAndDoesNotThrow`): `default(ClassifyResult)` has the
  empty word, `IsKnown` false, empty analyses, null details and ids, level none and confidence 0, and reading any
  property does not throw.
- **Analyses** (`ClassifyResult_Analyses_AreKeptInOrder`, `ClassifyResult_SameAnalyses_AreEqual`,
  `ClassifyResult_DifferentAnalyses_AreNotEqual`, `ClassifyResult_Unknown_SharesEmptyArrays`): the analyses are kept in
  the order given; two results with the same analyses item by item (senses included) are equal (and have the same hash
  code) and two results that differ in one analysis, one sense or the order are not; the results of two unknown words
  share the same empty analyses array.
- **AnalysisCandidate and SenseCandidate** (`AnalysisCandidate_Senses_DefaultIsEmpty`,
  `AnalysisCandidate_SameValues_AreEqual`, `AnalysisCandidate_SensesRankedByUsage_DefaultsToContextScored`,
  `SenseCandidate_SameValues_AreEqual`): an analysis built without senses, or
  with the default array, has the empty list and never a default array; two analyses with the same details, probability
  and senses are equal; a candidate built with three arguments, a `default` candidate and one built with the argument
  false have `SensesRankedByUsage` false (the default of a third-party model that lists senses), one built with the
  argument true has it true, and the flag takes part in the equality; two `SenseCandidate` values with the same sense id
  and probability are equal.
- **SenseLevel has four ordered values** (`SenseLevel_Values_AreTheFourLevels`, `SenseLevel_Order_IncreasesWithPrecision`):
  none, part of speech, group and sense, in order of increasing precision.
- **WordDetails equality and features** (`WordDetails_Construct_ExposesMembers`, `WordDetails_SameValues_AreEqual`,
  `WordDetails_TryGetFeature_FindsValue`, `WordDetails_TryGetFeature_MissingFeature_ReturnsFalse`): two `WordDetails`
  with equal lemma, part of speech and features are equal, and the type has no sense id; `TryGetFeature` finds the value
  of a named feature (such as `Tense`) among the `+`-joined features, and returns false with a null value for a missing
  feature, an empty name or empty features.
- **PartOfSpeech has ten values** (`PartOfSpeech_Values_AreTheTenClasses`): noun, pronoun, verb, adjective, adverb,
  preposition, conjunction, interjection, determiner and auxiliary; a modal or primary auxiliary lists `Auxiliary` as
  its last analysis.
- **SenseInfo and SenseGroup records** (`SenseInfo_Construct_ExposesMembers`, `SenseInfo_ProjectSense_HasNoKey`,
  `SenseInfo_SameValues_AreEqual`, `SenseGroup_Construct_ExposesMembers`, `SenseGroup_SameMembers_AreEqual`,
  `WordSenseContracts_SenseInfo_HasNoRespellingMember`): a sense
  exposes its id, group id (null when the sense is in no
  group), lemma, part of speech and key, and a group its id, lemma, part of speech and member sense ids; neither has a
  respelling member (checked by reflection, for both records, and for `WordDetails` in
  `WordDetails_Construct_ExposesMembers`); a project sense
  has a null key; equal values are equal;
  two groups are equal when their values and the sequence of their member ids are equal (content and order, with equal
  hash codes), not merely when they share the same array.
- **Fallback uses the primary result when it scored senses**
  (`FallbackWordSenseModel_PrimaryScoredSenses_UsesPrimary`): for a word whose primary result has a selected analysis
  with scored senses, or the level group or sense, the composite returns exactly the primary result (the same analyses,
  level, confidence and ids), not the fallback result.
- **Fallback uses the fallback result otherwise** (`FallbackWordSenseModel_PrimaryChoseOnlyAnalysis_UsesFallback`): for
  a word that the primary model did not know, or only answered at the part of speech level without scored senses (also
  one whose senses are only ranked by usage), the
  composite returns the complete fallback result; in a list with both kinds of word each position takes its own
  result, and no result is a mixture of the two.
- **Fallback gives tied variants the proportions of the fallback**
  (`FallbackWordSenseModel_TiedVariantsOfASlot_TakeTheProportionsOfTheFallback`,
  `..._TiedVariants_SelectedAnalysisFollowsTheFallbackSplit`, `..._TiedSlotThatIsNotSelected_IsSplitAndLeavesTheSelectedAnalysis`):
  an encoder result that ties the past and the infinitive of `put` at 0.3 each (slot mass 0.6, sensed, at the sense level)
  and a fallback result that says 6 to 1 for the infinitive give the infinitive 0.6 * 6/7 and the past 0.6/7, listed in
  that order, with the selected analysis the infinitive, the noun at 0.4, the senses of the variants, the level, the
  confidence and the ids of the encoder result unchanged; a fallback that says past (0.5 to 0.1) selects the past, and
  the probabilities are applied; a tied slot that is not the selected one is split and the selected noun stays.
- **Fallback keeps the primary order of equal shares**
  (`..._TiedVariants_EqualShares_KeepTheOrderOfThePrimaryResult`): three tied variants of the verb slot (0.2 each) and a
  fallback that ranks the finite form first (0.4) and gives the past and the infinitive 0.1 each list the finite form
  first, then the past and the infinitive in the order of the primary result.
- **Fallback leaves the rest alone** (`..._UnequalVariants_AreLeftAsThePrimaryModelSplitThem`,
  `..._TieTolerance_IsOneNanoUnit`, `..._FallbackWithoutDistinction_LeavesThePrimaryResult`): a slot the encoder split
  0.45 to 0.15, a slot whose variants differ by 2e-9, a fallback that ties the variants or does not list one of them, and
  a result with a single analysis are returned exactly as the primary model gave them; a difference of 5e-10 is a tie.
- **Slot totals, level and confidence are kept** (`..._ManyInputs_KeepSlotTotalsLevelAndConfidence`): over 200
  deterministic pairs of results, the probability of every slot, the level, the confidence and the sense id are those of
  the primary result, the probabilities sum to one and the selected analysis is the first analysis.
- **Fallback combines the slot probabilities at the part of speech level**
  (`..._PartOfSpeechLevel_CombinesTheSlotProbabilities`, `..._PrimaryWeight_OneKeepsThePrimaryZeroTakesTheFallback`,
  `..._SlotOfOneResultOnly_CountsWithTheFloor`): an encoder result at the part of speech level that says noun 0.7 and
  verb 0.3 (the two verb variants tied) and a fallback that says verb 0.95 and noun 0.05 give, for the primary weight
  0.5, the slot probabilities `sqrt(0.3 * 0.95)` and `sqrt(0.7 * 0.05)` normalized (verb about 0.74), the verb selected
  as the first analysis with that probability as the confidence, the level still part of speech and no group or sense id;
  the weight 1 gives the composition without a weight (the noun stays on top at 0.7) and the weight 0 gives the slot
  probabilities of the fallback (0.95 and 0.05); a slot that one result does not list counts with the floor 1e-6 (a noun
  the fallback lacks gets `sqrt(0.5 * 1e-6)` of the unnormalized mass), and a slot only the fallback lists enters the
  result with its own analyses, with the senses and the ranking kind (`SensesRankedByUsage`) that the fallback result
  lists for them.
- **Fallback takes the closed-class probability from the fallback result**
  (`..._ClosedClassSlots_TakeTheProbabilityOfTheFallbackAndOpenClassKeepsTheSense`,
  `..._ClosedClassSlotMostProbable_AnswersAtPartOfSpeechLevel`,
  `..._ClosedClassSlotMostProbable_FallbackSelectedAnother_AnswersAtPartOfSpeechLevel`,
  `..._ClosedClassSplit_NotApplicable_LeavesThePrimaryResult`,
  `..._ClosedClassSlots_KeepTheFallbackProbabilityWhenWeighted`,
`..._ClosedClassSlotWithZeroProbability_StaysZeroWhenWeighted`): an encoder result at the part-of-speech level that
lists the auxiliary with probability 0, combined with a fallback that gives the auxiliary 0 or omits it, keeps the
auxiliary at 0 with weight 0.6; an encoder result that gives the verb `have` all the mass
  (sense level, confidence 0.8) and lists the auxiliary and the pronoun with probability 0, combined with a fallback
  that says verb 0.7, auxiliary 0.2 and pronoun 0.1, has these slot probabilities, keeps the sense level, the sense id and
  the verb as the selected analysis, has the confidence 0.8 × 0.7 and lists the senses of the auxiliary from the
  fallback; with a fallback that says auxiliary 0.85 and names its only sense the answer is at the part-of-speech level
  with the auxiliary selected, the confidence 0.85 and no sense; with open mass split
  between a verb and a noun and a fallback of verb 0.4, auxiliary 0.35 and pronoun 0.25 that selects the verb, the answer
  is at the part-of-speech level with the auxiliary selected (0.35 against 0.2 and 0.2) and no sense; a primary result
  without an open-class slot, one that gives a closed-class slot a probability of its own, a fallback that lists none of
  the slots, and a result without a closed-class slot are left as they are; with the primary weight 0.6, an encoder
  result that splits its open mass evenly between a verb and a noun and a fallback of auxiliary 0.85, verb 0.10 and noun
  0.05 keeps the auxiliary at exactly 0.85 (the answer is at the part-of-speech level, the confidence 0.85), and the verb
  and the noun share the other 0.15 as `0.10^0.4` against `0.05^0.4` (0.0853 and 0.0647), not as a second combination
  of the closed-class slot with the fallback. Through the shipped
  models (`MiniLM_Classifier_EndToEnd_Have_AuxiliaryStaysAuxiliaryAndVerbGetsAVerbSense` and the BertBaseCased
  counterpart) the `has` of "He has been there all day ." is an auxiliary, and the `have` of "I have a car ." is a verb
  that lists only verb senses scored in context.
- **Fallback keeps a low-confidence single-sense answer below the sense level**
  (`..._SingleSenseAnswerBelowTarget_AnswersAtPartOfSpeechLevel`, `..._SingleSenseAnswerAtTarget_StaysAtSenseLevel`,
  `..._SingleAnalysis_StaysAtSenseLevel`, `..._SenseLevelWithoutExactlyOneSense_IsLeftAsItIs`): for a word that the
  primary model did not score, a fallback result at the sense level for a word with two slots (`more` as the adjective
  `much` 0.6, the adverb 0.4) is answered at the part-of-speech level with the same analyses, selected analysis and
  confidence 0.6 and no group or sense id; the same result at the confidence 0.97 (the target itself) and 0.995 stays at
  the sense level and is returned unchanged; a word with a single analysis and confidence 1 stays at the sense level; a
  sense-level result whose selected analysis lists two senses is not the single-sense rule and is returned unchanged.
- **Fallback does not take a usage ranking for a score**
  (`..._BasicAsPrimary_UsageRankedSenses_AreNotRefinedOrReconciled`, `..._BasicWithBasic_ReturnsTheBasicAnswerAsItIs`,
  `..._PrimaryWithContextScoredSenses_StillReconciled`, `..._Reconcile_KeepsTheRankingKindOfEveryAnalysis`): the real
  Basic result of `record` (two slots, the selected analysis listing two or more senses ranked by usage,
  `SensesRankedByUsage` true, level part of speech) used as the primary result is replaced by the fallback result as it
  is,
  for the primary weights 0.5 and 0, and with the real Basic model as the primary; a composite of Basic with Basic returns
  the Basic answers unchanged at the weights 0, 0.5 and 1; the same analyses declared as scored in context (the
  three-argument constructor) are used and reconciled (weight 1 keeps them, 0.5 changes the result, and every analysis
  keeps its senses); a slot that only the fallback lists keeps its `SensesRankedByUsage` flag in the reconciled result
  while the slot of the primary stays false.
- **Fallback keeps the variants and the senses when it combines**
  (`..._PartOfSpeechLevel_KeepsTheShareOfTheVariantsAndTheirSenses`, `..._PartOfSpeechLevel_KeepsTheTieSplitOfTheFallback`):
  verb variants that the encoder split 1 to 2 are still 1 to 2 in the combined result and keep their ranked senses; tied
  variants are first re-split with the proportions of the fallback (3 to 1) and keep that split after the combination.
- **Fallback leaves sense and group answers alone**
  (`..._SenseOrGroupLevel_IsLeftAsThePrimaryGaveIt`, `..._SingleSlotOrEmptyFallback_LeavesThePrimaryResult`): for a
  primary result at the sense level or at the group level, with the weight 0 and a fallback that says verb, the level,
  the confidence, the ids, the selected analysis and the probability of the noun slot are those of the primary result; a
  result of a single slot and a fallback without analyses are returned as they are.
- **Combined results stay consistent** (`..._PartOfSpeechLevel_ManyInputs_StayConsistent`): over 200 deterministic pairs
  of results and weights the probabilities sum to one, the selected analysis is the first analysis, the confidence is the
  probability of its slot, the level is part of speech, the number of analyses is unchanged and every analysis keeps its
  senses.
- **The weight is validated and reported**
  (`..._PrimaryWeight_OutsideZeroToOne_Throws`, `..._PrimaryWeight_IsReported`): a weight
  below 0, above 1, NaN or infinite throws `ArgumentOutOfRangeException`; 0, 0.6 and 1 are accepted and reported, and
  1 is reported when no weight is given.
- **Fallback ensures the capable members and disposes both models**
  (`FallbackWordSenseModel_IsDownloadable`, `FallbackWordSenseModel_EnsureModelAsync_EnsuresBoth`,
  `FallbackWordSenseModel_EnsureModelAsync_OnlyCapableMemberEnsured`,
  `FallbackWordSenseModel_EnsureModelAsync_NoCapableMember_CompletesImmediately`,
  `FallbackWordSenseModel_EnsureModelAsync_NoCapableMemberCancelled_ReturnsCanceledTask`,
  `FallbackWordSenseModel_EnsureModelAsync_PrimaryFails_StillEnsuresFallback`,
  `FallbackWordSenseModel_EnsureModelAsync_BothFail_ReportsAfterBothRan`,
  `FallbackWordSenseModel_Dispose_DisposesBothOnce`): the composite is an `IDownloadableWordSenseModel`; one ensure call
  starts the ensure of every member that implements `IDownloadableWordSenseModel` with the same options and token before
  any completes and completes when all have; a member that only implements `IWordSenseModel` is never asked (theory
  over the primary and the fallback position); with no capable member the call returns a completed task, or a cancelled
  task for an already cancelled token; when the primary model fails (by throwing before it returns a task, or by a
  faulted task) the fallback model is still ensured and the failure is reported afterwards, and when both fail the
  exception of the first failed member is reported unchanged (a `ModelUnavailableException` stays one) after both ran;
  disposing twice disposes each model once, and an ensure or a classification after the disposal throws
  `ObjectDisposedException`.
- **Fallback validates its arguments and the parallel results**
  (`FallbackWordSenseModel_ClassifyAsync_InvalidArguments_Throw`, `FallbackWordSenseModel_ClassifyAsync_ResultNotParallel_Throws`):
  a null model, a null list and a null element are rejected with the documented exceptions, and a model that returns an
  array of another length than the input makes the call throw `InvalidDataException`.
- **DownloadOptions defaults and construction** (`DownloadOptions_IsSealedRecord`, `DownloadOptions_Defaults_AreNull`,
  `DownloadOptions_InvalidValues_AreNotRejectedByTheConstructor`, each prefixed `WordSenseContracts_`): the type is a
  sealed record and the former `MirrorOptions` type no longer exists; only the base URI is required and bearer token,
  credentials, store root and timeout default to null; the constructor accepts any values (a relative address, a token
  together with credentials, a zero or negative timeout) because the model that uses the options validates them and
  reports `DownloadConfigurationException`.
- **DownloadOptions hides secrets** (`WordSenseContracts_DownloadOptions_ToString_RedactsSecrets`,
  `WordSenseContracts_DownloadOptions_ToString_HidesUserInfoQueryAndFragment`,
  `WordSenseContracts_DownloadOptions_ToString_NeverPrintsTokenOrCredentials`): `ToString` shows neither the bearer
  token nor a credential; each is shown as `***` and an absent one as `null`; the user-info, query and fragment of the
  base address are never printed, and a token or credential never appears however the options are formatted.
- **Interfaces are implementable**
  (`WordSenseContracts_IWordSenseModel_TestDouble_IsUsableThroughTheInterface`,
  `WordSenseContracts_IDownloadableWordSenseModel_TestDouble_IsUsableThroughTheInterface`): a test double implements
  `IWordSenseModel` (`ClassifyAsync` over a word list) and is used through the interface only, and a type check shows
  it lacks the capability; a second double implements `IDownloadableWordSenseModel`, is held as `IWordSenseModel`,
  reaches the capability with a type check and receives the options in `EnsureModelAsync`.
- **Ownership and capability** (`WordSenseContracts_IWordSenseModel_IsDisposable`,
  `WordSenseContracts_IWordSenseModel_HasNoEnsureMember`,
  `WordSenseContracts_IDownloadableWordSenseModel_ExtendsIWordSenseModel`): `IWordSenseModel` is `IDisposable` (so
  `using` disposes any model without a cast), is the only classification contract, has no `Create` method and declares
  only `ClassifyAsync`, no `EnsureModelAsync`; `IDownloadableWordSenseModel` is an interface of the core package that
  extends it and declares `EnsureModelAsync` with the optional `DownloadOptions` and the optional cancellation token.
- **Typed exceptions** (`WordSenseExceptions_WordSenseException_IsAbstractPublicException`,
  `WordSenseExceptions_Hierarchy_DerivesFromWordSenseException`,
  `WordSenseExceptions_Constructors_AreTheThreeStandardOnes`,
  `WordSenseExceptions_Constructors_KeepMessageAndInnerException`,
  `WordSenseExceptions_CatchBase_CatchesEveryTypedException`): `WordSenseException` is a public abstract `Exception`;
  `ModelUnavailableException`, `ModelIntegrityException` and `DownloadConfigurationException` are public sealed types
  derived from it and none of them is an `ArgumentException`; each has exactly the constructors `()`, `(string)` and
  `(string, Exception)`, keeping the message and the original exception as the inner exception; a handler for the base
  type catches every typed exception.
- **Key of a result** (`ClassifyResult_Key_SenseLevel_HasTheLabelOfTheAnsweredSense`,
  `ClassifyResult_Key_GroupId_IsNeverUsed`, `ClassifyResult_Key_RankedSenses_FallBackToTheTopRankedSense`,
  `ClassifyResult_Key_TiedRankedSenses_TakeTheFirstListed`,
  `ClassifyResult_Key_RankingOfAnotherAnalysis_IsNotUsed`,
  `ClassifyResult_Key_PartOfSpeechLevelWithoutRanking_HasNoLabel`,
  `ClassifyResult_Key_EmptyFeatures_LeaveOutTheFeatureSegment`, `ClassifyResult_Key_UnknownOrUnanswered_IsNull`,
  `ClassifyResult_Key_CapitalizedWordAndLemma_NormalizeOnlyTheSpelling`,
  `ClassifyResult_Key_ContractionAndPossessive_AreKeyedBySpelling`): results built by hand and results of the Basic model
  give `bass|bass/Noun/Number=Sing#sea-bass` at the sense level whatever the ranking says; a group id never appears in a
  key; a group-level and a part-of-speech-level result take the label of the most probable ranked sense of the selected
  analysis (the unsorted list is searched, a tie goes to the first listed sense, the ranking of another analysis is
  ignored); a
  part-of-speech result without a ranking has no label; empty features leave out the feature segment; an unknown word, a
  default result and a known word without selected analysis have a null key; `POLISH` with the lemma `Polish` is
  `polish|Polish/Noun/Number=Sing` and `Don’t` with a typographic apostrophe is spelled `don't`; the Basic model gives the
  possessive `dog's` the features `Number=Sing+Poss=Yes`, and every key it gives is enumerated.
- **WordKey value, text and parser** (`WordKey_Value_SameParts_AreEqual`,
  `WordKey_ToString_Parts_AreWrittenInTheCanonicalOrder`,
  `WordKey_ToString_SeparatorsInParts_AreEscaped`, `WordKey_TryParse_ValidKey_RoundTrips`,
  `WordKey_TryParse_EscapedKey_GivesTheUnescapedParts`, `WordKey_TryParse_NotAKey_IsRefused`): two keys with the same
  parts are equal and have one hash code; the parts are written in
  the canonical order with the optional segments left out; a backslash, bar, slash and number sign inside a part are
  escaped (`a\/c|A\/C/Noun/Number=Sing#x\#y\|z\\w`) and nothing else is; the text of a key parses back to an equal key
  and
  formats to the same text; text that is not canonical (no part, an unknown or lower-case part of speech name, a number
  as part of speech, an empty features or label segment, an extra segment, an unescaped separator, a dangling or unknown
  escape, a spelling that is not its own normalized form such as `POLISH` or one with a typographic apostrophe or without
  a letter, a null) is refused.
- **Keys of the models are enumerated, sample verification** (`BasicKeys_SuiteAndCorpusSentences_AreAllEnumerated`,
  `MiniLMKeys_SuiteAndCorpusSentences_AreAllEnumerated`,
  `BertBaseCasedKeys_SuiteAndCorpusSentences_AreAllEnumerated`, all through the shared `KeyCompleteness` helper): the
  words of every row of the reviewed suite and of a sample (every 20th row, about 570 sentences) of the held-out corpus
  are classified with each model; a word has a key exactly when it is known, and every key is found in one pass over
  `WordLexicon.Keys()`. This is a sample: it can show a mistake in the guarantee but cannot prove it. The guarantee (the
  enumeration is a superset of the keys of every model) holds by construction: `WordKey.KeyOf` builds the key of a
  result from an analysis of the lookup of its word and at most a catalog sense of that analysis, and
  `WordImage.Keys` lists the key of every analysis of every spelling and of every catalog sense of its lemma and part of
  speech through the same `WordKey.Format`. The encoder tests are skipped when the model files are not available, unless
  `WORDSENSE_REQUIRE_MODELS` is set.
