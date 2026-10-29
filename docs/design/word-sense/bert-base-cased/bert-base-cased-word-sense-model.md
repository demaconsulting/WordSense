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
checked with the weights and not used for answers). The `model_int8.onnx` file is the derived encoder export, not the
upstream masked language model export.

#### Key Methods

The constructor `(string? modelDirectory = null, int maxSequenceLength = 128)` creates the encoder model with the
description (the base class validates the sequence length, 8 to 512) and the Basic model, and wraps both. `ModelId` and
`HiddenSize` come from the encoder model. An internal constructor `(string? modelDirectory, int maxSequenceLength, double
primaryWeight)` does the same with an explicit primary weight (the public constructor passes `PartOfSpeechPrimaryWeight`,
which is 1; the tests pin other values). `EnsureModelAsync(DownloadOptions? options = null, CancellationToken)`,
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
unit document). The part-of-speech weight of the encoder in the combination of slot probabilities (see the same unit
document) is 1 for this model (`BertBaseCasedWordSenseModel.PartOfSpeechPrimaryWeight`), so the part of speech of
its encoder is always kept: a weight below 1 gains at most 0.15 points of accuracy on the development splits (the
accuracy on all scored tokens, open dev / EWT dev, is 94.22% / 92.08% for the weight 1, 94.30% / 92.23% for 0.7 and
94.03% / 91.90% for 0) and loses where this model is strong, on the part-of-speech heteronyms of the treebank (98.8%
of the words against 99.2% for the weight 0.6, a loss of 0.4 points), and the model must never lose. A caller that
holds a `BertBaseCasedWordSenseModel` calls `await model.EnsureModelAsync();`
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

Application code and the builder (`ModelCatalog`, `weights bert-base-cased`, `evaluate bert-base-cased`).
