## MiniLM Subsystem Design

![MiniLM Structure](MiniLMView.svg)

### Overview

The MiniLM subsystem is the package `DemaConsulting.WordSense.MiniLM`. It adds one public class,
`MiniLMWordSenseModel`, that pins the all-MiniLM-L6-v2 int8 encoder (uncased, hidden size 384), embeds the fine-sense
weights and per-lemma thresholds trained and calibrated for it, and composes the encoder model with the Basic model.
The encoder behavior lives in the Onnx package (this package derives an internal encoder model that states which
model it is) and the composition in the core `FallbackWordSenseModel`.

The subsystem contains one unit, **MiniLMWordSenseModel**. It references the core, Onnx and Basic packages.

### Interfaces

It exposes `MiniLMWordSenseModel` with the constructor `(string? modelDirectory = null, int maxSequenceLength = 128)`,
the properties `HiddenSize` and `ModelId` and the methods `EnsureModelAsync`, `ClassifyAsync` and `Dispose` of
`IWordSenseModel`. It consumes the internal model description type of the Onnx package, the `FallbackWordSenseModel`
of the core package, `BasicWordSenseModel` of the Basic package and the weights embedded in its own assembly.

### Design

The package has an internal sealed class `MiniLMEncoderModel` that derives from `OnnxWordSenseModel` and holds a static
model description (model id `all-MiniLM-L6-v2-int8`; files `model_int8.onnx` and
`vocab.txt` with their SHA-256 digests; ONNX file `model_int8.onnx`; vocabulary `vocab.txt`; lower-casing tokenizer;
the embedded weights resource of this assembly; hidden size 384). It adds no members and no logic. The public
`MiniLMWordSenseModel` creates an encoder model and a `BasicWordSenseModel` and wraps them in a
`FallbackWordSenseModel` (the encoder is the primary model and Basic the fallback), to which it delegates
`EnsureModelAsync` (so the encoder files are ensured and Basic needs none), `ClassifyAsync` and `Dispose` (so both
models are disposed). The weights embedded in the assembly (the format `WSX3`) were trained by the builder for
exactly this model: they hold the fine-sense classifiers of the trained lemmas, the form choosers and the per-lemma fine
and group thresholds measured on the embeddings of this encoder, and they are matched against the shipped word image at
load. The encoder scores the senses of the trained lemmas and answers them at the sense, group or part-of-speech level
its thresholds allow, listing the probabilities of their analyses and senses; every word for which the encoder scored no
senses (the other ambiguous words) is answered by the Basic model at the part of speech level, with the probabilities
of its analyses. In the current version the encoder does not yet score fine senses, so every ambiguous word is
answered by the Basic model.
