### MiniLMWordSenseModel Unit Design

![MiniLM Structure](MiniLMView.svg)

#### Purpose

`MiniLMWordSenseModel` is the public model based on all-MiniLM-L6-v2, quantized to int8: the smaller and faster of
the two encoder models. It is the composition of its encoder model with the Basic model.

#### Data Model

A sealed public class that implements `IDownloadableWordSenseModel` and holds one `FallbackWordSenseModel`, built in
the constructor from two models it creates and
owns: an internal `MiniLMEncoderModel` (the primary model, a sealed class deriving from `OnnxWordSenseModel`) and a
`BasicWordSenseModel` (the fallback model). The two are also kept in the internal properties `Encoder` and `Fallback`
for the tests. The encoder model's description: model id
`all-MiniLM-L6-v2-int8`; files `model_int8.onnx` (about 21.9 MiB; 22 MiB with `vocab.txt`) and `vocab.txt`, each with a
pinned SHA-256;
lower-casing (uncased) tokenizer; hidden size 384; embedded weights `weights.bin` (fine-sense classifiers, form
choosers and per-lemma fine and group thresholds calibrated for this encoder) trained at sequence length 128; embedded
part-of-speech head `head.bin` (the format `WSH1`, bound to the pinned ONNX file; loaded and checked with the weights;
the description declares it not in use, so the model answers as a model without a head: it needs the encoder only for a
word with scored senses, and its slot probability is the mass of its senses). The encoder model loads the head through
`OnnxWordSenseModel` and `PosHead` (see their unit
documents): it must belong to the pinned ONNX file, the hidden size 384, the model id and the parts of speech of the
library, and a head read against another encoder file or model id is rejected as invalid data.

#### Key Methods

The constructor `(string? modelDirectory = null, int maxSequenceLength = 128)` creates the encoder model with the
description (the base class validates the sequence length, 8 to 512) and the Basic model, and wraps both. `ModelId` and
`HiddenSize` come from the encoder model. An internal constructor `(string? modelDirectory, int maxSequenceLength, double
primaryWeight)` does the same with an explicit primary weight (the public constructor passes `PartOfSpeechPrimaryWeight`;
the builder tunes the weight and the tests pin it). `EnsureModelAsync(DownloadOptions? options = null, CancellationToken)`,
`ClassifyAsync` and `Dispose` delegate to the composite:
ensuring is optional (`ClassifyAsync` ensures lazily) and verifies or downloads the encoder files (the Basic model
needs none and is skipped by the composite), classification answers each word with the result of
the encoder model when it scored senses and otherwise with the complete result of the Basic model, and disposal
disposes both models once. When the encoder scored senses, the composite also re-splits the variants of a slot that the
encoder cannot tell apart: the encoder gives the analyses of one lemma and part of speech (the past and the infinitive
of `put`, the past and the present of `read`) the same probability, because they share their senses, so the composite
gives those tied variants the relative proportions that the Basic model gives them from the context (its shared tense
model), with the probability of the slot, the level, the confidence, the group, the sense and the senses of the encoder
result unchanged, and the selected analysis is the best variant of the first slot (see the `FallbackWordSenseModel`
unit document). When the encoder committed to no sense and no group (its result is at the part of speech level), the
composite also combines the slot probabilities of the encoder and of the Basic model with the primary weight 0.6
(`MiniLMWordSenseModel.PartOfSpeechPrimaryWeight`, passed to the `FallbackWordSenseModel`): the probability that the
encoder gives a slot is the mass of its senses and not a part-of-speech estimate, so a wrong top slot is repaired by the
tagger of Basic where the encoder is unsure. The weight was chosen on development data only (the open corpus
development split and the English EWT development file, `evaluate-running --split dev` with the matching gold file),
never on a test split; the accuracy curve is flat between 0.4 and 0.7 and its best sum is at 0.6. An answer at the sense
or group level, the real-text sense share and precision and the share and precision of the SemCor sense evaluation do
not depend on the weight; only the part-of-speech-level answers, their confidence and the fine top-1 of the selected
analysis do. The measured figures are in the model table of the user guide and the reviewed suite is in
`corpus/suite/README.md`. A caller that holds a `MiniLMWordSenseModel` calls
`await model.EnsureModelAsync();`
directly; a caller that holds only an `IWordSenseModel` tests for `IDownloadableWordSenseModel` first.

#### Error Handling

Inherited from the composite and the encoder model: the argument errors of `ClassifyAsync`, `ObjectDisposedException`
after `Dispose`, and `ArgumentOutOfRangeException` for a
sequence length outside 8 to 512 from the constructor. The typed errors of the encoder reach the caller unchanged from
`EnsureModelAsync` and from the lazy ensure and the loading of `ClassifyAsync`: `ModelUnavailableException` (files
missing or not downloadable), `ModelIntegrityException` (a supplied file, the embedded weights or the ONNX file is
damaged) and `DownloadConfigurationException` (invalid download options or `WORDSENSE_MIRROR_*` settings; it is not an
`ArgumentException`); the original exception is the inner exception. A damaged Basic table reports a
`ModelIntegrityException` from `ClassifyAsync`.

#### Dependencies

`FallbackWordSenseModel` (core), `OnnxWordSenseModel` (Onnx), `BasicWordSenseModel` (core) and the core contracts.

#### Callers

Application code and the builder (`ModelCatalog`, `weights minilm`, `evaluate minilm`).
