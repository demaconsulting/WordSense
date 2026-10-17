### WordSenseContracts Verification

#### Verification Approach

The unit groups the public contract types `IWordSenseModel`, `MirrorOptions`, `IWordSenseClassifier`, `WordSense`,
`WordDetails` and `PartOfSpeech`. Tests in `test/DemaConsulting.WordSense.Tests/ContractsTests.cs` exercise the records
and the enum directly and implement the two interfaces with small test doubles to show they are usable without a model
package. Nothing else is mocked.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **WordSense is a value**: two `WordSense` values with equal word, details and confidence are equal, and `WordSense` is
  a struct, so an array of results has no null element.
- **WordDetails equality**: two `WordDetails` with equal lemma, part of speech, meaning and respelling are equal; the
  meaning and the respelling may be null.
- **PartOfSpeech has eight values**: noun, pronoun, verb, adjective, adverb, preposition, conjunction and interjection.
- **MirrorOptions defaults**: only the base URI is required; bearer token, credentials, store root and timeout default to
  null.
- **MirrorOptions hides secrets**: `ToString` shows neither the bearer token nor a credential; each is shown as `***` and
  an absent one as `null`; the user-info, query and fragment of the base address are never printed, and a token or
  credential never appears however the options are formatted.
- **Interfaces are implementable**: a test double implements `IWordSenseModel` (`EnsureModelAsync`, `Create`) and
  `IWordSenseClassifier` (`ClassifyAsync` over a word list) and is used through the interfaces only.
- **Ownership**: `IWordSenseModel` is `IDisposable` (so `using` disposes any model without a cast) and
  `IWordSenseClassifier` is not.
