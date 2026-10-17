# WordSenseModel Verification

## Verification Approach

The `IWordSenseModel` abstraction is an interface with a mismatch check, so it is verified through its consumers: the
classifier tests in `WordSenseClassifierTests.cs` use a fake `IWordSenseModel`, and the real implementation is
verified in the MiniLM test project.

## Test Environment

Tests run under the standard xUnit v3 runner on net8.0, net9.0 and net10.0. The fake model needs no files; the
real-model scenario needs the Git LFS model files and is skipped when they are absent.

## Test Scenarios

- **WordSenseClassifier_ClassifyAsync_Mixed_EncodesOnce**: a classifier delivers all targets of a text to the model in
  one `EncodeAsync` call and uses the returned vectors.
- **WordSenseClassifier_ClassifyAsync_WeightedWordWithoutEvidence_IsUnknown**: a target for which the model returns no
  vector is unknown.
- **MiniLMWordSenseModel_EncodeAsync_ValidTargets_ReturnsDeterministicVectors**: the real model returns one
  deterministic vector of `HiddenSize` values per target.
- **WordSenseClassifier_Constructor_Mismatch_Throws**: a model and weights that disagree on id, digest, dimension or
  lexicon are refused with `WordSenseModelMismatchException`.
- **WordSense_EmbeddedWeights_ModelAcceptsThemAndOtherLexiconIsRefused**: `EnsureWeightsMatch` accepts the embedded
  weights and refuses another lexicon.

## Acceptance Criteria

All tests listed in the scenarios above pass on every platform and target framework of the CI matrix, and each
requirement listed under Requirements Coverage is satisfied by at least one passing test.

## Requirements Coverage

- `WordSense-Model-Contract`: **WordSenseClassifier_ClassifyAsync_Mixed_EncodesOnce**,
  **WordSenseClassifier_ClassifyAsync_WeightedWordWithoutEvidence_IsUnknown** and
  **MiniLMWordSenseModel_EncodeAsync_ValidTargets_ReturnsDeterministicVectors**.
- `WordSense-Model-Match`: **WordSenseClassifier_Constructor_Mismatch_Throws** and
  **WordSense_EmbeddedWeights_ModelAcceptsThemAndOtherLexiconIsRefused**.
