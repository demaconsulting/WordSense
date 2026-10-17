## BertBaseCased Subsystem Design

![BertBaseCased Structure](BertBaseCasedView.svg)

### Overview

The BertBaseCased subsystem is the package `DemaConsulting.WordSense.BertBaseCased`. It adds one public class,
`BertBaseCasedWordSenseModel`, that pins the bert-base-cased int8 encoder (cased, hidden size 768) and embeds the
weights trained for it. Because the encoder keeps the case of the text, capitalization is part of the context the
model sees. All behavior lives in the Onnx package; this package only states which model it is.

The subsystem contains one unit, **BertBaseCasedWordSenseModel**. It references the Onnx package.

### Interfaces

It exposes `BertBaseCasedWordSenseModel` with the constructor `(string? modelDirectory = null,
int maxSequenceLength = 128)`, and through its base class `EnsureModelAsync`, `ClassifyAsync`, `HiddenSize`, `ModelId` and
`Dispose`. It consumes the internal model description type of the Onnx package and the weights embedded in its own
assembly.

### Design

The class holds a static model description (model id `bert-base-cased-int8`; files `model_int8.onnx`, `vocab.txt`,
`config.json` and `tokenizer.json` with their SHA-256 digests; ONNX file `model_int8.onnx`; vocabulary `vocab.txt`;
case-preserving tokenizer; the embedded weights resource of this assembly; hidden size 768) and passes it to the base
class constructor. It adds no members and no logic. The `model_int8.onnx` file is the derived encoder export of the
model, not the upstream masked language model export. The weights embedded in the assembly were trained by the
builder for exactly this model.
