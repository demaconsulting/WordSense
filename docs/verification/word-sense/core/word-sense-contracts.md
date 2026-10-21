### WordSenseContracts Verification

#### Verification Approach

The unit groups the public contract types `IWordSenseModel`, `FallbackWordSenseModel`, `MirrorOptions`,
`ClassifyResult`, `AnalysisCandidate`, `SenseCandidate`, `SenseLevel`, `WordDetails`, `PartOfSpeech`, `SenseInfo` and
`SenseGroup`. Tests in `test/DemaConsulting.WordSense.Tests` (`ContractsTests.cs`, and the composite tests of
`FallbackWordSenseModel`) exercise the records and the enums directly and implement the interface with small test
doubles to show it is usable without a model package and to drive the composite: a double returns prepared
`ClassifyResult` arrays, counts its ensure and dispose calls and can throw or return an array of the wrong length.
Nothing else is mocked.

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
  `SenseInfo_SameValues_AreEqual`, `SenseGroup_Construct_ExposesMembers`, `SenseGroup_SameMembers_AreEqual`): a sense
  exposes its id, group id (null when
  the sense is in no group), lemma,
  part of speech, key and respelling; a project sense has a null key; equal values are equal; a group exposes its
  member sense ids, and two groups are equal when their values and the sequence of their member ids are equal (content
  and order, with equal hash codes), not merely when they share the same array.
- **Fallback uses the primary result when it scored senses**
  (`FallbackWordSenseModel_PrimaryScoredSenses_UsesPrimary`): for a word whose primary result has a selected analysis
  with scored senses, or the level group or sense, the composite returns exactly the primary result (the same analyses,
  level, confidence and ids), not the fallback result.
- **Fallback uses the fallback result otherwise** (`FallbackWordSenseModel_PrimaryChoseOnlyAnalysis_UsesFallback`): for
  a word that the primary model did not know, or only answered at the part of speech level without scored senses, the
  composite returns the complete fallback result; in a list with both kinds of word each position takes its own
  result, and no result is a mixture of the two.
- **Fallback ensures and disposes both models**
  (`FallbackWordSenseModel_EnsureModelAsync_EnsuresBoth`,
  `FallbackWordSenseModel_EnsureModelAsync_PrimaryFails_StillEnsuresFallback`,
  `FallbackWordSenseModel_Dispose_DisposesBothOnce`): one
  ensure call starts the ensure of both models with the same mirror before either completes and completes when both
  have; when the primary model fails (by throwing before it returns a task, or by a faulted task) the fallback model is
  still ensured and the failure is reported afterwards; disposing twice disposes each model once, and an ensure or a
  classification after the disposal throws
  `ObjectDisposedException`.
- **Fallback validates its arguments and the parallel results**
  (`FallbackWordSenseModel_ClassifyAsync_InvalidArguments_Throw`, `FallbackWordSenseModel_ClassifyAsync_ResultNotParallel_Throws`):
  a null model, a null list and a null element are rejected with the documented exceptions, and a model that returns an
  array of another length than the input makes the call throw `InvalidDataException`.
- **MirrorOptions defaults**: only the base URI is required; bearer token, credentials, store root and timeout default to
  null.
- **MirrorOptions hides secrets**: `ToString` shows neither the bearer token nor a credential; each is shown as `***` and
  an absent one as `null`; the user-info, query and fragment of the base address are never printed, and a token or
  credential never appears however the options are formatted.
- **Interface is implementable**: a test double implements `IWordSenseModel` (`EnsureModelAsync`, `ClassifyAsync` over
  a word list) and is used through the interface only.
- **Ownership**: `IWordSenseModel` is `IDisposable` (so `using` disposes any model without a cast), is the only
  classification contract, and has no `Create` method.
