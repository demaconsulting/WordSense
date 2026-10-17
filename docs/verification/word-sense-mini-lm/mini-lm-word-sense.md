# MiniLMWordSense Verification

## Verification Approach

The tests are the factory scenarios of `WordSenseTests.cs` in the MiniLM test project. They use the real int8 model
from `model/all-MiniLM-L6-v2-int8/` and the embedded lexicon and weights (skipped when the Git LFS content is
absent), except the option-validation scenario, which needs no model.

## Test Environment

Tests run under the standard xUnit v3 runner on net8.0, net9.0 and net10.0; the model files are Git LFS content and
no network access is needed.

## Test Scenarios

- **WordSense_Classifier_EndToEnd_ClassifiesRecord**, **WordSense_Classifier_EndToEnd_WordListApi**:
  `MiniLMWordSense.CreateClassifierAsync` over the committed model directory returns a working classifier that tells
  the noun and the verb `record` apart, through the text and the word-list overloads.
- **WordSense_Classifier_CreateAsync_RejectsUnverifiedDirectory**: a model directory with missing files is rejected.
- **WordSense_Classifier_CreateAsync_RejectsWrongContentDirectory**: a directory whose files have the wrong digest is
  rejected.
- **WordSense_Classifier_CreateAsync_ValidatesOptionsBeforeModel**: invalid options are reported before the model
  directory is touched.
- **WordSense_EmbeddedWeights_MatchPinnedModelAndLexicon**: the weights embedded for the model id and ONNX digest
  load, and carry the pinned model digest and the digest of the embedded lexicon.
- **WordSense_EmbeddedWeights_ModelAcceptsThemAndOtherLexiconIsRefused**: the model accepts those weights, and a
  weights table naming another lexicon is refused with `WordSenseModelMismatchException`.

## Acceptance Criteria

All tests listed in the scenarios above pass on every platform and target framework of the CI matrix, and each
requirement listed under Requirements Coverage is satisfied by at least one passing test.

## Requirements Coverage

- `WordSenseMiniLM-Factory-Create`: **WordSense_Classifier_EndToEnd_ClassifiesRecord** and
  **WordSense_Classifier_EndToEnd_WordListApi**.
- `WordSenseMiniLM-Factory-Directory`: **WordSense_Classifier_CreateAsync_RejectsUnverifiedDirectory** and
  **WordSense_Classifier_CreateAsync_RejectsWrongContentDirectory**.
- `WordSenseMiniLM-Factory-Validation`: **WordSense_Classifier_CreateAsync_ValidatesOptionsBeforeModel**.
- `WordSenseMiniLM-Factory-Weights`: **WordSense_EmbeddedWeights_MatchPinnedModelAndLexicon** and
  **WordSense_EmbeddedWeights_ModelAcceptsThemAndOtherLexiconIsRefused**.
