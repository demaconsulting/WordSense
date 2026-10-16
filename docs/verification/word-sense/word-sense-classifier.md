# WordSenseClassifier Verification

## Verification Approach

Tests in `WordSenseClassifierTests.cs` use a fake `IWordSenseEncoder` that returns deterministic
hidden states and a small in-memory table, so behavior is verified without the model. The exceptions are
`WordSense_Classifier_CreateAsync_RejectsUnverifiedDirectory`,
`WordSense_Classifier_CreateAsync_RejectsWrongContentDirectory` and
`WordSense_Classifier_CreateAsync_ValidatesOptionsBeforeModel`, which are in `WordSenseTests.cs`.

## Test Scenarios

- **WordSenseClassifier_ClassifyAsync_Text_TokensTileInput**: concatenated tokens equal the input, token starts
  are contiguous, each token's analysis word equals its text, and a whitespace token has no details and a zero
  margin (unknown).
- **WordSenseToken_Tiles_FollowSegmentSpans**: the text overload `ClassifyAsync(text)` on `They bat it, don't.`
  returns tokens with contiguous spans whose texts concatenate to the input and whose analysis word equals the
  token text; only `bat` (start 5) carries details, and it is the shared candidate of the lemma; the first token
  has none.
- **WordSenseClassifier_ClassifyAsync_NoKnownWords_DoesNotEncode**: no encoder call without targets.
- **WordSenseClassifier_ClassifyAsync_Sentence_AssignsSenseToKnownWordOnly**,
  **..._Words_ResolvesForms**, **..._Words_EdgeCases**, **..._ManyTargets_EncodesOnce**: lemma resolution, an
  array parallel to the input and a single encoder pass. **..._InfiniteAbstainMarginScale_AlwaysAbstains**: a
  positive-infinity scale is accepted and every word abstains. **..._Words_EdgeCases** also covers unknown words,
  which have no details, are not abstained and have zero margins.
- **WordSenseClassifier_KnownLemmas_And_GetCandidates_AreExposed** (including an unknown lemma): the lemmas and the
  candidate answers of a lemma.
- **WordSenseClassifier_ClassifyAsync_ReportsMarginsAndRunnerUp**, **..._ThreeSenses_ReportsRunnerUpMargin**,
  **..._LowMargin_Abstains**: the `WordAnalysis` fields. For a two-sense lemma `Margin` is twice the activation,
  `RunnerUp` is the other sense and `RunnerUpMargin` is 0; for a three-sense lemma `RunnerUp` is the second-best
  sense and `RunnerUpMargin` is its score minus the third-best score. A word below the scaled threshold has
  `Abstained` set, no details, and its margin and runner-up still reported, which tells it apart from an unknown
  word.
- **WordSenseClassifier_ClassifyAsync_LongText_UsesWindowPerTarget**: windowing. A long text with two known words
  (`the bat` and `a bat`, separated by long filler) and a maximum sequence length of 8 gives a verb and a noun,
  exactly two encoder calls (one per target) and no encoder input longer than 8.
- **WordSenseClassifier_Constructor_InvalidArguments_Throw** (including a negative or NaN
  `AbstainMarginScale`), **..._ModelMismatch_Throws**,
  **WordSense_Classifier_CreateAsync_RejectsUnverifiedDirectory** (missing files),
  **WordSense_Classifier_CreateAsync_RejectsWrongContentDirectory** (files with the wrong digest),
  **WordSense_Classifier_CreateAsync_ValidatesOptionsBeforeModel** (invalid options are reported before the
  model directory is touched): validation.
- **WordSenseClassifier_ClassifyAsync_Cancelled_Throws**, **..._Concurrent_IsConsistent**,
  **WordSenseClassifier_Dispose_OwnedEncoder_IsDisposed**: lifetime.
- **WordSenseClassifier_ClassifyAsync_CancelledDuringEncode_StopsBeforeNextTarget**: when the token is cancelled
  by the encoder during its first call, a text with two known words throws `OperationCanceledException` and the
  encoder has been called once.
- **WordSenseClassifier_Dispose_OwnedEncoder_LaterCallsBehaveAsDocumented**: after an owned classifier is disposed
  (twice, harmlessly) the encoder is disposed; the word-array and text overloads with a known word throw
  `ObjectDisposedException`; both overloads with only unknown words still return answers without details; the
  encoder is never called.

## Requirements Coverage

`WordSense-Classifier-Tiles`, `-Answers`, `-Candidates`, `-Abstain`, `-Analysis`, `-Unknown`, `-LongText`,
`-Validation` and `-Lifetime`.
