### OnnxEncoder Unit Design

![Onnx Structure](OnnxView.svg)

#### Purpose

`OnnxEncoder` runs the ONNX encoder over a text and returns one pooled vector per requested word.

#### Data Model

An internal sealed class that owns an ONNX Runtime session, a `WordPieceTokenizer`, the hidden size (the last
dimension of the first model output) and the maximum sequence length. The model inputs are `input_ids`,
`attention_mask` and `token_type_ids`, all 64-bit integers. The session is safe for concurrent use.

#### Key Methods

- Constructor `(modelPath, vocabularyPath, lowerCase, maxSequenceLength, intraOpThreads)`: opens the session. When
  the thread count is above 0 it sets the intra-op threads to that count and the inter-op threads to 1.
- `EncodeAsync(words, targets, cancellationToken)`: tokenizes the words, builds the model input and returns, per
  target, the mean over the word pieces of the target word of the hidden states, as doubles, without centering or
  scaling; null when the word has no word pieces. A text that fits the sequence length is encoded once for all
  targets; a longer text is encoded per target from a window centered on that target.

#### Error Handling

A missing model file throws `FileNotFoundException`; an out-of-range sequence length or thread count throws
`ArgumentOutOfRangeException`; calls after `Dispose` throw `ObjectDisposedException`. Runtime failures of ONNX Runtime
propagate. These are the raw exceptions of the unit: `OnnxWordSenseModel` reports a missing file after verification as
a `ModelUnavailableException` and an `OnnxRuntimeException` or `InvalidDataException` of the constructor as a
`ModelIntegrityException`.

#### Dependencies

ONNX Runtime and `WordPieceTokenizer`.

#### Callers

`OnnxWordSenseModel` (creates and disposes it) and `OnnxClassifier` (encodes through it), and the builder's trainer
through `OnnxWordSenseModel.OpenEncoder`.
