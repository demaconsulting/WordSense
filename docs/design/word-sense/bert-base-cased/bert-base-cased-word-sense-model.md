### BertBaseCasedWordSenseModel Unit Design

![BertBaseCased Structure](BertBaseCasedView.svg)

#### Purpose

`BertBaseCasedWordSenseModel` is the public model based on bert-base-cased, quantized to int8: the larger model,
which keeps capitalization as context. It is the composition of its encoder model with the Basic model.

#### Data Model

A sealed public class that implements `IDownloadableWordSenseModel` and holds one `FallbackWordSenseModel`, built in
the constructor from two models it creates and
owns: an internal `BertBaseCasedEncoderModel` (the primary model, a sealed class deriving from `OnnxWordSenseModel`) and
a `BasicWordSenseModel` (the fallback model). The two are also kept in the internal properties `Encoder` and `Fallback`
for the tests. The encoder model's description: model id
`bert-base-cased-int8`; files `model_int8.onnx` (about 103.5 MiB; 104 MiB for all four files), `vocab.txt`,
`config.json` and `tokenizer.json`, each
with a pinned SHA-256; case-preserving tokenizer; hidden size 768; embedded weights `weights.bin` (fine-sense
classifiers, form choosers and per-lemma fine and group thresholds calibrated for this encoder) trained at sequence
length 128; embedded part-of-speech head `head.bin` (the format `WSH1`, bound to the pinned ONNX file; loaded and
checked with the weights, and used: the description declares the head in use). The encoder model loads the head
through `OnnxWordSenseModel` and
`PosHead` (see their unit documents): it must belong to the pinned ONNX file, the hidden size 768, the model id and the
parts of speech of the library, and a head read against another encoder file or model id is rejected as invalid data.
The `model_int8.onnx` file is the derived encoder export, not the upstream masked language model export.

#### Key Methods

The constructor `(string? modelDirectory = null, int maxSequenceLength = 128)` creates the encoder model with the
description (the base class validates the sequence length, 8 to 512) and the Basic model, and wraps both. `ModelId` and
`HiddenSize` come from the encoder model. An internal constructor `(string? modelDirectory, int maxSequenceLength, double
primaryWeight)` does the same with an explicit primary weight (the public constructor passes `PartOfSpeechHeadWeight`,
which is 0.8; the tests pin other values). `EnsureModelAsync(DownloadOptions? options = null, CancellationToken)`,
`ClassifyAsync` and `Dispose` delegate to the composite:
ensuring is optional (`ClassifyAsync` ensures lazily, on the first text that has a word of several parts of speech or a
word with scored senses; a text of words that need neither never touches a model file) and verifies or downloads the
encoder files (the Basic model needs none and is skipped by the composite), classification answers each word with the
result of
the encoder model when it scored senses and otherwise with the complete result of the Basic model, and disposal
disposes both models once. When the encoder scored senses, the composite also re-splits the variants of a slot that the
encoder cannot tell apart: the encoder gives the analyses of one lemma and part of speech (the past and the infinitive
of `put`, the past and the present of `read`) the same probability, because they share their senses, so the composite
gives those tied variants the relative proportions that the Basic model gives them from the context (its shared tense
model), with the probability of the slot, the level, the confidence, the group, the sense and the senses of the encoder
result unchanged, and the selected analysis is the best variant of the first slot (see the `FallbackWordSenseModel`
unit document).

The encoder model of this package declares its head in use, so it implements the part-of-speech evidence marker and the
composite reweighs the slot probabilities of every word of two or more parts of speech: the head's probabilities (the
posterior restricted to the parts of speech of the lexicon, times the share of the slot among the slots of its part of
speech, closed classes included) against those of the Basic model, as the geometric blend with the weight 0.8
(`BertBaseCasedWordSenseModel.PartOfSpeechHeadWeight`, chosen on the development splits of the open corpus and never on
a test split; 1 takes the head alone, 0 the Basic model alone). The sense, the group and the ranked senses are those of
the encoder; an answer keeps its level when its slot stays the most probable one and otherwise steps down to the part of
speech, as the `FallbackWordSenseModel` unit document states. The tense of a verb stays the one of the Basic model. The
confidence and the slot probabilities of an ambiguous word are those of this reweighing. A caller that
holds a `BertBaseCasedWordSenseModel` calls `await model.EnsureModelAsync();`
directly; a caller that holds only an `IWordSenseModel` tests for `IDownloadableWordSenseModel` first.

#### Error Handling

Inherited from the composite and the encoder model: the argument errors of `ClassifyAsync`, `ObjectDisposedException`
after `Dispose`, and `ArgumentOutOfRangeException` for a
sequence length outside 8 to 512 from the constructor. The typed errors of the encoder reach the caller unchanged from
`EnsureModelAsync` and from the lazy ensure and the loading of `ClassifyAsync`: `ModelUnavailableException` (files
missing or not downloadable), `ModelIntegrityException` (a supplied file, the embedded weights, the embedded head or the
ONNX file is damaged) and `DownloadConfigurationException` (invalid download options or `WORDSENSE_MIRROR_*`
settings; it is not an
`ArgumentException`); the original exception is the inner exception. A damaged Basic table reports a
`ModelIntegrityException` from `ClassifyAsync`.

#### Dependencies

`FallbackWordSenseModel` (core), `OnnxWordSenseModel` (Onnx), `BasicWordSenseModel` (core) and the core contracts.

#### Callers

Application code and the builder (`ModelCatalog`, `weights bert-base-cased`, `evaluate bert-base-cased`).
