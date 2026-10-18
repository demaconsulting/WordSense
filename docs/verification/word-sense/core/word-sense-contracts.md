### WordSenseContracts Verification

#### Verification Approach

The unit groups the public contract types `IWordSenseModel`, `MirrorOptions`, `WordSense`,
`WordDetails` and `PartOfSpeech`. Tests in `test/DemaConsulting.WordSense.Tests/ContractsTests.cs` exercise the records
and the enum directly and implement the interface with a small test double to show it is usable without a model
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
- **PartOfSpeech has ten values**: noun, pronoun, verb, adjective, adverb, preposition, conjunction, interjection,
  determiner and auxiliary; a modal or primary auxiliary lists `Auxiliary` as its last sense.
- **MirrorOptions defaults**: only the base URI is required; bearer token, credentials, store root and timeout default to
  null.
- **MirrorOptions hides secrets**: `ToString` shows neither the bearer token nor a credential; each is shown as `***` and
  an absent one as `null`; the user-info, query and fragment of the base address are never printed, and a token or
  credential never appears however the options are formatted.
- **Interface is implementable**: a test double implements `IWordSenseModel` (`EnsureModelAsync`, `ClassifyAsync` over
  a word list) and is used through the interface only.
- **Ownership**: `IWordSenseModel` is `IDisposable` (so `using` disposes any model without a cast), is the only
  classification contract, and has no `Create` method.
