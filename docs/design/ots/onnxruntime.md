## ONNX Runtime

### Purpose

Microsoft.ML.OnnxRuntime 1.28.0 (MIT license) is the inference engine that runs the int8-quantized
`all-MiniLM-L6-v2` model. It is the only runtime (shipped) dependency of the
`DemaConsulting.WordSense.MiniLM` package; the core `DemaConsulting.WordSense` package does not use it. It was chosen because it executes the model file directly,
supports Windows, Linux and macOS, and needs no Python runtime.

### Features Used

- `InferenceSession` created from the verified model file, with a configurable number of
  intra-operator threads
- `Run` with `input_ids`, `attention_mask` and `token_type_ids` tensors, reading the
  `last_hidden_state` output

### Integration Pattern

`MiniLMWordSenseModel` is the only class that references ONNX Runtime. It creates one session per
model and serializes nothing: `InferenceSession.Run` is thread-safe, so concurrent callers share
the session. The core package depends only on the `IWordSenseModel` interface, which allows
tests to substitute a fake model and lexicon-only use to run without ONNX Runtime at all. The session is
disposed with the model.
