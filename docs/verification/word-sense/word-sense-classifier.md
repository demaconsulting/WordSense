# WordSenseClassifier Verification

## Verification Approach

Tests in `WordSenseClassifierTests.cs` use a fake `IWordSenseEncoder` that returns deterministic
hidden states and a small in-memory table, so behavior is verified without the model. The exceptions are
`WordSense_Classifier_CreateAsync_RejectsUnverifiedDirectory` and
`WordSense_Classifier_CreateAsync_ValidatesOptionsBeforeModel`, which are in `WordSenseTests.cs`.

## Test Scenarios

- **WordSenseClassifier_ClassifyAsync_Text_TokensTileInput**: concatenated tokens equal the input.
- **WordSenseClassifier_ClassifyAsync_NoKnownWords_DoesNotEncode**: no encoder call without targets.
- **WordSenseClassifier_ClassifyAsync_Sentence_AssignsSenseToKnownWordOnly**,
  **..._Words_ResolvesForms**, **..._Words_EdgeCases**, **..._ManyTargets_EncodesOnce**,
  **WordSenseClassifier_KnownLemmas_And_GetSenses_AreExposed** (including `GetSenses(null)`): lemma resolution and
  single pass. **..._InfiniteAbstainMarginScale_AlwaysAbstains**: a positive-infinity scale is accepted and every
  word abstains.
- **WordSenseClassifier_ClassifyDetailAsync_ReportsMarginsAndRunnerUp**, **..._LowMargin_IsFlaggedAbstained**:
  detail margins and the abstained flag.
- **WordSenseClassifier_ClassifyTagAsync_ReturnsTagOrNull**, **WordSenseClassifier_IsTagAsync_ReturnsBoolOrNull**,
  **WordSenseClassifier_TagQueries_InvalidArguments_Throw**: tag queries.
- **WordSenseClassifier_ClassifyAsync_LowMargin_Abstains**: abstention; `ClassifyAsync` and `ClassifyDetailAsync`
  share one abstain predicate, so they agree.
- **WordSenseClassifier_ClassifyAsync_LongText_UsesWindowPerTarget**: windowing.
- **WordSenseClassifier_Constructor_InvalidArguments_Throw** (including a negative or NaN `AbstainMarginScale`), **..._ModelMismatch_Throws**,
  **WordSense_Classifier_CreateAsync_RejectsUnverifiedDirectory**,
  **WordSense_Classifier_CreateAsync_ValidatesOptionsBeforeModel** (invalid options are reported before the
  model directory is touched): validation.
- **WordSenseClassifier_ClassifyAsync_Cancelled_Throws**, **..._Concurrent_IsConsistent**,
  **WordSenseClassifier_Dispose_OwnedEncoder_IsDisposed**: lifetime.

## Requirements Coverage

`WordSense-Classifier-Tiles`, `-Senses`, `-Detail`, `-Tags`, `-Abstain`, `-LongText`, `-Validation` and `-Lifetime`.
