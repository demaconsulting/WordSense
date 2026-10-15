# WordSenseClassifier Verification

## Verification Approach

Tests in `WordSenseClassifierTests.cs` use a fake `IWordSenseEncoder` that returns deterministic
hidden states and a small in-memory table, so behavior is verified without the model.

## Test Scenarios

- **WordSenseClassifier_ClassifyAsync_Text_TokensTileInput**: concatenated tokens equal the input.
- **WordSenseClassifier_ClassifyAsync_NoKnownWords_DoesNotEncode**: no encoder call without targets.
- **WordSenseClassifier_ClassifyAsync_Sentence_AssignsSenseToKnownWordOnly**,
  **..._Span_ResolvesForms**, **..._Span_EdgeCases**, **..._ManyTargets_EncodesOnce**,
  **WordSenseClassifier_KnownLemmas_And_SenseLabels_AreExposed**: lemma resolution and single pass.
- **WordSenseClassifier_ClassifyAsync_LowMargin_Abstains**: abstention.
- **WordSenseClassifier_ClassifyAsync_LongText_UsesWindowPerTarget**: windowing.
- **WordSenseClassifier_Constructor_InvalidArguments_Throw**, **..._ModelMismatch_Throws**,
  **WordSense_Classifier_CreateAsync_RejectsUnverifiedDirectory**: validation.
- **WordSenseClassifier_ClassifyAsync_Cancelled_Throws**, **..._Concurrent_IsConsistent**,
  **WordSenseClassifier_Dispose_OwnedEncoder_IsDisposed**: lifetime.

## Requirements Coverage

`WordSense-Classifier-Tiles`, `-Senses`, `-Abstain`, `-LongText`, `-Validation` and `-Lifetime`.
