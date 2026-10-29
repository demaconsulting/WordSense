### OnnxEncoder Unit Design

![Onnx Structure](OnnxView.svg)

#### Purpose

`OnnxEncoder` runs the ONNX encoder over a text and returns one pooled vector per requested word. `PossessiveView` gives
the form of the text that the part-of-speech head is trained on.

#### Data Model

An internal sealed class that owns an ONNX Runtime session, a `WordPieceTokenizer`, the hidden size (the last
dimension of the first model output) and the maximum sequence length. The model inputs are `input_ids`,
`attention_mask` and `token_type_ids`, all 64-bit integers. The session is safe for concurrent use. `PassCount` is the
number of encoder passes made so far.

`WordVectors(Tokens, Start, Vectors)` is the result of the possessive view: the tokens of the view, the index of the
first token of every word of the text, and the pooled vector of every token (null for a token without word pieces).
`PossessiveView` (in `PossessiveView.cs`) is a static helper of this unit.

#### Key Methods

- Constructor `(modelPath, vocabularyPath, lowerCase, maxSequenceLength, intraOpThreads)`: opens the session. When
  the thread count is above 0 it sets the intra-op threads to that count and the inter-op threads to 1.
- `EncodeAsync(words, targets, cancellationToken)`: tokenizes the words, builds the model input and returns, per
  target, the mean over the word pieces of the target word of the hidden states, as doubles, without centering or
  scaling; null when the word has no word pieces. A text that fits the sequence length is encoded once for all
  targets; a longer text is encoded per target from a window centered on that target.
- `EncodeAllAsync(words, cancellationToken)`: the vector of every word of the text, pooled the same way. A text whose
  word pieces fit one window (the maximum length less the two special tokens) is encoded in one pass and gives exactly
  the vectors of `EncodeAsync`. A longer text is cut into windows of that size that start half a window apart; the last
  window ends at the end of the text, so a text of `n` word pieces needs about `n / (window / 2)` passes and not one
  pass per word. Each word is assigned to one window: the one that gives it the most context on its closer side, that
  is the larger of the smaller of the pieces before the word and the pieces after it inside the window, a text edge
  counting as unlimited context, and the earliest window on a tie. Each window that has words is encoded once and its
  words are pooled from it. A word that is longer than a window is pooled from the part of it that the window keeps. A
  word without word pieces gets null and an empty text gives an empty result.
- `EncodeAllAsync(words, isPossessive, cancellationToken)`: expands the text with `PossessiveView.Expand`, encodes the
  tokens of the view with `EncodeAllAsync` and returns them as `WordVectors`.
- `PossessiveView.Expand(words, isPossessive)`: for each word the predicate calls a possessive, whose written form ends
  in `'s` or in a final apostrophe (the typographic apostrophe counts) after a stem, the view has the stem and the marker
  as two tokens; every other word, and a word that is only a marker, stays one token. It returns the tokens and the index
  of the first token of every word. The WordPiece pieces of the split text equal those of the joined word, so the
  encoder sees the same text; what changes is the pooling: the stem has a vector of the stem pieces alone, and the marker
  has a vector of its own, which is the previous vector of the next word.
- `PossessiveView.IsLexiconPossessive(word)`: true when the normalized word ends in a possessive marker after a stem, is
  in the lexicon and every analysis of it has the feature `Poss=Yes`. A contraction (`it's`), an ordinary word, a bare
  marker and an unknown stem are not possessives.

#### Error Handling

A missing model file throws `FileNotFoundException`; an out-of-range sequence length or thread count throws
`ArgumentOutOfRangeException`; calls after `Dispose` throw `ObjectDisposedException`. `EncodeAllAsync` and
`PossessiveView` throw `ArgumentNullException` for a null list, predicate or word, and `EncodeAllAsync` throws
`OperationCanceledException` for a canceled token. Runtime failures of ONNX Runtime
propagate. These are the raw exceptions of the unit: `OnnxWordSenseModel` reports a missing file after verification as
a `ModelUnavailableException` and a structural failure of the constructor (`OnnxRuntimeException`,
`InvalidDataException`, or an `ArgumentException`, `InvalidOperationException`, `IndexOutOfRangeException` or
`FormatException` from the vocabulary or the model metadata) as a `ModelIntegrityException`.

#### Dependencies

ONNX Runtime, `WordPieceTokenizer` and, for the possessive view, `WordLexicon`.

#### Callers

`OnnxWordSenseModel` (creates and disposes it) and `OnnxClassifier` (encodes through it), and the builder's trainer
through `OnnxWordSenseModel.OpenEncoder` (`train-pos-head` encodes whole sentences with `EncodeAllAsync`).
