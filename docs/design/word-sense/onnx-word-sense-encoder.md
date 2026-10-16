# OnnxWordSenseEncoder Unit Design

## Purpose

`OnnxWordSenseEncoder` implements `IWordSenseEncoder` with an ONNX Runtime `InferenceSession` over
the int8 `all-MiniLM-L6-v2` model.

## Data Model

The encoder holds one session, the model file's SHA-256 (computed at construction) and the hidden
size read from the model output (384).

## Key Methods

- `OnnxWordSenseEncoder(path, intraOpThreads)`: validates the path and thread count, hashes the file first,
  then creates the session; the session is disposed if reading the output shape fails.
- `EncodeAsync(ids, ct)`: validates the input (non-null, non-empty), builds `input_ids`,
  `attention_mask` and `token_type_ids`, runs the session and returns the flattened
  `last_hidden_state` (`ids.Length × HiddenSize`). The run is synchronous on the calling thread and the
  method returns an already completed `ValueTask`; cancellation is checked once before the run and
  cannot interrupt a run in progress.
- `Dispose()`: idempotent; later `EncodeAsync` calls throw `ObjectDisposedException`. It does not wait
  for calls in progress (see Thread Safety).

## Error Handling

Argument and state errors are detected locally and thrown before any native work; native errors are
propagated unchanged.

- Constructor: `ArgumentNullException` for a null path, `ArgumentOutOfRangeException` for a negative
  thread count, `FileNotFoundException` when the file does not exist. An invalid or unreadable model
  makes ONNX Runtime throw (for example `OnnxRuntimeException`); that exception is propagated, and
  the session is disposed if it was already created.
- `EncodeAsync`: `ArgumentNullException` for null ids, `ArgumentException` for empty ids,
  `ObjectDisposedException` after disposal, `OperationCanceledException` when the token is already
  cancelled. Exceptions from ONNX Runtime during the run (for example an id outside the model
  vocabulary) are propagated.
- `Dispose()` never throws on repeated calls.

## Thread Safety

The ONNX Runtime session supports concurrent `Run` calls, so `EncodeAsync` may be called from many
threads at once. Concurrent encoding is verified by
`OnnxWordSenseEncoder_EncodeAsync_Concurrent_MatchesSequentialResults`, which shares one encoder between many
tasks and requires the results to equal the sequential ones. `Dispose()` does not wait for running calls: it must
be called once every call has finished.

## Dependencies

ONNX Runtime (see the OTS design) and `WordSenseModelCatalog` (file hashing).

## Callers

`WordSenseClassifier` and `WordSenseBuilder`, through `IWordSenseEncoder`.
