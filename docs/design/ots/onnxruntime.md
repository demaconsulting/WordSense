## ONNX Runtime

### Purpose

Microsoft.ML.OnnxRuntime (MIT license) is the inference engine that runs the int8-quantized encoder models
(`all-MiniLM-L6-v2` and `bert-base-cased`). It is the only third-party runtime (shipped) dependency of the system
and belongs to the `DemaConsulting.WordSense.Onnx` package; the MiniLM and BertBaseCased packages receive it through
the Onnx package, while the core `DemaConsulting.WordSense` package and the Basic package have no third-party
runtime dependency. It was chosen because it executes the model files directly, supports Windows, Linux and macOS,
and needs no Python runtime.

### Features Used

- `InferenceSession` created from the verified model file, with a configurable number of
  intra-operator threads
- `Run` with `input_ids`, `attention_mask` and `token_type_ids` tensors, reading the
  `last_hidden_state` output

### Integration Pattern

`OnnxEncoder` is the only class that references ONNX Runtime. `OnnxWordSenseModel` creates one encoder, and so one
session, per model and serializes nothing: `InferenceSession.Run` is thread-safe, so concurrent callers share
the session. The core package depends only on the `IWordSenseModel` interface, which allows
tests to substitute a fake model and lexicon-only or Basic-model use to run without ONNX Runtime at all. The
session is disposed with the model.
