## ONNX Runtime

### Purpose

Microsoft.ML.OnnxRuntime 1.28.0 (MIT license) is the inference engine that runs the int8-quantized
`all-MiniLM-L6-v2` model. It is the only runtime (shipped) dependency of the
`DemaConsulting.WordSense` package. It was chosen because it executes the model file directly,
supports Windows, Linux and macOS, and needs no Python runtime.

### Features Used

- `InferenceSession` created from the verified model file, with a configurable number of
  intra-operator threads
- `Run` with `input_ids`, `attention_mask` and `token_type_ids` tensors, reading the
  `last_hidden_state` output

### Integration Pattern

`OnnxWordSenseEncoder` is the only class that references ONNX Runtime. It creates one session per
encoder and serializes nothing: `InferenceSession.Run` is thread-safe, so concurrent callers share
the session. The rest of the library depends only on the `IWordSenseEncoder` interface, which allows
tests to substitute a fake encoder. The session is disposed with the encoder.
