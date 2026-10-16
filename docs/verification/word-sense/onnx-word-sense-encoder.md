# OnnxWordSenseEncoder Verification

## Verification Approach

Tests in `OnnxWordSenseEncoderTests.cs` use the real model from the `model/` directory (skipped when
the Git LFS content is absent).

## Test Scenarios

- **OnnxWordSenseEncoder_EncodeAsync_ValidIds_ReturnsHiddenStates**: three ids give `3 × 384` values,
  identical on repetition; the digest is the pinned one.
- **OnnxWordSenseEncoder_EncodeAsync_InvalidUse_Throws**: empty and null input, a cancelled token, use
  after disposal, a missing file and an invalid thread count.
- **OnnxWordSenseEncoder_EncodeAsync_Concurrent_MatchesSequentialResults**: parallel calls on one encoder give
  the same hidden states as sequential calls.
- **OnnxWordSenseEncoder_Constructor_InvalidModelFile_Throws**: a file that is not an ONNX model is
  rejected with an exception (this test does not need the real model).

## Requirements Coverage

`WordSense-Encoder-Encode` and `WordSense-Encoder-Validation`.
