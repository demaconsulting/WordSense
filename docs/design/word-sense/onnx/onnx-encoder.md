### OnnxEncoder Unit Design

![Onnx Structure](OnnxView.svg)

#### Purpose

`OnnxEncoder` runs the ONNX encoder over a text and returns one pooled vector per requested word: the sense vector,
and for the heads the last-layer vector. `PossessiveView` gives the form of the text that the part-of-speech head is
trained on.

#### Data Model

An internal sealed class that owns an ONNX Runtime session, a `WordPieceTokenizer`, the hidden size (the last
dimension of the last-layer output) and the maximum sequence length. The model inputs are `input_ids`,
`attention_mask` and `token_type_ids`, all 64-bit integers. The session is safe for concurrent use. `PassCount` is the
number of encoder passes made so far.

The outputs are read by name. The last layer is the output `last_hidden_state` (a model that does not name it has one
output, which is taken). With *sense layers* (`ModelSpec.SenseLayers`, for example 9, 10, 11, 12) the other layers `n`
are the tap outputs `hidden_n` and the last of the sense layers is the last layer. `FeatureLayout` (in
`FeatureLayout.cs`) validates the layers (at least two, at least 1, increasing), names their outputs and gives the
layout text that the weights record (`last` or `mean:9-12`). Without sense layers (every model today) the encoder
reads the one last-layer output and behaves as it always did.

- The *sense vector* of a word is the mean over its word pieces of the last layer; with sense layers it is the mean,
  over the layers in increasing order, of the per-layer means (each in double). `EncodeAsync` and `EncodeAllAsync(words)`
  return it, so the weights and the embedding cache are about it.
- The *head vector* is the mean over the word pieces of the last layer alone. `EncodeHeadAllAsync` returns it, and
  without sense layers it equals the sense vector.

`WordVectors(Tokens, Start, Vectors, SenseVectors)` is the result of the possessive view: the tokens of the view, the
index of the first token of every word of the text, the head vector of every token (null for a token without word
pieces) and, with sense layers, the sense vector of every token (null without sense layers, when `Vectors` serve).
`PossessiveView` (in `PossessiveView.cs`) is a static helper of this unit.

#### Key Methods

- Constructor `(modelPath, vocabularyPath, lowerCase, maxSequenceLength, intraOpThreads, senseLayers)`: opens the
  session. When the thread count is above 0 it sets the intra-op threads to that count and the inter-op threads to 1.
  With sense layers it checks that the graph has the output of every layer with the hidden size.
- `EncodeAsync(words, targets, cancellationToken)`: tokenizes the words, builds the model input and returns, per
  target, the sense vector of the target word, as doubles, without centering or scaling; null when the word has no
  word pieces. A text that fits the sequence length is encoded once for all
  targets; a longer text is encoded per target from a window centered on that target.
- `EncodeAllAsync(words, cancellationToken)`: the sense vector of every word of the text. A text whose
  word pieces fit one window (the maximum length less the two special tokens) is encoded in one pass and gives exactly
  the vectors of `EncodeAsync`. A longer text is cut into windows of that size that start half a window apart; the last
  window ends at the end of the text, so a text of `n` word pieces needs about `n / (window / 2)` passes and not one
  pass per word. Each word is assigned to one window: the one that gives it the most context on its closer side, that
  is the larger of the smaller of the pieces before the word and the pieces after it inside the window, a text edge
  counting as unlimited context, and the earliest window on a tie. Each window that has words is encoded once and its
  words are pooled from it. A word that fits a window is never cut: when it straddles the border of two windows of the
  regular grid, a window that starts at the word is added to the plan. A word of more word pieces than a window holds
  gets a window that starts at the word and is pooled from its first pieces, as many as the window holds. A
  word without word pieces gets null and an empty text gives an empty result.
- `EncodeHeadAllAsync(words, cancellationToken)`: the same windows and the same pooling for the head vector of every
  word; with sense layers it asks the session for the last-layer output only.
- `EncodeAllAsync(words, isPossessive, cancellationToken)`: expands the text with `PossessiveView.Expand`, encodes the
  tokens of the view in the same passes for both vectors and returns them as `WordVectors`.
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
`ArgumentOutOfRangeException`; sense layers that are not at least two increasing layers throw `ArgumentException`; a
graph without the output of a sense layer (or of another hidden size) throws `InvalidDataException` naming the output;
calls after `Dispose` throw `ObjectDisposedException`. `EncodeAllAsync` and
`PossessiveView` throw `ArgumentNullException` for a null list, predicate or word, and `EncodeAllAsync` throws
`OperationCanceledException` for a canceled token. Runtime failures of ONNX Runtime
propagate. These are the raw exceptions of the unit: `OnnxWordSenseModel` reports a missing file after verification as
a `ModelUnavailableException` and a structural failure of the constructor (`OnnxRuntimeException`,
`InvalidDataException`, or an `ArgumentException`, `InvalidOperationException`, `IndexOutOfRangeException` or
`FormatException` from the vocabulary or the model metadata) as a `ModelIntegrityException`.

#### Dependencies

ONNX Runtime, `WordPieceTokenizer` and, for the possessive view, `WordLexicon`.

#### Callers

`OnnxWordSenseModel` (creates and disposes it, and gives it the sense layers of the model description) and
`OnnxClassifier` (encodes through it: `EncodeAsync` for the target
words, and, for a model that uses its head, `EncodeAllAsync` with the possessive view: the head vectors for the head and
the sense vectors for the senses), and the builder's trainer
through `OnnxWordSenseModel.OpenEncoder` (`weights` encodes target words with `EncodeAsync`; `train-pos-head` and
`train-tense-head` encode whole sentences with `EncodeHeadAllAsync`).
