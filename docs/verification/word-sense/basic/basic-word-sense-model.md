### BasicWordSenseModel Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Basic.Tests/BasicWordSenseModelTests.cs` use the public type with the embedded
lexicon and the embedded statistics table. The model has no files and no state, so nothing is mocked.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **EnsureModelAsync is a no-op**: the returned task completes without touching the file system or the network, and a
  mirror passed in (even an unusual one) is ignored.
- **ClassifyAsync works without setup**: `ClassifyAsync` works without calling `EnsureModelAsync` first and answers a
  word list parallel to the input; lexicon-only words need no ensure at all.
- **Classification is thread-safe**: parallel calls on one model give the same results as sequential calls.
- **Part of speech answers with the embedded statistics**
  (`BasicWordSenseModel_ClassifyAsync_AuxiliaryContext_AnswersAuxiliary`, `..._CopulaBe_AnswersMainVerb`,
  `..._NounContext_StaysNoun`, `..._NeedAsMainVerb_IsNotAuxiliary`): with the embedded lexicon and table, a modal or a
  helper in its context is answered as an auxiliary analysis, the copula `be` as a verb, a noun after an article or
  possessive stays a noun, and `need` as a main verb is not an auxiliary; each answer is at the part of speech level.
- **Cancelled token is honored**: `EnsureModelAsync` with an already cancelled token returns a cancelled task, and
  `ClassifyAsync` with one throws `OperationCanceledException`. A null word list is rejected.
- **Disposal**: `Dispose` is idempotent, thread-safe and releases nothing, so a classification that is already
  running completes normally. After it, `EnsureModelAsync` and
  `ClassifyAsync` throw `ObjectDisposedException`, whatever the words (including an empty list).
