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
length 128. The `model_int8.onnx` file is the derived encoder export, not the upstream masked language model export.

#### Key Methods

The constructor `(string? modelDirectory = null, int maxSequenceLength = 128)` creates the encoder model with the
description (the base class validates the sequence length, 8 to 512) and the Basic model, and wraps both. `ModelId` and
`HiddenSize` come from the encoder model. `EnsureModelAsync(DownloadOptions? options = null, CancellationToken)`,
`ClassifyAsync` and `Dispose` delegate to the composite:
ensuring is optional (`ClassifyAsync` ensures lazily) and verifies or downloads the encoder files (the Basic model
needs none and is skipped by the composite), classification answers each word with the result of
the encoder model when it scored senses and otherwise with the complete result of the Basic model, and disposal
disposes both models once. A caller that holds a `BertBaseCasedWordSenseModel` calls `await model.EnsureModelAsync();`
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
