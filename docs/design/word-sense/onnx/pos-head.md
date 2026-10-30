### PosHead Unit Design

![Onnx Structure](OnnxView.svg)

#### Purpose

`PosHead` supplies the trained part-of-speech head of an encoder model: it reads the embedded head blob, binds the head
to its encoder and to the parts of speech of the library, computes the probability of every part of speech of a word
from the pooled encoder vectors of the word and of its two neighbors, and restricts those probabilities to the parts of
speech that a spelling can have.

#### Data Model

- `PosHeadData(ModelId, HiddenSize, EncoderSha256, FeatureLayout, ClassNames, TrainSha256, C, Variant, Weights,
  Biases)`: the decoded blob. `EncoderSha256` is the 32-byte SHA-256 of the pinned ONNX file of the encoder the head was
  fitted on. `FeatureLayout` is 1 (`PosHeadBlob.WordAndNeighbors`): the vector of the word, of the previous word and of
  the next word, with zeros beyond the edge of the sentence. `ClassNames` are the class names in output order.
  `TrainSha256` is the SHA-256 of the training file, `C` is the regularization strength that was selected and `Variant`
  names the feature variant (`word+-1`). `Weights` has one row per class of `3 × HiddenSize` values in the order
  previous word, word, next word, widened to 32-bit floats; `Biases` has one value per class.
- Blob format `WSH1` (little-endian, not compressed, because half-precision weights do not compress): the magic `WSH1`,
  the model id (a length-prefixed UTF-8 string), the hidden size (a varint), the 32-byte encoder digest, the feature
  layout (a varint), the class count and the class names, the 32-byte training-file digest, `C` (float32), the variant
  name, the section count and the sections, and last the 32-byte SHA-256 of all bytes before it. A section is its id
  (varint), a required flag (byte: 1 means a reader that does not know the section rejects the blob, 0 means it skips
  it), its payload length (varint) and its payload. Section 1 is the part-of-speech head and is required: the `k × 3d`
  weights as half-precision numbers, row-major, followed by the `k` biases as float32. Section 2 is reserved for a later
  tense head. The head has no lemma, no sense id and no inventory hash, so it does not depend on the word image.
- `PosHead`: an immutable object over a `PosHeadData`. `HiddenSize`, `ClassCount` and `Data` are its properties. It is
  safe for concurrent use.
- The classes are the names of `PartOfSpeech` in declaration order (`Noun`, `Pronoun`, `Verb`, `Adjective`, `Adverb`,
  `Preposition`, `Conjunction`, `Interjection`, `Determiner`, `Auxiliary`), so the index of a class is
  `(int)PartOfSpeech`.

#### Key Methods

- `PosHeadBlob.Read`: decodes and validates the blob. It checks the magic and the trailing hash first, then the
  structure: the hidden size (1 to 16,384) and the regularization strength, the class count (at most 1,024, so no size
  computed from the two can overflow), the section list (a duplicate section, a flag other than 0
  or 1, a required section that it does not know and a missing part-of-speech section are errors), the length of the
  part-of-speech section (`2·k·3d + 4·k` bytes, with at least two classes), finite weights and biases and the end of the
  data. A varint is read in at most five bytes; the fifth byte may only set bits 28 to 30 and must not continue, so a
  number is never wrapped. A section that is not known and is optional is skipped. It does not check the binding.
- `PosHead.LoadEmbedded(assembly, resource, modelId, hiddenSize, encoderSha256)`: reads the resource into an array of
  exactly its length (`WeightedLemmaTable.ReadResource`), decodes it and binds the head: the model id, the hidden size,
  the encoder digest (compared with the pinned digest of the ONNX file, case-insensitive hexadecimal), the feature
  layout and the class names, which must equal `Enum.GetNames<PartOfSpeech>()` in order. Any difference is a load
  failure, so a changed encoder file or a changed enum fails loudly.
- `Posterior(previous, word, next)`: the logit of class `c` is the bias plus the sum, in the fixed order previous word,
  word, next word and within a block from the first to the last feature, of the weight times the vector value. The
  vectors are the raw pooled vectors of the encoder (the standardization of training is folded into the weights). A
  vector that is null (a neighbor beyond the sentence edge, or a word without word pieces) adds nothing, which is the
  same as zeros. Each vector value is first rounded to a 32-bit float (once per call), the sum runs in 64-bit floats,
  and the logits go through a softmax that subtracts the maximum. The code is plain scalar code, without vector
  instructions or parallelism; it computes several classes in one loop, but every logit is still one sum in the fixed
  order, so the result does not depend on the hardware or on the number of threads. A vector whose length is not
  the hidden size throws `ArgumentException`.
- `Restrict(posterior, allowedMask)`: keeps the probabilities of the allowed parts of speech (bit `(int)PartOfSpeech` of
  the mask), sets the others to zero and divides by the sum of the kept ones. When the mask allows nothing, or the
  kept probabilities sum to zero, it returns a copy of the unchanged posterior. It does not read the lexicon: the caller
  builds the mask.

#### Error Handling

`InvalidDataException` for a missing resource, an unknown magic, a trailing hash that does not match (the head is
corrupt), a truncated or malformed structure, a number that does not fit a non-negative 32-bit integer, a hidden size or
class count above its bound, a section that is duplicated, has the wrong length or is required and
unknown, a missing part-of-speech section, a non-finite value, trailing data, and every binding mismatch of
`LoadEmbedded`. `Posterior` throws `ArgumentException` for a vector of the wrong length and `Restrict` throws
`ArgumentNullException` for a null posterior. These are the raw exceptions of the unit; `OnnxWordSenseModel` reports an
`InvalidDataException` of the embedded head as a `ModelIntegrityException` (original as inner exception).

#### Dependencies

`PartOfSpeech` (the classes) and `WeightedLemmaTable.ReadResource` (the exact-size read of an embedded resource).

#### Callers

`OnnxWordSenseModel` (loads and checks the head with the weights and hands it out through `GetHead`), `OnnxClassifier`
(asks for the posterior of a word of several parts of speech and restricts it to the parts of speech of the lexicon,
for a model that uses its head) and the builder
(`train-pos-head` writes and measures the head, and its blob writer produces the format this unit reads).
