### MiniLMWordSenseModel Unit Design

![MiniLM Structure](MiniLMView.svg)

#### Purpose

`MiniLMWordSenseModel` is the public model based on all-MiniLM-L6-v2, quantized to int8: the smaller and faster of
the two encoder models. It is the composition of its encoder model with the Basic model.

#### Data Model

A sealed public class that holds one `FallbackWordSenseModel`, built in the constructor from two models it creates and
owns: an internal `MiniLMEncoderModel` (the primary model, a sealed class deriving from `OnnxWordSenseModel`) and a
`BasicWordSenseModel` (the fallback model). The two are also kept in the internal properties `Encoder` and `Fallback`
for the tests. The encoder model's description: model id
`all-MiniLM-L6-v2-int8`; files `model_int8.onnx` (about 21.9 MiB; 22 MiB with `vocab.txt`) and `vocab.txt`, each with a
pinned SHA-256;
lower-casing (uncased) tokenizer; hidden size 384; embedded weights `weights.bin` (fine-sense classifiers, form
choosers and per-lemma fine and group thresholds calibrated for this encoder) trained at sequence length 128.

#### Key Methods

The constructor `(string? modelDirectory = null, int maxSequenceLength = 128)` creates the encoder model with the
description (the base class validates the sequence length, 8 to 512) and the Basic model, and wraps both. `ModelId` and
`HiddenSize` come from the encoder model. `EnsureModelAsync`, `ClassifyAsync` and `Dispose` delegate to the composite:
ensuring verifies the encoder files (the Basic model needs none), classification answers each word with the result of
the encoder model when it scored senses and otherwise with the complete result of the Basic model, and disposal
disposes both models once.

#### Error Handling

Inherited from the composite and the encoder model: the argument errors of `ClassifyAsync`, `ObjectDisposedException`
after `Dispose`, the verification and download errors of `EnsureModelAsync`, and `ArgumentOutOfRangeException` for a
sequence length outside 8 to 512 from the constructor.

#### Dependencies

`FallbackWordSenseModel` (core), `OnnxWordSenseModel` (Onnx), `BasicWordSenseModel` (Basic) and the core contracts.

#### Callers

Application code and the builder (`ModelCatalog`, `weights minilm`, `evaluate minilm`).
