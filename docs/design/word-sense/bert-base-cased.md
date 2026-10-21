## BertBaseCased Subsystem Design

![BertBaseCased Structure](BertBaseCasedView.svg)

### Overview

The BertBaseCased subsystem is the package `DemaConsulting.WordSense.BertBaseCased`. It adds one public class,
`BertBaseCasedWordSenseModel`, that pins the bert-base-cased int8 encoder (cased, hidden size 768), embeds the
fine-sense weights and per-lemma thresholds trained and calibrated for it, and composes the encoder model with the Basic
model. Because the encoder keeps the case of the
text, capitalization is part of the context the model sees. The encoder behavior lives in the Onnx package (this
package derives an internal encoder model that states which model it is) and the composition in the core
`FallbackWordSenseModel`.

The subsystem contains one unit, **BertBaseCasedWordSenseModel**. It references the core and Onnx packages.

### Interfaces

It exposes `BertBaseCasedWordSenseModel` with the constructor `(string? modelDirectory = null,
int maxSequenceLength = 128)`, the properties `HiddenSize` and `ModelId` and the methods `EnsureModelAsync`,
`ClassifyAsync` and `Dispose` of `IWordSenseModel`. It consumes the internal model description type of the Onnx
package, the `FallbackWordSenseModel` and `BasicWordSenseModel` of the core package and the weights
embedded in its own assembly.

### Design

The package has an internal sealed class `BertBaseCasedEncoderModel` that derives from `OnnxWordSenseModel` and holds a
static model description (model id `bert-base-cased-int8`; files `model_int8.onnx`, `vocab.txt`,
`config.json` and `tokenizer.json` with their SHA-256 digests; ONNX file `model_int8.onnx`; vocabulary `vocab.txt`;
case-preserving tokenizer; the embedded weights resource of this assembly; hidden size 768). It adds no members and no
logic. The `model_int8.onnx` file is the derived encoder export of the
model, not the upstream masked language model export. The public `BertBaseCasedWordSenseModel` creates an encoder model
and a `BasicWordSenseModel` and wraps them in a `FallbackWordSenseModel` (the encoder is the primary model and Basic the
fallback), to which it delegates `EnsureModelAsync` (so the encoder files are ensured and Basic needs none),
`ClassifyAsync` and `Dispose` (so both models are disposed). The weights embedded in the assembly (the format `WSX3`)
were trained by the builder for exactly this model: they hold the fine-sense classifiers of the trained lemmas, the form
choosers and the per-lemma fine and group thresholds measured on the embeddings of this encoder, and they are matched
against the shipped word image at load. The encoder scores the senses of the trained lemmas and answers them at the
sense, group or part-of-speech level its thresholds allow, listing the probabilities of their analyses and senses;
every word for which the encoder scored no senses (the other ambiguous words) is answered by the Basic model at the part
of speech level, with the probabilities of its analyses. In the current version the encoder does not yet score fine
senses, so every ambiguous word is answered by the Basic model.
