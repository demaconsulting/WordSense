# MiniLMWordSenseModel Verification

## Verification Approach

Tests in `MiniLMWordSenseModelTests.cs` use the real int8 model from `model/all-MiniLM-L6-v2-int8/` (skipped when the
Git LFS content is absent). The constructor and argument-validation scenarios that do not need the real model run
without it.

## Test Environment

Tests run under the standard xUnit v3 runner on net8.0, net9.0 and net10.0. The model files are Git LFS content; no
network access is needed.

## Test Scenarios

- **MiniLMWordSenseModel_EncodeAsync_ValidTargets_ReturnsDeterministicVectors**: for a text and target ranges the model
  returns one vector of 384 values per target, identical on repetition; `HiddenSize` is 384, and `ModelId` and
  `ModelSha256` are the pinned values.
- **MiniLMWordSenseModel_EncodeAsync_NoTargetsOrNoTokens_ReturnsEmptyOrNull**: no targets give an empty result, and a
  target with no word pieces gives a null entry.
- **MiniLMWordSenseModel_EncodeAsync_TextLongerThanWindow_ReturnsVectorsForTargets**: a text longer than the
  maximum sequence length is windowed inside the call and still returns a vector for each target.
- **MiniLMWordSenseModel_EncodeAsync_InvalidUse_Throws**: null input, an invalid target range, a cancelled token and
  use after disposal.
- **MiniLMWordSenseModel_Constructor_InvalidArguments_Throws**: a null or empty path, a missing file and an invalid
  thread count.
- **MiniLMWordSenseModel_EncodeAsync_Concurrent_MatchesSequentialResults**: parallel calls on one model give the
  same vectors as sequential calls.
- **MiniLMWordSenseModel_Constructor_InvalidModelFile_Throws**: a file that is not an ONNX model is rejected with an
  exception (this test does not need the real model).
- **WordSense_EmbeddedWeights_ModelAcceptsThemAndOtherLexiconIsRefused**: the model accepts the embedded weights and
  refuses weights of another lexicon (system test of the MiniLM package, shared with the factory).

A model whose output has a dynamic (unknown) last dimension is not covered by a test, because it needs a purpose-built
ONNX file; the behavior is described in the design.

## Acceptance Criteria

All tests listed in the scenarios above pass on every platform and target framework of the CI matrix, and each
requirement listed under Requirements Coverage is satisfied by at least one passing test.

## Requirements Coverage

- `WordSense-Encoder-Encode`: **MiniLMWordSenseModel_EncodeAsync_ValidTargets_ReturnsDeterministicVectors** and
  **MiniLMWordSenseModel_EncodeAsync_NoTargetsOrNoTokens_ReturnsEmptyOrNull**.
- `WordSense-Encoder-Identity`: **MiniLMWordSenseModel_EncodeAsync_ValidTargets_ReturnsDeterministicVectors** and
  **WordSense_EmbeddedWeights_ModelAcceptsThemAndOtherLexiconIsRefused**.
- `WordSense-Encoder-Validation`: **MiniLMWordSenseModel_EncodeAsync_InvalidUse_Throws**,
  **MiniLMWordSenseModel_Constructor_InvalidArguments_Throws** and
  **MiniLMWordSenseModel_Constructor_InvalidModelFile_Throws**.
- `WordSense-Encoder-Concurrency`: **MiniLMWordSenseModel_EncodeAsync_Concurrent_MatchesSequentialResults**.
- `WordSense-Encoder-LongText`: **MiniLMWordSenseModel_EncodeAsync_TextLongerThanWindow_ReturnsVectorsForTargets**.
