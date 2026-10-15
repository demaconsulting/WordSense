# OnnxWordSenseEncoder Unit Design

## Overview

`OnnxWordSenseEncoder` implements `IWordSenseEncoder` with an ONNX Runtime `InferenceSession` over
the int8 `all-MiniLM-L6-v2` model.

## Data Model

The encoder holds one session, the model file's SHA-256 (computed at construction) and the hidden
size read from the model output (384).

## Methods

- `OnnxWordSenseEncoder(path, intraOpThreads)`: validates the path and thread count, creates the
  session and hashes the file; the session is disposed if reading the output shape fails.
- `EncodeAsync(ids, ct)`: validates the input (non-null, non-empty), builds `input_ids`,
  `attention_mask` and `token_type_ids`, runs the session and returns the flattened
  `last_hidden_state` (`ids.Length × HiddenSize`). The run is synchronous on the calling thread and the
  method returns an already completed `ValueTask`; cancellation is checked once before the run and
  cannot interrupt a run in progress.
- `Dispose()`: idempotent; later calls throw `ObjectDisposedException`.

## Interactions

Called by `WordSenseClassifier` and `WordSenseBuilder` through `IWordSenseEncoder`; depends on ONNX
Runtime (see the OTS design).
