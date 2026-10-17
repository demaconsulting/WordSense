# MiniLMWordSenseModel Unit Design

## Overview

`MiniLMWordSenseModel` implements `IWordSenseModel` with an ONNX Runtime `InferenceSession` over the int8
`all-MiniLM-L6-v2` model. It owns the tokenizer and the session, windows long text, and pools the word pieces of each
target, so the classifier only sees one vector per target word. It is the model of the MiniLM package, built by the
`MiniLMWordSense` factory or directly from a model file and a vocabulary file.

## Interface

`IWordSenseModel` (see the WordSenseModel unit) is the seam for test substitution and other models: the classifier
depends only on that interface. The model identity is `ModelId` (the catalog id `all-MiniLM-L6-v2-int8`), `ModelSha256`
(the digest of the model file, computed at construction) and `HiddenSize`.

## Data Model

The model holds one session, one `WordPieceTokenizer`, the maximum sequence length, the model file's SHA-256
and the hidden size read from the model output: the last dimension of the first output as reported by ONNX Runtime,
which is 384 for the pinned model. The model does not validate that value. When the model declares that dimension as
dynamic or unknown, ONNX Runtime reports `-1`, so `HiddenSize` would be `-1`, and the classifier would reject
the pairing as a dimension mismatch with the weights (`WordSenseModelMismatchException`).

## Methods

- `MiniLMWordSenseModel(modelPath, vocabularyPath, maxSequenceLength, intraOpThreads)`: validates the paths, the
  sequence length (8 to 512) and the thread count, builds the tokenizer from the vocabulary, hashes the model file,
  then creates the session. With a positive thread count the session uses that many intra-op threads and one inter-op
  thread. The session is disposed if reading the output shape fails.
- `EncodeAsync(words, targets, ct)`: validates the arguments and the target indexes, tokenizes the words, and runs the
  session. When the whole text (with its two special tokens) fits the sequence length, the session runs once for the
  text; otherwise it runs once per target on a window centered on the target. The inputs are `input_ids`,
  `attention_mask` and `token_type_ids` and the output is the first output (`last_hidden_state`). For each target the
  hidden states of its word pieces are averaged (in double precision) without centering or scaling and returned as one
  vector; a target with no word pieces gets null. The run is synchronous on the calling thread and the method returns
  an already completed `ValueTask`; cancellation is checked before each target and cannot interrupt a run in progress.
- `Dispose()`: idempotent; later `EncodeAsync` calls throw `ObjectDisposedException`. It does not wait
  for calls in progress (see Thread Safety).

## Error Handling

Argument and state errors are detected locally and thrown before any native work; native errors are
propagated unchanged.

- Constructor: `ArgumentNullException` for a null path, `ArgumentOutOfRangeException` for a sequence length outside
  8 to 512 or a negative thread count, `FileNotFoundException` when the model file does not exist,
  `ArgumentException` when the vocabulary lacks `[UNK]`, `[CLS]` or `[SEP]`. An invalid or unreadable model
  makes ONNX Runtime throw (for example `OnnxRuntimeException`); that exception is propagated, and
  the session is disposed if it was already created.
- `EncodeAsync`: `ArgumentNullException` for a null word list or target list, `ArgumentOutOfRangeException` for a
  target that is not an index of the word list, `ObjectDisposedException` after disposal,
  `OperationCanceledException` when the token is cancelled. Exceptions from ONNX Runtime during the run are
  propagated.
- `Dispose()` never throws on repeated calls.

## Thread Safety

The ONNX Runtime session supports concurrent `Run` calls, and the tokenizer is immutable, so `EncodeAsync` may be
called from many threads at once (requirement `WordSense-Encoder-Concurrency`). Concurrent encoding is verified by
`MiniLMWordSenseModel_EncodeAsync_Concurrent_MatchesSequentialResults`, which shares one model between many
tasks and requires the results to equal the sequential ones. `Dispose()` does not wait for running calls: it must
be called once every call has finished.

## Interactions

Uses ONNX Runtime (see the OTS design), `WordPieceTokenizer` and `WordSenseModelCatalog` (file hashing). It is called
by `WordSenseClassifier` through `IWordSenseModel`, created by `MiniLMWordSense`, and opened by `WordSenseBuilder`
(through the internals-visible declaration) to embed the corpus when training weights.
