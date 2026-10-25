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
  `AnalysisCandidate_SameValues_AreEqual`, `SenseCandidate_SameValues_AreEqual`): an analysis built without senses, or
  with the default array, has the empty list and never a default array; two analyses with the same details, probability
  and senses are equal; two `SenseCandidate` values with the same sense id and probability are equal.
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
  a word that the primary model did not know, or only answered at the part of speech level without scored senses, the
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
  `ClassifyResult_Key_TiedRankedSenses_UseTheOrdinalOrderOfTheId`,
  `ClassifyResult_Key_RankingOfAnotherAnalysis_IsNotUsed`,
  `ClassifyResult_Key_PartOfSpeechLevelWithoutRanking_HasNoLabel`,
  `ClassifyResult_Key_EmptyFeatures_LeaveOutTheFeatureSegment`, `ClassifyResult_Key_UnknownOrUnanswered_IsNull`,
  `ClassifyResult_Key_CapitalizedWordAndLemma_NormalizeOnlyTheSpelling`,
  `ClassifyResult_Key_ContractionAndPossessive_AreKeyedBySpelling`): results built by hand and results of the Basic model
  give `bass|bass/Noun/Number=Sing#sea-bass` at the sense level whatever the ranking says; a group id never appears in a
  key; a group-level and a part-of-speech-level result take the label of the most probable ranked sense of the selected
  analysis (the unsorted list is searched, a tie goes to the smaller id, the ranking of another analysis is ignored); a
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
