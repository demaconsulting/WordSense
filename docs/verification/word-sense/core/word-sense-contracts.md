### WordSenseContracts Verification

#### Verification Approach

The unit groups the public contract types `IWordSenseModel`, `MirrorOptions`, `WordSense`, `SenseLevel`,
`WordDetails`, `PartOfSpeech`, `SenseInfo` and `SenseGroup`. Tests in
`test/DemaConsulting.WordSense.Tests/ContractsTests.cs` exercise the records and the enums directly and implement the
interface with a small test double to show it is usable without a model package. Nothing else is mocked.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **WordSense is a value** (`WordSense_SameValues_AreEqual`): two `WordSense` values with equal word, details, level,
  ids and confidence are equal, and `WordSense` is a struct, so an array of results has no null element.
- **Level decides which ids are set** (`WordSense_LevelDecidesWhichIdsAreSet`): a result at the sense level carries a
  sense id and a group id, at the group level only a group id, and at the part of speech and none levels neither;
  details are null only at level none (`WordSense_NoDetails_HasNullDetails`).
- **SenseLevel has four ordered values** (`SenseLevel_Values_AreTheFourLevels`, `SenseLevel_Order_IncreasesWithPrecision`):
  none, part of speech, group and sense, in order of increasing precision.
- **WordDetails equality** (`WordDetails_Construct_ExposesMembers`, `WordDetails_SameValues_AreEqual`): two
  `WordDetails` with equal lemma, part of speech and features are equal, and the type has no sense id.
- **PartOfSpeech has ten values** (`PartOfSpeech_Values_AreTheTenClasses`): noun, pronoun, verb, adjective, adverb,
  preposition, conjunction, interjection, determiner and auxiliary; a modal or primary auxiliary lists `Auxiliary` as
  its last analysis.
- **SenseInfo and SenseGroup records** (`SenseInfo_Construct_ExposesMembers`, `SenseInfo_ProjectSense_HasNoKey`,
  `SenseInfo_SameValues_AreEqual`, `SenseGroup_Construct_ExposesMembers`): a sense exposes its id, group id, lemma,
  part of speech, key and respelling; a project sense has a null key; equal values are equal; a group exposes its
  member sense ids.
- **MirrorOptions defaults**: only the base URI is required; bearer token, credentials, store root and timeout default to
  null.
- **MirrorOptions hides secrets**: `ToString` shows neither the bearer token nor a credential; each is shown as `***` and
  an absent one as `null`; the user-info, query and fragment of the base address are never printed, and a token or
  credential never appears however the options are formatted.
- **Interface is implementable**: a test double implements `IWordSenseModel` (`EnsureModelAsync`, `ClassifyAsync` over
  a word list) and is used through the interface only.
- **Ownership**: `IWordSenseModel` is `IDisposable` (so `using` disposes any model without a cast), is the only
  classification contract, and has no `Create` method.
