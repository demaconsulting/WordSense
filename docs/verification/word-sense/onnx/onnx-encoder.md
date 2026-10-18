### OnnxEncoder Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Onnx.Tests/OnnxEncoderTests.cs` run the real MiniLM int8 model and vocabulary
through `ModelAssets` (skipped when the Git LFS content is absent), because the encoder's contract is the shape and
determinism of the vectors of a real ONNX session. Constructor validation scenarios that need no real model run without
it. ONNX Runtime is limited to two intra-op threads in the tests.

#### Test Environment

Standard xUnit v3 runner. `model/all-MiniLM-L6-v2-int8/` (Git LFS) for the session scenarios. No network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Deterministic vectors**: encoding the same words and targets twice gives identical vectors, one per target, each of
  the model's hidden size (384 for MiniLM).
- **No targets or no tokens**: no targets give an empty result and a target without word pieces gives a null entry.
- **Long text is windowed**: a text longer than the maximum sequence length still returns a vector for each target.
- **Concurrent calls**: parallel calls on one encoder give the same vectors as sequential calls.
- **Invalid use**: null words or targets, a target index outside the word list, a cancelled token and use after disposal
  each throw.
- **Constructor validation**: a missing model file throws `FileNotFoundException`; a file that is not an ONNX model is
  rejected with an exception; a sequence length outside 8 to 512 or a negative thread count is rejected.
